using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Native;

namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

public readonly record struct BmsSampleUsage(
    ushort SampleKey,
    double Time,
    double? CandidateStartTime = null,
    double? CandidateEndTime = null)
{
    public double EarliestTriggerTime => CandidateStartTime ?? Time;
    public double LatestTriggerTime => CandidateEndTime ?? Time;
}

/// <summary>
///     Coordinates BMS sample resources and playback for the ruleset.
/// </summary>
/// <remarks>
///     Track loading and playback state live in dedicated collaborators. This class remains the
///     Component-facing facade so callers do not need to know which subsystem owns a request.
/// </remarks>
public partial class BmsSampleStore(
    IReadOnlyDictionary<ushort, string> sampleDefinitions,
    string? basePath = null,
    double rate = 1.0,
    IEnumerable<BmsSampleUsage>? sampleUsages = null) : Component
{
    private AudioMixer? mixer;
    private BmsSampleTrackRegistry? trackRegistry;
    private BmsSamplePlaybackController? playbackController;
    private BmsPcmVoiceMixer? pcmMixer;
    private BmsPcmPlaybackController? pcmPlaybackController;
    private BmsBassMixerBridge? pcmBridge;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public double MaxTrackLengthMilliseconds =>
        pcmPlaybackController?.MaxTrackLengthMilliseconds
        ?? trackRegistry?.MaxTrackLengthMilliseconds
        ?? 0;

    internal AudioMixer? Mixer => mixer;

    internal bool UsesPcmBackend => pcmPlaybackController != null;

    internal BmsAudioDiagnostics PcmDiagnostics => pcmMixer?.GetDiagnostics() ?? default;

    internal BmsPcmAssetCacheDiagnostics PcmCacheDiagnostics => pcmPlaybackController?.GetCacheDiagnostics() ?? default;

    internal bool IsPcmSampleReady(ushort sampleKey) => pcmPlaybackController?.IsSampleReady(sampleKey) == true;

    internal long PcmPreloadUnderflows => pcmPlaybackController?.PreloadUnderflows ?? 0;

    internal long PcmBridgeCallbackFailures => pcmBridge?.CallbackFailures ?? 0;

    protected override void Dispose(bool isDisposing)
    {
        playbackController?.Dispose();
        playbackController = null;
        pcmPlaybackController?.Dispose();
        pcmPlaybackController = null;
        pcmBridge?.Dispose();
        pcmBridge = null;
        pcmMixer = null;
        trackRegistry?.Dispose();
        trackRegistry = null;
        mixer?.Dispose();
        mixer = null;
        if (audioManager != null)
            BmsKeysoundMixerPatcher.UnbindGlobalMixer(audioManager);
        base.Dispose(isDisposing);
    }

    internal Track? GetTrack(ushort sampleKey) => trackRegistry?.GetTrack(sampleKey);

    internal double GetTrackLength(ushort sampleKey) =>
        pcmPlaybackController?.GetTrackLength(sampleKey)
        ?? trackRegistry?.GetTrackLength(sampleKey)
        ?? 0;

    internal bool HasSampleDefinition(ushort sampleKey) =>
        pcmPlaybackController?.HasSampleDefinition(sampleKey)
        ?? trackRegistry?.HasSampleDefinition(sampleKey)
        ?? false;

    internal bool SupportsScheduling => pcmPlaybackController != null || playbackController?.SupportsScheduling == true;

    internal void QueueLivePlay(ushort sampleKey, int volume = 100)
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.QueueLivePlay(sampleKey, volume);
        else
            playbackController?.QueueLivePlay(sampleKey, volume);
    }

    internal void SubmitLivePlayBatch()
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.SubmitLivePlayBatch();
        else
            playbackController?.SubmitLivePlayBatch();
    }

    internal void Play(ushort sampleKey, int volume = 100, double offset = 0)
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.Play(sampleKey, volume, offset);
        else
            playbackController?.Play(sampleKey, volume, offset);
    }

    internal bool CanSchedule(ushort sampleKey) =>
        pcmPlaybackController?.CanSchedule(sampleKey)
        ?? playbackController?.CanSchedule(sampleKey)
        ?? false;

    internal string GetScheduleDiagnostic(ushort sampleKey) => pcmPlaybackController != null
        ? $"pcm_state={pcmPlaybackController.GetCacheDiagnostics()}"
        : playbackController?.GetScheduleDiagnostic(sampleKey) ?? string.Empty;

    internal void SchedulePlay(ushort sampleKey, int volume, double delay, double targetTime)
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.SchedulePlay(sampleKey, volume, targetTime);
        else
            playbackController?.SchedulePlay(sampleKey, volume, delay, targetTime);
    }

    internal void SetPlaybackBlocked(bool blocked)
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.SetPlaybackBlocked(blocked);
        else
            playbackController?.SetPlaybackBlocked(blocked);
    }

    internal void ResumeAll()
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.ResumeAll();
        else
            playbackController?.ResumeAll();
    }

    internal void StopAll()
    {
        if (pcmPlaybackController != null)
            pcmPlaybackController.StopAll();
        else
            playbackController?.StopAll();
    }

    protected override void Update()
    {
        base.Update();

        var now = Time.Current;
        pcmBridge?.EnsureAttached();
        pcmPlaybackController?.Update(now);

        if (trackRegistry != null)
        {
            var completedLoads = trackRegistry.Update(now);
            playbackController?.Update(completedLoads, now);
        }
    }

    [BackgroundDependencyLoader]
    private void load(CancellationToken? cancellationToken)
    {
        BmsPcmMixerPatcher.InstallOnce();

        if (BmsPcmMixerPatcher.IsInstalled)
        {
            try
            {
                mixer = audioManager.CreateAudioMixer(BmsPcmMixerPatcher.MIXER_IDENTIFIER);
                pcmMixer = new BmsPcmVoiceMixer();
                pcmBridge = new BmsBassMixerBridge(mixer, pcmMixer);
                pcmPlaybackController = new BmsPcmPlaybackController(
                    sampleDefinitions,
                    basePath,
                    rate,
                    sampleUsages,
                    audioManager.AggregateVolume,
                    () => Time.Current,
                    pcmMixer);
                pcmPlaybackController.Initialise(cancellationToken ?? CancellationToken.None, Time.Current);
                pcmBridge.EnsureAttached();

                if (pcmPlaybackController.IsInitialised)
                    return;
            }
            catch (Exception exception)
            {
                BmsLogger.LogAudioFailure("Failed to initialise the BMS PCM playback backend. Falling back to framework Tracks.", exception);
            }

            pcmPlaybackController?.Dispose();
            pcmPlaybackController = null;
            pcmBridge?.Dispose();
            pcmBridge = null;
            pcmMixer = null;
            mixer?.Dispose();
            mixer = null;
        }

        initialiseTrackBackend(cancellationToken ?? CancellationToken.None);
    }

    private void initialiseTrackBackend(CancellationToken cancellationToken)
    {
        trackRegistry = new BmsSampleTrackRegistry(sampleDefinitions, basePath, rate, sampleUsages);
        BmsKeysoundMixerPatcher.InstallOnce();
        BmsKeysoundMixerPatcher.BindGlobalMixer(audioManager);

        if (BmsKeysoundMixerPatcher.EnableReverseStreamWorkaround)
            BmsTrackAudioPatcher.InstallOnce();

        if (BmsKeysoundMixerPatcher.IsInstalled)
            mixer = audioManager.CreateAudioMixer(BmsKeysoundMixerPatcher.MIXER_IDENTIFIER);

        playbackController = new BmsSamplePlaybackController(
            trackRegistry,
            mixer,
            audioManager.AggregateVolume,
            rate,
            () => Time.Current);

        trackRegistry.Initialise(audioManager, mixer, cancellationToken, Time.Current);
    }
}
