using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

/// <summary>
///     Provides the gameplay-facing sample playback facade.
/// </summary>
/// <remarks>
///     PCM backend ownership lives in <see cref="BmsPcmPlaybackSession"/> so this Component only
///     coordinates gameplay calls with the session lifecycle.
/// </remarks>
public partial class BmsSamplePlayback(
    IReadOnlyDictionary<ushort, string> sampleDefinitions,
    string? basePath = null,
    double rate = 1.0,
    IEnumerable<BmsSampleUsage>? sampleUsages = null) : Component
{
    private BmsPcmPlaybackSession? playbackSession;

    private BmsPcmPlaybackController? controller => playbackSession?.Controller;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public double MaxSampleLengthMilliseconds => controller?.MaxSampleLengthMilliseconds ?? 0;

    internal bool IsSampleReady(ushort sampleKey) => controller?.IsSampleReady(sampleKey) == true;

    protected override void Dispose(bool isDisposing)
    {
        playbackSession?.Dispose();
        playbackSession = null;
        base.Dispose(isDisposing);
    }

    internal double GetSampleLength(ushort sampleKey) => controller?.GetSampleLength(sampleKey) ?? 0;

    internal bool HasSampleDefinition(ushort sampleKey) => controller?.HasSampleDefinition(sampleKey) == true;

    internal bool IsPlaybackAvailable => controller != null;

    internal void QueueLivePlay(ushort sampleKey, int volume = 100) => controller?.QueueLivePlay(sampleKey, volume);

    internal void SubmitLivePlayBatch() => controller?.SubmitLivePlayBatch();

    internal void Play(ushort sampleKey, int volume = 100, double offset = 0) => controller?.Play(sampleKey, volume, offset);

    internal bool CanSchedule(ushort sampleKey) => controller?.CanSchedule(sampleKey) == true;

    internal void SchedulePlay(ushort sampleKey, int volume, double targetTime) =>
        controller?.SchedulePlay(sampleKey, volume, targetTime);

    internal void SetPlaybackBlocked(bool blocked)
    {
        controller?.SetPlaybackBlocked(blocked);
    }

    internal void ResumeAll()
    {
        controller?.ResumeAll();
    }

    internal void StopAll()
    {
        controller?.StopAll();
    }

    protected override void Update()
    {
        base.Update();

        var now = Time.Current;
        playbackSession?.Update(now);
    }

    [BackgroundDependencyLoader]
    private void load(CancellationToken? cancellationToken)
    {
        try
        {
            playbackSession = new BmsPcmPlaybackSession(
                sampleDefinitions,
                basePath,
                rate,
                sampleUsages,
                audioManager,
                () => Time.Current);
            playbackSession.Initialise(cancellationToken ?? CancellationToken.None, Time.Current);

            if (playbackSession.IsInitialised)
                return;
        }
        catch (Exception exception)
        {
            BmsLogger.LogAudioFailure("Failed to initialise BMS PCM playback. Gameplay samples will be unavailable.", exception);
        }

        playbackSession?.Dispose();
        playbackSession = null;
    }
}
