using System;
using System.Linq;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

public static class BmsPracticeBeatmap
{
    public static (double StartTime, double EndTime) GetTimeBounds(BmsBeatmap beatmap)
    {
        var start = Math.Min(0, beatmap.HitObjects.Select(note => note.StartTime).DefaultIfEmpty(0).Min());
        // EndTime is exclusive, including when the final object is a tap.
        var end = beatmap.HitObjects.Select(note => note.GetEndTime()).DefaultIfEmpty(start).Max() + 1;
        if (beatmap.TimingMap is { TickResolution: > 0, BpmEvents.Count: > 0 } timingMap)
        {
            // A grid-aligned exclusive endpoint must leave the final tap or hold inside its closing measure.
            foreach (var measure in timingMap.Measures)
            {
                var closingTime = timingMap.ProjectTickToTime(measure.StartTick + measure.LengthTicks);
                if (measure.LengthTicks > 0 && closingTime >= end)
                {
                    end = closingTime;
                    break;
                }
            }
        }

        return (start, Math.Max(start + 1, end));
    }

    public static int SelectedCount(BmsBeatmap beatmap, BmsPracticeSettings settings)
    {
        var millisecondPrecision = beatmap.BeatmapInfo.Ruleset.OnlineID == 3;
        return beatmap.HitObjects.Count(note => note is not BmsLandmine and not BmsInvisibleNote && settings.Contains(note.StartTime, millisecondPrecision));
    }

    public static BmsBeatmap Create(BmsBeatmap fullBeatmap, BmsPracticeSettings settings)
    {
        if (!settings.IsValidFor(fullBeatmap))
            throw new ArgumentOutOfRangeException(nameof(settings));

        var millisecondPrecision = fullBeatmap.BeatmapInfo.Ruleset.OnlineID == 3;
        var practice = (BmsBeatmap)fullBeatmap.Clone();
        practice.BeatmapInfo = fullBeatmap.BeatmapInfo.Clone();
        practice.Difficulty = fullBeatmap.Difficulty.Clone();
        practice.BeatmapInfo.Difficulty = fullBeatmap.BeatmapInfo.Difficulty.Clone();
        practice.PracticeFullGaugeOpportunities = fullBeatmap.HitObjects.Where(note => note is not BmsInvisibleNote)
            .Select((note, id) => BmsProfiles.CreateScoringOpportunities(id, BmsEngineChart.KindFor(note)).Length).Sum();
        practice.PracticeEndTime = settings.EndTime;
        practice.HitObjects = fullBeatmap.HitObjects.Where(note => settings.Contains(note.StartTime, millisecondPrecision))
            .Select(note => cloneObject(note, practice)).ToList();

        var background = fullBeatmap.BackgroundSampleEvents.ToList();
        foreach (var note in fullBeatmap.HitObjects.Where(note => !settings.Contains(note.StartTime, millisecondPrecision) && note is not BmsLandmine and not BmsInvisibleNote))
        {
            background.AddRange(note.GetSamples());
            if (note is BmsLongNote longNote)
                background.AddRange(longNote.GetTailSamples());
        }

        practice.BackgroundSampleEvents = background.OrderBy(sample => sample.Time).ToArray();
        practice.InvisibleNotes = fullBeatmap.InvisibleNotes.Where(note => settings.Contains(note.StartTime, millisecondPrecision))
            .Select(note => note.CreateKeySoundState(practice)).ToArray();

        // This audio-only state never enters HitObjects or the visible invisible-note layer.
        practice.PracticeInitialKeySounds = fullBeatmap.HitObjects.Where(note => note is not BmsLongNote and not BmsLandmine)
            .Concat(fullBeatmap.InvisibleNotes).Where(note => note.StartTime < settings.StartTime && !settings.Contains(note.StartTime, millisecondPrecision) && note.SampleKey.HasValue)
            .OrderBy(note => note.StartTime).ThenBy(note => note is BmsInvisibleNote ? 0 : 1)
            .GroupBy(note => note.Column).Select(column => column.Last().CreateKeySoundState(practice)).ToArray();

        return practice;
    }

    private static BmsHitObject cloneObject(BmsHitObject source, BmsBeatmap beatmap)
    {
        var clone = source.CloneForConversion();
        clone.Beatmap = beatmap;
        clone.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        return clone;
    }
}
