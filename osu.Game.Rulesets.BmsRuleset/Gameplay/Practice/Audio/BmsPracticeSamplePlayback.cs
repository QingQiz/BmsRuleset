using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;

internal partial class BmsPracticeSamplePlayback(BmsBeatmap beatmap, IReadOnlyList<Mod> mods)
    : BmsSamplePlayback(beatmap.SampleDefinitions, BmsGameplayAudioController.ResolveBeatmapSource(beatmap),
        BmsGameplayAudioController.GetPlaybackRate(mods))
{
    private Task<BmsPcmPlaybackSession?>? preparation;

    internal bool IsPreparationSuccessful => preparation?.IsCompletedSuccessfully == true;

    internal override void LoadSamples(CancellationToken cancellationToken)
    {
        // The actual preroll time depends on the loaded playfield and skin. Resolve it before
        // starting any historical PCM decoding, including when a sample is reused much later.
    }

    internal Task PrepareAsync(double targetTime, BmsSampleDurationCache durations, CancellationToken cancellationToken)
        => preparation = Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var usages = BmsGameplayAudioController.GetSampleUsages(beatmap).ToArray();
            var plan = new BmsPracticeAudioPreparation(usages);
            using var store = new BmsAudioResourceStore(BmsGameplayAudioController.ResolveBeatmapSource(beatmap), cancellationToken);
            var resources = beatmap.SampleDefinitions.Where(pair => !string.IsNullOrEmpty(pair.Value.Path))
                .Select(pair => (pair.Key, Path: store.ResolveSamplePath(pair.Value.Path)))
                .Where(pair => pair.Path != null).ToDictionary(pair => pair.Key, pair => pair.Path!);
            var candidates = plan.GetDurationResources(targetTime, resources);
            var result = await durations.GetAsync(candidates.Values, cancellationToken).ConfigureAwait(false);
            plan.SetDurations(candidates.ToDictionary(pair => pair.Key, pair =>
            {
                var duration = result[pair.Value];
                return duration.State == BmsSampleDurationState.Known
                    ? duration with { Milliseconds = beatmap.SampleDefinitions[pair.Key].GetLength(duration.Milliseconds) }
                    : duration;
            }));
            cancellationToken.ThrowIfCancellationRequested();

            var prepared = CreatePlaybackSession(plan.GetPlaybackUsages(usages, targetTime));
            try
            {
                prepared.Initialise(cancellationToken, targetTime, waitForInitialAssets: false, knownLengths: plan.KnownLengths);
                if (prepared.Controller is { } controller)
                {
                    try
                    {
                        await controller.WaitForPreparedSamplesAsync(cancellationToken,
                            plan.GetResumeOffsets(targetTime)).ConfigureAwait(false);
                    }
                    catch (TimeoutException exception)
                    {
                        // Keep the decoder alive so pending playback can recover after contention.
                        BmsLogger.LogAudioFailure("Timed out while preparing BMS practice seek buffers.", exception);
                    }

                    return prepared;
                }

                prepared.Dispose();
                return null;
            }
            catch
            {
                prepared.Dispose();
                throw;
            }
        }, cancellationToken);

    internal void ApplyPreparedSamples()
    {
        var prepared = preparation!.GetAwaiter().GetResult();
        playbackSession?.Dispose();
        playbackSession = prepared;
        preparation = null;
    }

    protected override void Dispose(bool isDisposing)
    {
        // Player cancellation happens before disposal. Join preparation before releasing audio
        // dependencies so a late completion cannot leave an unattached native session alive.
        if (preparation != null)
        {
            try
            {
                preparation.GetAwaiter().GetResult()?.Dispose();
            }
            catch (Exception)
            {
                // The player's preparation continuation reports failures; disposal only joins it.
            }

            preparation = null;
        }

        base.Dispose(isDisposing);
    }
}
