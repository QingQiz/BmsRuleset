using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;
using osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Filter;

internal sealed class BmsDifficultyRefresh(
    BmsSongSelectDifficultyCache cache,
    Func<BeatmapInfo, Mod[], CancellationToken, Task<double?>> calculateDifficulty) : IDisposable
{
    internal event Action<Progress>? ProgressChanged;

    internal Task ProcessingTask
    {
        get
        {
            lock (stateLock)
                return processingTask;
        }
    }

    internal readonly record struct Progress(CancellationToken RequestToken, int Processed, int Total, int Failed, bool Finished);

    private const int progress_interval_ms = 10000;

    // Bound simultaneous decoded/playable charts and leave capacity for song select rendering.
    internal static int CalculationConcurrency => Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

    private readonly SemaphoreSlim runLock = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private BeatmapInfo[] library = [];
    private readonly Lock stateLock = new();
    private Task processingTask = Task.CompletedTask;
    private CancellationTokenSource? activeCalculation;
    private TaskCompletionSource<CancellationToken> activePeriod = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Refresh(IEnumerable<BeatmapInfo> beatmaps)
    {
        var charts = beatmaps.Where(beatmap => !UnavailableTableBeatmapFactory.IsUnavailable(beatmap)
                                               && (beatmap.Ruleset.ShortName == Constant.SHORT_NAME || Mania7KBeatmapConverter.CanConvert(beatmap)))
            .DistinctBy(beatmap => beatmap.Hash).ToArray();
        lock (stateLock)
        {
            // Score history and display metadata updates do not change difficulty inputs.
            if (charts.Select(beatmap => beatmap.Hash).ToHashSet().SetEquals(library.Select(beatmap => beatmap.Hash)))
                return;

            library = charts;
            request?.Cancel();
            request?.Dispose();
            request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var token = request.Token;
            // Even cancellation before startup must publish the final notification state.
            processingTask = Task.Run(() => refresh(charts, token), CancellationToken.None);
        }
    }

    internal bool IsCurrent(CancellationToken token)
    {
        lock (stateLock)
            return request?.Token == token;
    }

    internal void Cancel(CancellationToken token)
    {
        lock (stateLock)
        {
            // A delayed click on a superseded notification must not cancel its replacement.
            if (request?.Token == token)
                request.Cancel();
        }
    }

    internal void Resume()
    {
        lock (stateLock)
        {
            if (activeCalculation != null)
                return;

            activeCalculation = new CancellationTokenSource();
            activePeriod.SetResult(activeCalculation.Token);
        }
    }

    internal void Pause()
    {
        CancellationTokenSource? previous;
        lock (stateLock)
        {
            previous = activeCalculation;
            if (previous == null)
                return;

            activeCalculation = null;
            activePeriod = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        previous.Cancel();
        previous.Dispose();
    }

    private async Task refresh(BeatmapInfo[] charts, CancellationToken token)
    {
        try
        {
            await runLock.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ProgressChanged?.Invoke(new Progress(token, 0, 0, 0, true));
            return;
        }

        var processed = 0;
        var total = 0;
        var failed = 0;
        var progressLock = new Lock();
        try
        {
            token.ThrowIfCancellationRequested();
            var missing = charts.Where(beatmap => !cache.Contains(beatmap.Hash, true)
                                                  || (beatmap.Ruleset.ShortName != Constant.SHORT_NAME && !cache.Contains(beatmap.Hash, false))).ToArray();
            total = missing.Length;
            if (total == 0)
                return;

            ProgressChanged?.Invoke(new Progress(token, 0, total, 0, false));
            var progressTimer = Stopwatch.StartNew();
            await Parallel.ForEachAsync(missing, new ParallelOptions
            {
                MaxDegreeOfParallelism = CalculationConcurrency,
                CancellationToken = token,
            }, async (beatmap, workerToken) =>
            {
                workerToken.ThrowIfCancellationRequested();
                var native = beatmap.Ruleset.ShortName == Constant.SHORT_NAME;
                var stars = !native && cache.Contains(beatmap.Hash, false)
                    ? cache.GetStars(beatmap, false)
                    : await calculate(beatmap, native, workerToken).ConfigureAwait(false);

                workerToken.ThrowIfCancellationRequested();
                if (stars.HasValue)
                {
                    if (!native)
                        cache.Store(beatmap.Hash, false, stars.Value);
                    // Converted 7K charts have no scratch notes, so HS shares the calculated base value.
                    cache.Store(beatmap.Hash, true, stars.Value);
                }

                lock (progressLock)
                {
                    // The outer failure handler runs only after all parallel workers have stopped.
                    // ReSharper disable once AccessToModifiedClosure
                    if (!stars.HasValue)
                        failed++;
                    processed++;

                    // Fast calculations must not repeatedly sort the entire library in one frame.
                    if (processed < total && progressTimer.ElapsedMilliseconds >= progress_interval_ms)
                    {
                        cache.Flush();
                        // Notifications identify the refresh request, not the parallel loop's linked token.
                        // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                        ProgressChanged?.Invoke(new Progress(token, processed, total, failed, false));
                        progressTimer.Restart();
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Flush and publish completed records even when fewer than one update interval elapsed.
        }
        catch (Exception exception)
        {
            lock (progressLock)
                failed++;
            BmsLogger.Error(exception, "Failed to refresh BMS song-select difficulties.");
        }
        finally
        {
            cache.Flush();
            lock (progressLock)
                ProgressChanged?.Invoke(new Progress(token, processed, total, failed, true));
            runLock.Release();
        }
    }

    private async Task<double?> calculate(BeatmapInfo beatmap, bool hideScratch, CancellationToken token)
    {
        while (true)
        {
            Task<CancellationToken> period;
            lock (stateLock)
                period = activePeriod.Task;

            var activeToken = await period.WaitAsync(token).ConfigureAwait(false);
            using var calculation = CancellationTokenSource.CreateLinkedTokenSource(token, activeToken);
            try
            {
                calculation.Token.ThrowIfCancellationRequested();
                Mod[] mods = hideScratch ? [new BmsModHideScratch()] : [];
                var difficulty = await calculateDifficulty(beatmap, mods, calculation.Token).ConfigureAwait(false);
                calculation.Token.ThrowIfCancellationRequested();
                return difficulty;
            }
            catch (OperationCanceledException) when (activeToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                cache.Flush();
                // Await the next active period, retrying only this interrupted chart.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                BmsLogger.Error(exception, $"Failed to calculate BMS difficulty for {beatmap}.");
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (stateLock)
        {
            lifetime.Cancel();
            request?.Dispose();
            request = null;
            lifetime.Dispose();
        }

        Pause();
    }
}
