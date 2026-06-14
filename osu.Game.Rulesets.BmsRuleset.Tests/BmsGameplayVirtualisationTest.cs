using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

public class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestPlayfieldUsesBmsHitObjectContainer()
    {
        var playfield = new BmsPlayfield(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        });

        Assert.That(playfield.HitObjectContainer.GetType().Name, Is.EqualTo("BmsHitObjectContainer"));
    }

    [Test]
    public void TestStageUsesVirtualisedMeasureLineArea()
    {
        var playfield = new BmsPlayfield(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = new BmsTimingMap(
                192,
                [
                    new BmsMeasureInfo(0, 0, 192, 1),
                    new BmsMeasureInfo(1, 192, 192, 1),
                ],
                [new BmsBpmEvent(0, 130, 0)],
                []),
        });

        Assert.That(playfield.Stage.MeasureLineArea.GetType().Name, Is.EqualTo("BmsMeasureLineContainer"));
    }

    [Test]
    public void TestSlowBpmExtendsHitObjectLifetime()
    {
        var timingMap = new BmsTimingMap(
            192,
            [
                new BmsMeasureInfo(0, 0, 192, 1),
                new BmsMeasureInfo(1, 192, 192, 1),
            ],
            [new BmsBpmEvent(0, 65, 0)],
            [],
            [],
            [],
            130);
        var hitObject = new BmsHitObject
        {
            TickInfo = new BmsTickInfo { Tick = 192, EndTick = 192 },
            StartTime = timingMap.ProjectTickToTime(192),
            Column = 1,
        };
        var playfield = new BmsPlayfield(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = timingMap,
            HitObjects = { hitObject },
        });

        playfield.Add(hitObject);

        var entry = playfield.HitObjectContainer.Entries.Single();

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(hitObject.StartTime - 3000));
    }

    [Test]
    public void TestAlephAnotherCombo841LifetimeStartsBeforeVisibleWindow()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_14ANOTHER.bms"));
        var hitObject = beatmap.HitObjects.Where(h => !h.IsMine).OrderBy(h => h.StartTime).ElementAt(840);
        var playfield = new BmsPlayfield(beatmap);

        playfield.Add(hitObject);

        var entry = playfield.HitObjectContainer.Entries.Single();
        var visibleStart = findFirstVisibleTime(beatmap, hitObject);

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(visibleStart - 100));
    }

    [Test]
    public void TestTempoTransitionExtendsHitObjectLifetimeFromActualVisibleWindow()
    {
        var timingMap = new BmsTimingMap(
            192,
            Enumerable.Range(0, 8).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1)),
            [
                new BmsBpmEvent(0, 65, 0),
                new BmsBpmEvent(900, 520, 900 * (60000d / 65) / (192 / 4d), 1),
            ],
            [],
            [],
            [],
            130);
        var hitObject = new BmsHitObject
        {
            TickInfo = new BmsTickInfo { Tick = 960, EndTick = 960 },
            StartTime = timingMap.ProjectTickToTime(960),
            Column = 1,
        };
        hitObject.ScrollPositionAtStartTime = timingMap.GetScrollPositionAtTime(hitObject.StartTime);

        var beatmap = new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = timingMap,
            HitObjects = { hitObject },
        };
        var playfield = new BmsPlayfield(beatmap);

        playfield.Add(hitObject);

        var entry = playfield.HitObjectContainer.Entries.Single();
        var visibleStart = findFirstVisibleTime(beatmap, hitObject);

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(visibleStart - 100));
    }

    [Test]
    public void TestOutlawCautionNonMineNotesAreAliveBeforeAutoplayPress()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "103_outlaw_ogg", "99_outlaw_caution.bms"));
        var playfield = new BmsPlayfield(beatmap);

        foreach (var hitObject in beatmap.HitObjects.Where(h => !h.IsMine).Take(64))
            playfield.Add(hitObject);

        var lateEntries = playfield.HitObjectContainer.Entries
            .Where(e => e.HitObject is BmsHitObject)
            .Where(e => e.LifetimeStart > e.HitObject.StartTime)
            .Select(e => $"{((BmsHitObject)e.HitObject).TickInfo.Tick}@{e.HitObject.StartTime:F1} lifetime={e.LifetimeStart:F1}")
            .ToArray();

        Assert.That(lateEntries, Is.Empty);
    }

    private static BmsBeatmap decodeFilesystemBeatmap(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);
        return (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
    }

    private static double findFirstVisibleTime(BmsBeatmap beatmap, BmsHitObject hitObject)
    {
        var timingMap = beatmap.TimingMap!;
        var scrollRange = BmsDrawableRuleset.ComputeScrollTime(8);
        var visibleStart = hitObject.StartTime;

        for (var time = hitObject.StartTime; time >= 0; time -= 10)
        {
            var speed = timingMap.GetSpeedFactorAtTime(time);
            var multiplier = Math.Max(0.001, Math.Abs(speed));
            var progress = hitObject.ScrollPositionAtStartTime - timingMap.GetScrollPositionAtTime(time);

            if (progress <= scrollRange / multiplier)
                visibleStart = time;
            else
                break;
        }

        return visibleStart;
    }
}
