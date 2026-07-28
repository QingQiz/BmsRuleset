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

        Assert.That(beatmap.HitObjects, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            assertLongNote(beatmap.HitObjects[0], 1, 1000, 875);
            assertLongNote(beatmap.HitObjects[1], 2, 1500, 100);
            assertLongNote(beatmap.HitObjects[2], 1, 2000, 875);
        });
    }

    [Test]
    public void TestUsesLongNoteHeadAndPreservesHeadSamples()
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

        var inverted = beatmap.HitObjects.Single() as BmsLongNote;
        Assert.That(inverted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(inverted!.StartTime, Is.EqualTo(1000));
            Assert.That(inverted.Duration, Is.EqualTo(875));
            Assert.That(inverted.SourceChannel, Is.EqualTo(0x11));
            Assert.That(inverted.SampleKey, Is.EqualTo(12));
            Assert.That(inverted.SampleVolume, Is.EqualTo(75));
            Assert.That(inverted.TailSampleKey, Is.Null);
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
            Assert.That(beatmap.HitObjects, Has.Count.EqualTo(2));
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

        var inverted = (BmsLongNote)beatmap.HitObjects.Single();
        Assert.That(inverted.ScrollPositionAtEndTime,
            Is.EqualTo(beatmap.TimingMap!.GetScrollPositionAtTime(inverted.EndTime)));
    }

    [Test]
    public void TestRegisteredAsConversionMod()
    {
        var mod = new BmsRuleset().GetModsFor(ModType.Conversion).OfType<BmsModInvert>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(mod.Acronym, Is.EqualTo("IN"));
            Assert.That(mod.Type, Is.EqualTo(ModType.Conversion));
        });
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
}
