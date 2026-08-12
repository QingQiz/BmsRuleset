using System;
using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;
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

internal readonly record struct BmsSampleStoreDiagnostics(
    BmsAudioDiagnostics Audio,
    BmsPcmAssetCacheDiagnostics Cache,
    long PreloadUnderflows,
    long BridgeCallbackFailures);

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
    private BmsPcmVoiceMixer? pcmMixer;
    private BmsPcmPlaybackController? pcmPlaybackController;
    private BmsBassMixerBridge? pcmBridge;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public double MaxSampleLengthMilliseconds => pcmPlaybackController?.MaxSampleLengthMilliseconds ?? 0;

    internal AudioMixer? DiagnosticMixer { get; private set; }

    internal BmsSampleStoreDiagnostics DiagnosticSnapshot => new(
        pcmMixer?.GetDiagnostics() ?? default,
        pcmPlaybackController?.GetCacheDiagnostics() ?? default,
        pcmPlaybackController?.PreloadUnderflows ?? 0,
        pcmBridge?.CallbackFailures ?? 0);

    internal bool IsSampleReady(ushort sampleKey) => pcmPlaybackController?.IsSampleReady(sampleKey) == true;

    protected override void Dispose(bool isDisposing)
    {
        pcmPlaybackController?.Dispose();
        pcmPlaybackController = null;
        pcmBridge?.Dispose();
        pcmBridge = null;
        pcmMixer = null;
        DiagnosticMixer?.Dispose();
        DiagnosticMixer = null;
        base.Dispose(isDisposing);
    }

    internal double GetSampleLength(ushort sampleKey) => pcmPlaybackController?.GetSampleLength(sampleKey) ?? 0;

    internal bool HasSampleDefinition(ushort sampleKey) => pcmPlaybackController?.HasSampleDefinition(sampleKey) == true;

    internal bool IsPlaybackAvailable => pcmPlaybackController != null;

    internal void QueueLivePlay(ushort sampleKey, int volume = 100) => pcmPlaybackController?.QueueLivePlay(sampleKey, volume);

    internal void SubmitLivePlayBatch() => pcmPlaybackController?.SubmitLivePlayBatch();

    internal void Play(ushort sampleKey, int volume = 100, double offset = 0) => pcmPlaybackController?.Play(sampleKey, volume, offset);

    internal bool CanSchedule(ushort sampleKey) => pcmPlaybackController?.CanSchedule(sampleKey) == true;

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
            DiagnosticMixer = audioManager.CreateAudioMixer(BmsPcmMixerPatcher.MIXER_IDENTIFIER);
            pcmMixer = new BmsPcmVoiceMixer();
            pcmBridge = new BmsBassMixerBridge(DiagnosticMixer, pcmMixer);
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
        DiagnosticMixer?.Dispose();
        DiagnosticMixer = null;
    }
}
