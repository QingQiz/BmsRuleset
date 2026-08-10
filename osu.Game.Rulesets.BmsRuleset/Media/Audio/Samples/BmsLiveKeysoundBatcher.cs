using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

internal readonly record struct BmsPreparedLivePlay(Track Track, Track? TrackToStop);

/// <summary>
///     Collects player-triggered keysounds and submits each ruleset update as one native mixer
///     action. The owner remains responsible for selecting the Track and updating playback state.
/// </summary>
internal sealed class BmsLiveKeysoundBatcher(
    AudioMixer? mixer,
    object syncRoot,
    Func<bool> playbackBlocked,
    Func<bool> supportsScheduling,
    Func<ushort, int, BmsPreparedLivePlay?> preparePlay,
    Action<ushort, int> fallbackPlay,
    Action<Track, double> markTrackStopped)
    : IDisposable
{
    private readonly List<PendingLivePlay> pendingPlays = [];
    private readonly Queue<PendingBatch> pendingBatches = new();
    private readonly Dictionary<Track, int> pendingTracks = [];
    private bool processing;
    private bool disposed;

    public bool IsTrackPending(Track track)
    {
        lock (syncRoot)
            return pendingTracks.ContainsKey(track);
    }

    public void Queue(ushort sampleKey, int volume)
    {
        lock (syncRoot)
        {
            if (disposed || playbackBlocked())
                return;

            pendingPlays.Add(new PendingLivePlay(sampleKey, volume));
        }
    }

    public void Submit(double targetTime)
    {
        PendingLivePlay[] plays;

        lock (syncRoot)
        {
            plays = pendingPlays.ToArray();
            pendingPlays.Clear();
        }

        if (plays.Length == 0 || playbackBlocked())
            return;

        if (!supportsScheduling())
        {
            foreach (var play in plays)
                fallbackPlay(play.SampleKey, play.Volume);

            return;
        }

        List<PendingBatchPlay> batchPlays = [];

        foreach (var play in plays)
        {
            if (preparePlay(play.SampleKey, play.Volume) is { } prepared)
            {
                // Reserve the voice before preparing the next item. Without this, repeated
                // same-key events in one frame can select the same Track twice and the second
                // native insertion resets the first event before it becomes audible.
                lock (syncRoot)
                    pendingTracks[prepared.Track] = pendingTracks.GetValueOrDefault(prepared.Track) + 1;

                batchPlays.Add(new PendingBatchPlay(prepared.Track, prepared.TrackToStop));
                continue;
            }

            fallbackPlay(play.SampleKey, play.Volume);
        }

        if (batchPlays.Count == 0)
            return;

        var batch = new PendingBatch(targetTime, batchPlays.ToArray());
        var startProcessing = false;

        lock (syncRoot)
        {
            if (disposed)
                return;

            pendingBatches.Enqueue(batch);

            if (!processing)
            {
                processing = true;
                startProcessing = true;
            }
        }

        if (startProcessing)
            _ = processBatches();
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            pendingPlays.Clear();
            pendingBatches.Clear();
            pendingTracks.Clear();
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            disposed = true;
            pendingPlays.Clear();
            pendingBatches.Clear();
            pendingTracks.Clear();
        }
    }

    private async Task processBatches()
    {
        while (true)
        {
            PendingBatch batch;

            lock (syncRoot)
            {
                if (disposed || pendingBatches.Count == 0)
                {
                    processing = false;
                    return;
                }

                batch = pendingBatches.Dequeue();
            }

            try
            {
                var items = batch.Plays
                    .Select(play => new BmsMixerBatchItem(play.Track, play.TrackToStop))
                    .ToArray();
                var results = await BmsKeysoundMixerPatcher.TryScheduleBatchAsync(mixer, items, batch.TargetTime).ConfigureAwait(false);

                for (var i = 0; i < batch.Plays.Length; i++)
                {
                    var play = batch.Plays[i];
                    var result = results[i];

                    if (result.TrackScheduled)
                    {
                        if (result.TrackStopped && play.TrackToStop != null)
                            markTrackStopped(play.TrackToStop, batch.TargetTime);
                        else if (play.TrackToStop != null && !result.TrackStopped)
                            await play.TrackToStop.StopAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        if (play.TrackToStop != null)
                            await play.TrackToStop.StopAsync().ConfigureAwait(false);

                        // Prefer the direct restart path: Track.SeekAsync() flushes the shared
                        // mixer on some platforms and can erase other voices from this batch.
                        if (!await BmsKeysoundMixerPatcher.TryRestartTrackAsync(mixer, play.Track, 0).ConfigureAwait(false))
                        {
                            await play.Track.SeekAsync(0).ConfigureAwait(false);
                            BmsKeysoundMixerPatcher.TryApplyTailRamp(mixer, play.Track, 0);
                            await play.Track.StartAsync().ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                BmsLogger.Error(exception, "Failed to execute a live BMS keysound batch.");
            }
            finally
            {
                lock (syncRoot)
                {
                    foreach (var play in batch.Plays)
                    {
                        if (!pendingTracks.TryGetValue(play.Track, out var count) || count <= 1)
                            pendingTracks.Remove(play.Track);
                        else
                            pendingTracks[play.Track] = count - 1;
                    }
                }
            }
        }
    }

    private readonly record struct PendingBatch(double TargetTime, PendingBatchPlay[] Plays);

    private readonly record struct PendingLivePlay(ushort SampleKey, int Volume);

    private readonly record struct PendingBatchPlay(Track Track, Track? TrackToStop);
}
