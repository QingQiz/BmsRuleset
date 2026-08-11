using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
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
///     PCM loading and playback state live in dedicated collaborators. This class remains the
///     Component-facing facade for gameplay-facing requests.
/// </remarks>
public partial class BmsSampleStore(
    IReadOnlyDictionary<ushort, string> sampleDefinitions,
    string? basePath = null,
    double rate = 1.0,
    IEnumerable<BmsSampleUsage>? sampleUsages = null) : Component
{
    private AudioMixer? mixer;
    private BmsPcmVoiceMixer? pcmMixer;
    private BmsPcmPlaybackController? pcmPlaybackController;
    private BmsBassMixerBridge? pcmBridge;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public double MaxSampleLengthMilliseconds => pcmPlaybackController?.MaxSampleLengthMilliseconds ?? 0;

    internal AudioMixer? Mixer => mixer;

    internal BmsAudioDiagnostics Diagnostics => pcmMixer?.GetDiagnostics() ?? default;

    internal BmsPcmAssetCacheDiagnostics CacheDiagnostics => pcmPlaybackController?.GetCacheDiagnostics() ?? default;

    internal bool IsSampleReady(ushort sampleKey) => pcmPlaybackController?.IsSampleReady(sampleKey) == true;

    internal long PreloadUnderflows => pcmPlaybackController?.PreloadUnderflows ?? 0;

    internal long BridgeCallbackFailures => pcmBridge?.CallbackFailures ?? 0;

    protected override void Dispose(bool isDisposing)
    {
        pcmPlaybackController?.Dispose();
        pcmPlaybackController = null;
        pcmBridge?.Dispose();
        pcmBridge = null;
        pcmMixer = null;
        mixer?.Dispose();
        mixer = null;
        base.Dispose(isDisposing);
    }

    internal double GetSampleLength(ushort sampleKey) => pcmPlaybackController?.GetSampleLength(sampleKey) ?? 0;

    internal bool HasSampleDefinition(ushort sampleKey) => pcmPlaybackController?.HasSampleDefinition(sampleKey) == true;

    internal bool SupportsScheduling => pcmPlaybackController != null;

    internal void QueueLivePlay(ushort sampleKey, int volume = 100) => pcmPlaybackController?.QueueLivePlay(sampleKey, volume);

    internal void SubmitLivePlayBatch() => pcmPlaybackController?.SubmitLivePlayBatch();

    internal void Play(ushort sampleKey, int volume = 100, double offset = 0) => pcmPlaybackController?.Play(sampleKey, volume, offset);

    internal bool CanSchedule(ushort sampleKey) => pcmPlaybackController?.CanSchedule(sampleKey) == true;

    internal string GetScheduleDiagnostic() => pcmPlaybackController == null
        ? "pcm_state=unavailable"
        : $"pcm_state={pcmPlaybackController.GetCacheDiagnostics()}";

    internal void SchedulePlay(ushort sampleKey, int volume, double targetTime) =>
        pcmPlaybackController?.SchedulePlay(sampleKey, volume, targetTime);

    internal void SetPlaybackBlocked(bool blocked)
    {
        pcmPlaybackController?.SetPlaybackBlocked(blocked);
    }

    internal void ResumeAll()
    {
        pcmPlaybackController?.ResumeAll();
    }

    internal void StopAll()
    {
        pcmPlaybackController?.StopAll();
    }

    protected override void Update()
    {
        base.Update();

        var now = Time.Current;
        pcmBridge?.EnsureAttached();
        pcmPlaybackController?.Update(now);
    }

    [BackgroundDependencyLoader]
    private void load(CancellationToken? cancellationToken)
    {
        BmsPcmMixerPatcher.InstallOnce();

        if (!BmsPcmMixerPatcher.IsInstalled)
            return;

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
            BmsLogger.LogAudioFailure("Failed to initialise BMS PCM playback. Gameplay samples will be unavailable.", exception);
        }

        pcmPlaybackController?.Dispose();
        pcmPlaybackController = null;
        pcmBridge?.Dispose();
        pcmBridge = null;
        pcmMixer = null;
        mixer?.Dispose();
        mixer = null;
    }
}
