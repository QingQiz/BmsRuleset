using System.Collections.Generic;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Mixing;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing;

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
    private readonly BmsSampleTrackRegistry trackRegistry = new(sampleDefinitions, basePath, rate, sampleUsages);

    private AudioMixer? mixer;
    private BmsSamplePlaybackController? playbackController;

    [Resolved]
    private AudioManager audioManager { get; set; } = null!;

    public double MaxTrackLengthMilliseconds => trackRegistry.MaxTrackLengthMilliseconds;

    internal AudioMixer? Mixer => mixer;

    protected override void Dispose(bool isDisposing)
    {
        playbackController?.Dispose();
        playbackController = null;
        trackRegistry.Dispose();
        mixer?.Dispose();
        mixer = null;
        if (audioManager != null)
            BmsKeysoundMixerPatcher.UnbindGlobalMixer(audioManager);
        base.Dispose(isDisposing);
    }

    internal Track? GetTrack(ushort sampleKey) => trackRegistry.GetTrack(sampleKey);

    internal double GetTrackLength(ushort sampleKey) => trackRegistry.GetTrackLength(sampleKey);

    internal bool HasSampleDefinition(ushort sampleKey) => trackRegistry.HasSampleDefinition(sampleKey);

    internal bool SupportsScheduling => playbackController?.SupportsScheduling == true;

    internal void QueueLivePlay(ushort sampleKey, int volume = 100) => playbackController?.QueueLivePlay(sampleKey, volume);

    internal void SubmitLivePlayBatch() => playbackController?.SubmitLivePlayBatch();

    internal void Play(ushort sampleKey, int volume = 100, double offset = 0) => playbackController?.Play(sampleKey, volume, offset);

    internal bool CanSchedule(ushort sampleKey) => playbackController?.CanSchedule(sampleKey) == true;

    internal string GetScheduleDiagnostic(ushort sampleKey) => playbackController?.GetScheduleDiagnostic(sampleKey) ?? string.Empty;

    internal void SchedulePlay(ushort sampleKey, int volume, double delay, double targetTime) =>
        playbackController?.SchedulePlay(sampleKey, volume, delay, targetTime);

    internal void SetPlaybackBlocked(bool blocked) => playbackController?.SetPlaybackBlocked(blocked);

    internal void ResumeAll() => playbackController?.ResumeAll();

    internal void StopAll() => playbackController?.StopAll();

    protected override void Update()
    {
        base.Update();

        var now = Time.Current;
        var completedLoads = trackRegistry.Update(now);
        playbackController?.Update(completedLoads, now);
    }

    [BackgroundDependencyLoader]
    private void load(CancellationToken? cancellationToken)
    {
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

        trackRegistry.Initialise(audioManager, mixer, cancellationToken ?? CancellationToken.None, Time.Current);
    }
}
