using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModInvertTest
{
    [Test]
    public void TestConvertsAdjacentObjectsInEachColumn()
    {
        var beatmap = createBeatmap(
            new BmsNote { StartTime = 1000, Column = 1 },
            new BmsNote { StartTime = 1500, Column = 2 },
            new BmsNote { StartTime = 1700, Column = 2 },
            new BmsNote { StartTime = 2000, Column = 1 },
            new BmsNote { StartTime = 3000, Column = 1 });

        new BmsModInvert().ApplyToBeatmap(beatmap);

        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(5));
        Assert.Multiple(() =>
        {
            assertLongNote(beatmap.HitObjects[0], 1, 1000, 875);
            assertLongNote(beatmap.HitObjects[1], 2, 1500, 100);
            assertNote(beatmap.HitObjects[2], 2, 1700);
            assertLongNote(beatmap.HitObjects[3], 1, 2000, 875);
            assertNote(beatmap.HitObjects[4], 1, 3000);
        });
    }

    [Test]
    public void TestUsesLongNoteHeadAndMovesTailSampleToBackground()
    {
        var beatmap = createBeatmap(
            new BmsLongNote
            {
                StartTime = 1000,
                Duration = 500,
                Column = 1,
                SourceChannel = 0x11,
                SampleKey = 12,
                SampleVolume = 75,
                TailSampleKey = 34,
                TailSampleVolume = 60,
            },
            new BmsNote { StartTime = 2000, Column = 1 });

        new BmsModInvert().ApplyToBeatmap(beatmap);

        var inverted = beatmap.HitObjects[0] as BmsLongNote;
        Assert.That(inverted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(inverted!.StartTime, Is.EqualTo(1000));
            Assert.That(inverted.Duration, Is.EqualTo(875));
            Assert.That(inverted.SourceChannel, Is.EqualTo(0x11));
            Assert.That(inverted.SampleKey, Is.EqualTo(12));
            Assert.That(inverted.SampleVolume, Is.EqualTo(75));
            Assert.That(inverted.TailSampleKey, Is.Null);
            Assert.That(beatmap.BackgroundSampleEvents, Has.Count.EqualTo(1));
            Assert.That(beatmap.BackgroundSampleEvents[0].Time, Is.EqualTo(1500));
            Assert.That(beatmap.BackgroundSampleEvents[0].SampleKey, Is.EqualTo(34));
            Assert.That(beatmap.BackgroundSampleEvents[0].Volume, Is.EqualTo(60));
        });
    }

    [Test]
    public void TestKeepsLastObjectAsNoteAndMovesItsTailSampleToBackground()
    {
        var beatmap = createBeatmap(
            new BmsNote { StartTime = 1000, Column = 1 },
            new BmsLongNote
            {
                StartTime = 2000,
                Duration = 500,
                Column = 1,
                SourceChannel = 0x51,
                SampleKey = 12,
                SampleVolume = 75,
                TailSampleKey = 34,
                TailSampleVolume = 60,
            });

        new BmsModInvert().ApplyToBeatmap(beatmap);

        var last = beatmap.HitObjects[1] as BmsNote;
        Assert.That(last, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(last!.StartTime, Is.EqualTo(2000));
            Assert.That(last.SourceChannel, Is.EqualTo(0x51));
            Assert.That(last.SampleKey, Is.EqualTo(12));
            Assert.That(last.SampleVolume, Is.EqualTo(75));
            Assert.That(beatmap.BackgroundSampleEvents, Has.Count.EqualTo(1));
            Assert.That(beatmap.BackgroundSampleEvents[0].Time, Is.EqualTo(2500));
            Assert.That(beatmap.BackgroundSampleEvents[0].SampleKey, Is.EqualTo(34));
            Assert.That(beatmap.BackgroundSampleEvents[0].Volume, Is.EqualTo(60));
        });
    }

    [Test]
    public void TestPreservesLandminesAndClearsBreaks()
    {
        var mine = new BmsLandmine { StartTime = 1250, Column = 1, LandmineDamagePercent = 20 };
        var beatmap = createBeatmap(
            new BmsNote { StartTime = 1000, Column = 1 },
            mine,
            new BmsNote { StartTime = 2000, Column = 1 });
        beatmap.Breaks.Add(new BreakPeriod(1100, 1900));

        new BmsModInvert().ApplyToBeatmap(beatmap);

        Assert.Multiple(() =>
        {
            Assert.That(beatmap.HitObjects, Has.Count.EqualTo(3));
            Assert.That(beatmap.HitObjects[1], Is.SameAs(mine));
            Assert.That(beatmap.Breaks, Is.Empty);
        });
    }

    [Test]
    public void TestRecomputesTailScrollPosition()
    {
        var beatmap = createBeatmap(
            new BmsNote { StartTime = 1000, Column = 1 },
            new BmsNote { StartTime = 2000, Column = 1 });

        new BmsModInvert().ApplyToBeatmap(beatmap);

        var inverted = (BmsLongNote)beatmap.HitObjects[0];
        Assert.That(inverted.ScrollPositionAtEndTime,
            Is.EqualTo(beatmap.TimingMap!.GetScrollPositionAtTime(inverted.EndTime)));
    }

    [Test]
    public void TestRandomLengthIsDeterministicAndPlayable()
    {
        var first = createEvenlySpacedBeatmap();
        var second = createEvenlySpacedBeatmap();
        var differentSeed = createEvenlySpacedBeatmap();

        applyRandomLength(first, 12345);
        applyRandomLength(second, 12345);
        applyRandomLength(differentSeed, 54321);

        var firstDurations = first.HitObjects.OfType<BmsLongNote>().Select(longNote => longNote.Duration).ToArray();
        var secondDurations = second.HitObjects.OfType<BmsLongNote>().Select(longNote => longNote.Duration).ToArray();
        var differentDurations = differentSeed.HitObjects.OfType<BmsLongNote>().Select(longNote => longNote.Duration).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstDurations, Is.EqualTo(secondDurations));
            Assert.That(firstDurations, Is.Not.EqualTo(differentDurations));
            Assert.That(firstDurations, Has.All.InRange(125, 875));
        });

        var notesByColumn = first.HitObjects.GroupBy(hitObject => hitObject.Column);

        foreach (var column in notesByColumn)
        {
            var ordered = column.OrderBy(hitObject => hitObject.StartTime).ToArray();

            for (var i = 0; i < ordered.Length - 1; i++)
                Assert.That(((BmsLongNote)ordered[i]).EndTime, Is.LessThan(ordered[i + 1].StartTime));
        }
    }

    [Test]
    public void TestRandomLengthSplitsIntervalsShorterThanHalfBeat()
    {
        var beatmap = createBeatmap(
            new BmsNote { StartTime = 1000, Column = 1 },
            new BmsNote { StartTime = 1200, Column = 1 });
        var mod = new BmsModInvert { RandomiseLength = { Value = true }, Seed = { Value = 12345 } };

        mod.ApplyToBeatmap(beatmap);

        assertLongNote(beatmap.HitObjects[0], 1, 1000, 100);
    }

    [Test]
    public void TestRandomSeedOnlyGeneratedWhenEnabled()
    {
        var disabled = new BmsModInvert();
        disabled.ApplyToBeatmap(createEvenlySpacedBeatmap());

        var enabled = new BmsModInvert { RandomiseLength = { Value = true } };
        enabled.ApplyToBeatmap(createEvenlySpacedBeatmap());

        Assert.Multiple(() =>
        {
            Assert.That(disabled.Seed.Value, Is.Null);
            Assert.That(enabled.Seed.Value, Is.Not.Null);
        });
    }

    [Test]
    public void TestRegisteredAsConversionMod()
    {
        var mod = new BmsRuleset().GetModsFor(ModType.Conversion).OfType<BmsModInvert>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(mod.Acronym, Is.EqualTo("IN"));
            Assert.That(mod.Type, Is.EqualTo(ModType.Conversion));
            Assert.That(mod.RandomiseLength.Value, Is.False);
        });
    }

    private static BmsBeatmap createEvenlySpacedBeatmap() => createBeatmap(
        new BmsNote { StartTime = 1000, Column = 1 },
        new BmsNote { StartTime = 2000, Column = 1 },
        new BmsNote { StartTime = 3000, Column = 1 },
        new BmsNote { StartTime = 1000, Column = 2 },
        new BmsNote { StartTime = 2000, Column = 2 },
        new BmsNote { StartTime = 3000, Column = 2 });

    private static void applyRandomLength(BmsBeatmap beatmap, int seed)
    {
        var mod = new BmsModInvert { RandomiseLength = { Value = true }, Seed = { Value = seed } };
        mod.ApplyToBeatmap(beatmap);
    }

    private static BmsBeatmap createBeatmap(params BmsHitObject[] hitObjects)
    {
        var beatmap = new BmsBeatmap
        {
            TotalColumns = 8,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = new BmsTimingMap(
                192,
                [new BmsMeasureInfo(0, 0, 192, 1)],
                [new BmsBpmEvent(0, 120, 0)],
                []),
        };
        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

        foreach (var hitObject in hitObjects)
        {
            hitObject.Beatmap = beatmap;
            hitObject.ScrollPositionAtStartTime = beatmap.TimingMap.GetScrollPositionAtTime(hitObject.StartTime);
            beatmap.HitObjects.Add(hitObject);
        }

        return beatmap;
    }

    private static void assertLongNote(BmsHitObject hitObject, int column, double startTime, double duration)
    {
        Assert.That(hitObject, Is.TypeOf<BmsLongNote>());
        var longNote = (BmsLongNote)hitObject;
        Assert.Multiple(() =>
        {
            Assert.That(longNote.Column, Is.EqualTo(column));
            Assert.That(longNote.StartTime, Is.EqualTo(startTime));
            Assert.That(longNote.Duration, Is.EqualTo(duration));
        });
    }

    private static void assertNote(BmsHitObject hitObject, int column, double startTime)
    {
        Assert.That(hitObject, Is.TypeOf<BmsNote>());
        Assert.Multiple(() =>
        {
            Assert.That(hitObject.Column, Is.EqualTo(column));
            Assert.That(hitObject.StartTime, Is.EqualTo(startTime));
        });
    }
}
