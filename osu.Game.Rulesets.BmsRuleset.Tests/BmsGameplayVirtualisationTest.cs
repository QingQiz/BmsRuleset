using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
    public void TestDecoderCreatesTypedHitObjects()
    {
        using var stream = new MemoryStream("""
                                            #BPM 120
                                            #LNTYPE 1
                                            #WAV00 bomb.wav
                                            #00111:01
                                            #00151:0203
                                            #001D1:0A
                                            """u8.ToArray());
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);

        var converted = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        var hitObjects = converted.HitObjects.OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();

        Assert.That(hitObjects, Has.Length.EqualTo(3));
        Assert.That(hitObjects.Count(h => h.GetType() == typeof(BmsNote)), Is.EqualTo(1));
        Assert.That(hitObjects.Count(h => h.GetType() == typeof(BmsLongNote)), Is.EqualTo(1));
        Assert.That(hitObjects.Count(h => h.GetType() == typeof(BmsLandmine)), Is.EqualTo(1));
    }

    [Test]
    public void TestDrawableBmsHitObjectDoesNotBranchOnNoteKind()
    {
        var sourcePath = Path.Combine(findRepositoryRoot(), "osu.Game.Rulesets.BmsRuleset", "Objects", "Drawables", "DrawableBmsHitObject.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.That(source, Does.Not.Contain("IsLongNote"));
        Assert.That(source, Does.Not.Contain("IsMine"));
        Assert.That(source, Does.Not.Contain("HoldNote"));
        Assert.That(source, Does.Not.Contain("Mine"));
        Assert.That(source, Does.Not.Contain("tail"));
        Assert.That(source, Does.Not.Contain("Tail"));
    }

    [Test]
    public void TestPlayfieldDoesNotRewriteBeatmapHitObjects()
    {
        var hitObject = new BmsHitObject { StartTime = 1000, Column = 1, IsLongNote = true, Duration = 500 };
        var beatmap = new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { hitObject },
        };

        _ = new BmsPlayfield(beatmap);

        Assert.That(beatmap.HitObjects.Single(), Is.SameAs(hitObject));
        Assert.That(beatmap.HitObjects.Single(), Is.TypeOf<BmsHitObject>());

        var sourcePath = Path.Combine(findRepositoryRoot(), "osu.Game.Rulesets.BmsRuleset", "UI", "BmsPlayfield.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.That(source, Does.Not.Contain("normaliseHitObject"));
        Assert.That(source, Does.Not.Contain("ToTypedHitObject"));
        Assert.That(source, Does.Not.Contain("RegisterPool<BmsHitObject, DrawableBmsNote>"));
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
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "Aleph-0 (by LeaF)", "_14ANOTHER.bms"));
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
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "103_outlaw_ogg", "99_outlaw_caution.bms"));
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

    [Test]
    public void TestOutlawScrollplusNotesAroundCombo316AreAliveBeforeFirstVisibleWindow()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "103_outlaw_ogg", "99_outlaw_scrollplus.bms"));
        var playableObjects = beatmap.HitObjects.Where(h => !h.IsMine).OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        var playfield = new BmsPlayfield(beatmap);

        foreach (var hitObject in playableObjects.Skip(300).Take(40))
            playfield.Add(hitObject);

        var lateEntries = playfield.HitObjectContainer.Entries
            .Select(e => (Entry: e, HitObject: (BmsHitObject)e.HitObject))
            .Select(x => (x.Entry, x.HitObject, Combo: Array.IndexOf(playableObjects, x.HitObject) + 1, FirstVisibleTime: findEarliestVisibleTime(beatmap, x.HitObject)))
            .Where(x => x.Entry.LifetimeStart > x.FirstVisibleTime - 100)
            .Select(x => $"combo={x.Combo} tick={x.HitObject.TickInfo.Tick} col={x.HitObject.Column} start={x.HitObject.StartTime:F1} firstVisible={x.FirstVisibleTime:F1} lifetime={x.Entry.LifetimeStart:F1}")
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

    private static string findTestSongsRoot([CallerFilePath] string sourceFile = "")
    {
        var rootFromSourceFile = findTestSongsRootFrom(Path.GetDirectoryName(sourceFile) ?? string.Empty);

        if (!string.IsNullOrEmpty(rootFromSourceFile))
            return rootFromSourceFile;

        var rootFromCurrentDirectory = findTestSongsRootFrom(Environment.CurrentDirectory);

        if (!string.IsNullOrEmpty(rootFromCurrentDirectory))
            return rootFromCurrentDirectory;

        var rootFromTestDirectory = findTestSongsRootFrom(TestContext.CurrentContext.TestDirectory);

        if (!string.IsNullOrEmpty(rootFromTestDirectory))
            return rootFromTestDirectory;

        return BmsEmbeddedSongDecoderTest.TestSongsRoot;
    }

    private static string findRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty);

        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "osu.Game.Rulesets.BmsRuleset.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return TestContext.CurrentContext.WorkDirectory;
    }

    private static string findTestSongsRootFrom(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "osu.Game.Rulesets.BmsRuleset.Tests", "bms_test_songs");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return string.Empty;
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

    private static double findEarliestVisibleTime(BmsBeatmap beatmap, BmsHitObject hitObject)
    {
        var timingMap = beatmap.TimingMap!;
        var scrollRange = BmsDrawableRuleset.ComputeScrollTime(8);
        var firstVisible = hitObject.StartTime;

        for (var time = 0d; time <= hitObject.StartTime; time += 10)
        {
            var speed = timingMap.GetSpeedFactorAtTime(time);
            var multiplier = Math.Max(0.001, Math.Abs(speed));
            var progress = hitObject.ScrollPositionAtStartTime - timingMap.GetScrollPositionAtTime(time);

            if (progress <= scrollRange / multiplier)
            {
                firstVisible = time;
                break;
            }
        }

        return firstVisible;
    }
}
