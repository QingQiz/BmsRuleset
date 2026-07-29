using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModInvert : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "Invert";

    public override string Acronym => "IN";

    public override LocalisableString Description => BmsStrings.ModInvert;

    public override IconUsage? Icon => OsuIcon.ModInvert;

    public override ModType Type => ModType.Conversion;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap bmsBeatmap)
            return;

        var newObjects = new List<BmsHitObject>();
        var tailSampleEvents = bmsBeatmap.HitObjects
            .OfType<BmsLongNote>()
            .Where(longNote => longNote.TailSampleKey.HasValue)
            .Select(longNote => new BmsSampleEvent(longNote.EndTime, 0, longNote.TailSampleKey!.Value, longNote.TailSampleVolume));

        foreach (var column in bmsBeatmap.HitObjects.Where(hitObject => hitObject is not BmsLandmine).GroupBy(hitObject => hitObject.Column))
        {
            var locations = column.OrderBy(hitObject => hitObject.StartTime).ToList();

            for (var i = 0; i < locations.Count - 1; i++)
            {
                var source = locations[i];
                var nextStartTime = locations[i + 1].StartTime;
                var duration = nextStartTime - source.StartTime;
                var beatLength = beatmap.ControlPointInfo.TimingPointAt(nextStartTime).BeatLength;

                duration = Math.Max(duration / 2, duration - beatLength / 4);

                newObjects.Add(new BmsLongNote
                {
                    Beatmap = bmsBeatmap,
                    StartTime = source.StartTime,
                    Duration = duration,
                    Column = source.Column,
                    SourceChannel = source.SourceChannel,
                    SampleKey = source.SampleKey,
                    SampleVolume = source.SampleVolume,
                    Samples = source.Samples.ToList(),
                    JudgementRate = source.JudgementRate,
                    ScrollPositionAtStartTime = source.ScrollPositionAtStartTime,
                    ScrollPositionAtEndTime = bmsBeatmap.TimingMap?.GetScrollPositionAtTime(source.StartTime + duration) ?? 0,
                });
            }

            var last = locations[^1];
            newObjects.Add(new BmsNote
            {
                Beatmap = bmsBeatmap,
                StartTime = last.StartTime,
                Column = last.Column,
                SourceChannel = last.SourceChannel,
                SampleKey = last.SampleKey,
                SampleVolume = last.SampleVolume,
                Samples = last.Samples.ToList(),
                JudgementRate = last.JudgementRate,
                ScrollPositionAtStartTime = last.ScrollPositionAtStartTime,
            });
        }

        newObjects.AddRange(bmsBeatmap.HitObjects.OfType<BmsLandmine>());
        bmsBeatmap.BackgroundSampleEvents = bmsBeatmap.BackgroundSampleEvents
            .Concat(tailSampleEvents)
            .OrderBy(sampleEvent => sampleEvent.Time)
            .ToArray();
        bmsBeatmap.HitObjects = newObjects.OrderBy(hitObject => hitObject.StartTime).ToList();
        bmsBeatmap.Breaks.Clear();
    }
}
