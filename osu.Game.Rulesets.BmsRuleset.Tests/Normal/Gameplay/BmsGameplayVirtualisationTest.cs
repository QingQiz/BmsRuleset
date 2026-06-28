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

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestPlayfieldRoutesHitObjectsToPerColumnContainers()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        }));

        // Top-level HitObjectContainer is the default (empty) one from Playfield base.
        Assert.That(playfield.HitObjectContainer.GetType().Name, Is.EqualTo("HitObjectContainer"));

        // Hit objects are routed to per-column BmsColumnHitObjectContainers.
        Assert.That(playfield.Stage.Columns[1].HitObjectContainer.GetType().Name, Is.EqualTo("BmsColumnHitObjectContainer"));
    }

    [Test]
    public void TestDrawableBmsHitObjectsDoNotReferenceBmsPlayfield()
    {
        var drawablesPath = Path.Combine(findSourceRoot(), "osu.Game.Rulesets.BmsRuleset", "Objects", "Drawables");
        var references = Directory.GetFiles(drawablesPath, "DrawableBms*.cs")
            .Where(file => File.ReadAllText(file).Contains("BmsPlayfield"))
            .Select(Path.GetFileName)
            .ToArray();

        Assert.That(references, Is.Empty);
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
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = timingMap,
            HitObjects = { hitObject },
        }));

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var col = hitObject.Column;
        var entry = playfield.Stage.Columns[col].HitObjectContainer.Entries.Single();

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(hitObject.StartTime - 3000));
    }

    [Test]
    public void TestAlephAnotherCombo841LifetimeStartsBeforeVisibleWindow()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "Aleph-0 (by LeaF)", "_14ANOTHER.bms"));
        var hitObject = beatmap.HitObjects.Where(h => h is not BmsLandmine).OrderBy(h => h.StartTime).ElementAt(840);
        var playfield = new BmsPlayfield(beatmap);

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var col = hitObject.Column;
        var entry = playfield.Stage.Columns[col].HitObjectContainer.Entries.Single();
        var visibleStart = findFirstVisibleTime(beatmap, hitObject);

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(visibleStart));
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

        var beatmap = attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = timingMap,
            HitObjects = { hitObject },
        });
        var playfield = new BmsPlayfield(beatmap);

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var col = hitObject.Column;
        var entry = playfield.Stage.Columns[col].HitObjectContainer.Entries.Single();
        var visibleStart = findFirstVisibleTime(beatmap, hitObject);

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(visibleStart));
    }

    [Test]
    public void TestOutlawCautionNonMineNotesAreAliveBeforeAutoplayPress()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "103_outlaw_ogg", "99_outlaw_caution.bms"));
        var playfield = new BmsPlayfield(beatmap);

        foreach (var hitObject in beatmap.HitObjects.Where(h => h is not BmsLandmine).Take(64))
            playfield.Add(hitObject);

        playfield.RefreshAllLifetimes();

        var lateEntries = playfield.Stage.Columns
            .SelectMany(c => c.HitObjectContainer.Entries)
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
        var playableObjects = beatmap.HitObjects.Where(h => h is not BmsLandmine).OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        var playfield = new BmsPlayfield(beatmap);

        foreach (var hitObject in playableObjects.Skip(300).Take(40))
            playfield.Add(hitObject);

        playfield.RefreshAllLifetimes();

        var lateEntries = playfield.Stage.Columns
            .SelectMany(c => c.HitObjectContainer.Entries)
            .Select(e => (Entry: e, HitObject: (BmsHitObject)e.HitObject))
            .Select(x => (x.Entry, x.HitObject, Combo: Array.IndexOf(playableObjects, x.HitObject) + 1, FirstVisibleTime: findEarliestVisibleTime(beatmap, x.HitObject)))
            .Where(x => x.Entry.LifetimeStart > x.FirstVisibleTime + 1)
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

    /// <summary>
    ///     Attaches the beatmap to each of its hit objects. The decoder/converter does this for real
    ///     charts, but synthetic beatmaps built inline in tests skip that step — and lifetime code
    ///     (e.g. BmsHitObjectLifetimeEntry.getEarlyBadWindow) reads Beatmap.Rank/LayoutVariant, so
    ///     leaving it null NREs. This mirrors BmsBeatmapConverter's attachment loop.
    /// </summary>
    private static BmsBeatmap attachBeatmap(BmsBeatmap beatmap)
    {
        foreach (BmsHitObject ho in beatmap.HitObjects)
            ho.Beatmap = beatmap;
        return beatmap;
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

    private static string findSourceRoot([CallerFilePath] string sourceFile = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile) ?? string.Empty);

        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "osu.Game.Rulesets.BmsRuleset")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return TestContext.CurrentContext.TestDirectory;
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
