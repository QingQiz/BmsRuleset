using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Preview;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal sealed class BmsGameplayAudioController
{
    private BmsPreviewTrack? previewTrackBeforePlay;
    private bool stoppedPreviewForGameplay;

    internal BmsSamplePlayback SamplePlayback { get; }

    internal BindableBool BackgroundAudioPaused { get; } = new(true);

    internal BmsGameplayAudioController(BmsBeatmap beatmap, IReadOnlyList<Mod>? mods, BmsSamplePlayback? samplePlayback = null)
    {
        SamplePlayback = samplePlayback ?? new BmsSamplePlayback(
            beatmap.SampleDefinitions,
            ResolveBeatmapSource(beatmap),
            GetPlaybackRate(mods),
            GetSampleUsages(beatmap));
    }

    internal BmsBackgroundAudioPlayer CreateBackgroundAudioPlayer(BmsBeatmap beatmap)
    {
        var events = beatmap.BackgroundSampleEvents
            .OrderBy(evt => evt.Time)
            .Where(evt => beatmap.SampleDefinitions.ContainsKey(evt.SampleKey))
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

    internal void Dispose(double? previewRestoreTime, bool restorePreview = true)
    {
        BackgroundAudioPaused.UnbindAll();

        if (previewTrackBeforePlay == null || !stoppedPreviewForGameplay)
            return;

        // A newly selected beatmap owns its own preview and must not be replaced by the old session.
        if (restorePreview && BmsWorkingBeatmap.ActivePreviewTrack == previewTrackBeforePlay)
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

    internal static IEnumerable<BmsSampleUsage> GetSampleUsages(BmsBeatmap beatmap)
    {
        foreach (var mine in beatmap.HitObjects.OfType<BmsLandmine>())
        {
            foreach (var sample in mine.GetSamples())
                yield return new BmsSampleUsage(sample.SampleKey, sample.Time);
        }

        foreach (var evt in beatmap.BackgroundSampleEvents)
            yield return new BmsSampleUsage(evt.SampleKey, evt.Time, ResumeAfterSeek: true);

        foreach (var invisibleColumn in beatmap.InvisibleNotes.Concat(beatmap.PracticeInitialKeySounds).GroupBy(n => n.Column))
        {
            // Empty presses can still use these samples long after the notes leave the screen.
            // Judged LNs do not replace them, so retain them through intervening long notes.
            var changes = beatmap.HitObjects.Where(n => n.Column == invisibleColumn.Key && n is not (BmsLongNote or BmsLandmine))
                .Concat(invisibleColumn).OrderBy(n => n.StartTime).ThenBy(n => n is BmsInvisibleNote ? 0 : 1).ToArray();
            for (var i = 0; i < changes.Length; i++)
            {
                var note = changes[i];
                var latestTriggerTime = i + 1 < changes.Length ? changes[i + 1].StartTime : double.MaxValue;
                foreach (var sample in note.GetSamples())
                    yield return new BmsSampleUsage(sample.SampleKey, sample.Time, sample.Time, latestTriggerTime);
            }
        }

        foreach (var column in beatmap.HitObjects
                     .Where(hitObject => hitObject is not BmsLandmine)
                     .GroupBy(hitObject => hitObject.Column))
        {
            BmsHitObject? previousHitObject = null;

            foreach (var hitObject in column.OrderBy(hitObject => hitObject.StartTime))
            {
                if (hitObject.SampleKey.HasValue || hitObject.AdditionalSampleKeys.Count > 0)
                {
                    // A column exposes its next note's sample as soon as the preceding note can be judged.
                    var earliestTriggerTime = previousHitObject == null
                        ? 0
                        : Math.Max(0, previousHitObject.StartTime - getFastJudgementWindow(previousHitObject));
                    var latestTriggerTime = hitObject.StartTime + getSlowJudgementWindow(hitObject);

                    foreach (var sample in hitObject.GetSamples())
                        yield return new BmsSampleUsage(sample.SampleKey, sample.Time, earliestTriggerTime, latestTriggerTime);
                }

                if (hitObject is BmsLongNote longNote)
                    foreach (var tail in longNote.GetTailSamples())
                        yield return new BmsSampleUsage(tail.SampleKey, tail.Time);

                previousHitObject = hitObject;
            }
        }
    }

    private static double getFastJudgementWindow(BmsHitObject hitObject) =>
        BmsJudgementProfileProvider.GetTable(hitObject, tail: false).FastWindowFor(HitResult.Ok);

    private static double getSlowJudgementWindow(BmsHitObject hitObject) =>
        BmsJudgementProfileProvider.GetTable(hitObject, tail: false).SlowWindowFor(HitResult.Ok);
}
