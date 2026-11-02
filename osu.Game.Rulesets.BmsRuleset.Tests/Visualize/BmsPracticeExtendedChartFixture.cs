using System;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

internal static class BmsPracticeExtendedChartFixture
{
    internal static BmsBeatmap RepeatToMinutes(BmsBeatmap source, int minimumMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumMinutes);
        var timing = source.TimingMap ?? throw new ArgumentException("The fixture needs native timing.", nameof(source));
        var ticks = timing.Measures[^1].StartTick + timing.Measures[^1].LengthTicks;
        var duration = timing.ProjectTickToTime(ticks);
        if (ticks <= 0 || duration <= source.HitObjects.Max(note => note.GetEndTime()))
            throw new ArgumentException("The fixture needs a closing measure after its notes.", nameof(source));
        var repetitions = (int)Math.Ceiling(minimumMinutes * 60000d / duration);
        var cycles = Enumerable.Range(0, repetitions).ToArray();
        var extended = (BmsBeatmap)source.Clone();
        extended.BeatmapInfo = source.BeatmapInfo.Clone();
        extended.BeatmapInfo.DifficultyName = $"{minimumMinutes} min repeated chart";
        extended.TimingMap = new BmsTimingMap(timing.TickResolution,
            cycles.SelectMany(cycle => timing.Measures.Select(measure => measure with { Index = measure.Index + cycle * timing.Measures.Count, StartTick = measure.StartTick + cycle * ticks })),
            cycles.SelectMany(cycle => timing.BpmEvents.Select(bpm => bpm with { Tick = bpm.Tick + cycle * ticks, Time = bpm.Time + cycle * duration })),
            cycles.SelectMany(cycle => timing.StopEvents.Select(stop => stop with { Tick = stop.Tick + cycle * ticks })),
            cycles.SelectMany(cycle => timing.ScrollEvents.Select(scroll => scroll with { Tick = scroll.Tick + cycle * ticks })),
            cycles.SelectMany(cycle => timing.SpeedEvents.Select(speed => speed with { Tick = speed.Tick + cycle * ticks })), timing.ScrollReferenceBpm);
        extended.HitObjects = cycles.SelectMany(cycle => source.HitObjects.Select(note =>
        {
            var clone = note.CloneForConversion();
            clone.StartTime += cycle * duration;
            clone.Beatmap = extended;
            return clone;
        })).ToList();
        extended.InvisibleNotes = cycles.SelectMany(cycle => source.InvisibleNotes.Select(note =>
        {
            var clone = note.CreateKeySoundState(extended);
            clone.StartTime += cycle * duration;
            return clone;
        })).ToArray();
        extended.BackgroundSampleEvents = cycles.SelectMany(cycle => source.BackgroundSampleEvents.Select(sample => sample with { Time = sample.Time + cycle * duration, Tick = sample.Tick + cycle * ticks })).ToArray();
        extended.LongNoteTailSampleEvents = cycles.SelectMany(cycle => source.LongNoteTailSampleEvents.Select(sample => sample with { Time = sample.Time + cycle * duration, Tick = sample.Tick + cycle * ticks })).ToArray();
        extended.BeatmapInfo.Length = (int)(duration * repetitions);
        return extended;
    }
}
