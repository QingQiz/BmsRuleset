using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplayAudioController
{
    private BmsPreviewTrack? previewTrackBeforePlay;
    private bool stoppedPreviewForGameplay;

    internal BmsSamplePlayback SamplePlayback { get; }

    internal BindableBool BackgroundAudioPaused { get; } = new(true);

    internal BmsGameplayAudioController(BmsBeatmap beatmap, IReadOnlyList<Mod>? mods)
    {
        SamplePlayback = new BmsSamplePlayback(
            beatmap.SampleDefinitions,
            ResolveBeatmapSource(beatmap),
            GetPlaybackRate(mods),
            getSampleUsages(beatmap));
    }

    internal BmsBackgroundAudioPlayer CreateBackgroundAudioPlayer(BmsBeatmap beatmap)
    {
        var events = beatmap.BackgroundSampleEvents
            .OrderBy(evt => evt.Time)
            .Where(evt => beatmap.SampleDefinitions.ContainsKey(evt.SampleKey))
            .Select(evt => new BmsBackgroundAudioPlayer.BgmEvent(evt.Time, evt.SampleKey, evt.Volume))
            .ToList();

        return new BmsBackgroundAudioPlayer(events, BackgroundAudioPaused);
    }

    internal void BindPauseSource(IBindable<bool> paused)
    {
        ((IBindable<bool>)BackgroundAudioPaused).BindTo(paused);
    }

    internal void Update()
    {
        if (!BackgroundAudioPaused.Value && !stoppedPreviewForGameplay)
            StopPreviewForGameplay();
    }

    internal void SubmitLivePlayBatch() => SamplePlayback.SubmitLivePlayBatch();

    internal void StopPreviewForGameplay()
    {
        if (stoppedPreviewForGameplay)
            return;

        previewTrackBeforePlay = BmsWorkingBeatmap.ActivePreviewTrack;

        if (previewTrackBeforePlay == null)
            return;

        BmsWorkingBeatmap.SwitchActivePreviewToGameplayClockOnly();
        stoppedPreviewForGameplay = true;
    }

    internal void Dispose(double? previewRestoreTime)
    {
        BackgroundAudioPaused.UnbindAll();

        if (previewTrackBeforePlay == null || !stoppedPreviewForGameplay)
            return;

        // A newly selected beatmap owns its own preview and must not be replaced by the old session.
        if (BmsWorkingBeatmap.ActivePreviewTrack == previewTrackBeforePlay)
            BmsWorkingBeatmap.RestoreActivePreview(previewRestoreTime);

        previewTrackBeforePlay = null;
    }

    internal static string ResolveBeatmapSource(BmsBeatmap beatmap) =>
        beatmap.BeatmapInfo.BeatmapSet?.Beatmaps.FirstOrDefault(candidate => candidate.ID == beatmap.BeatmapInfo.ID)
            ?.Metadata.Source ?? beatmap.BeatmapInfo.Metadata.Source;

    internal static double GetPlaybackRate(IReadOnlyList<Mod>? mods)
    {
        var rateMod = mods?.OfType<ModRateAdjust>().FirstOrDefault();
        return rateMod?.SpeedChange.Value ?? 1;
    }

    private static IEnumerable<BmsSampleUsage> getSampleUsages(BmsBeatmap beatmap)
    {
        foreach (var evt in beatmap.BackgroundSampleEvents)
            yield return new BmsSampleUsage(evt.SampleKey, evt.Time, ResumeAfterSeek: true);

        foreach (var column in beatmap.HitObjects
                     .Where(hitObject => hitObject is not BmsLandmine)
                     .GroupBy(hitObject => hitObject.Column))
        {
            BmsHitObject? previousHitObject = null;

            foreach (var hitObject in column.OrderBy(hitObject => hitObject.StartTime))
            {
                if (hitObject.SampleKey is { } sampleKey)
                {
                    // A column exposes its next note's sample as soon as the preceding note can be judged.
                    var earliestTriggerTime = previousHitObject == null
                        ? 0
                        : Math.Max(0, previousHitObject.StartTime - getFastJudgementWindow(previousHitObject));
                    var latestTriggerTime = hitObject.StartTime + getSlowJudgementWindow(hitObject);

                    yield return new BmsSampleUsage(sampleKey, hitObject.StartTime, earliestTriggerTime, latestTriggerTime);
                }

                if (hitObject is BmsLongNote { TailSampleKey: { } tailSampleKey } longNote)
                    yield return new BmsSampleUsage(tailSampleKey, longNote.EndTime);

                previousHitObject = hitObject;
            }
        }
    }

    private static double getFastJudgementWindow(BmsHitObject hitObject) =>
        BmsJudgementProfileProvider.GetTable(
            hitObject.Beatmap.LayoutVariant,
            hitObject.Column,
            hitObject.EffectiveJudgementRate,
            tail: false).FastWindowFor(HitResult.Ok);

    private static double getSlowJudgementWindow(BmsHitObject hitObject) =>
        BmsJudgementProfileProvider.GetTable(
            hitObject.Beatmap.LayoutVariant,
            hitObject.Column,
            hitObject.EffectiveJudgementRate,
            tail: false).SlowWindowFor(HitResult.Ok);
}
