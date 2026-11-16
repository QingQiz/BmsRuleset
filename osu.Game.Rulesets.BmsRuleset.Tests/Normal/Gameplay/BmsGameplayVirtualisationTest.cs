using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using RhythmEngine.Core;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [TestCase(500, 1310, 3310)]
    public void SelectedProfileControlsLifetimeBeforePlayfieldLoad(double start, double tapEnd, double holdEnd)
    {
        var tap = new BmsNote { StartTime = 1000, Column = 1 };
        var hold = new BmsLongNote { StartTime = 2000, Duration = 1000, Column = 1, Kind = KeyNoteKind.ReleaseHold };
        var beatmap = attachBeatmap(new BmsBeatmap { Rank = 2, TotalColumns = BmsLayout.BME7_KEY_COLUMNS, LayoutVariant = BmsLayoutVariant.Bme7K, HitObjects = [tap, hold] });
        using var score = new BmsScoreProcessor();
        score.ApplyBeatmap(beatmap);
        var playfield = new BmsPlayfield(beatmap, () => score.EngineChart);
        playfield.ScrollController.SetConfiguredScrollSpeed(1000);
        playfield.ScrollController.ConstantScrollActive = true;
        playfield.Add(tap);
        playfield.Add(hold);
        playfield.RefreshAllLifetimes();
        var entries = playfield.Stage.Columns[1].HitObjectContainer.Entries.ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(playfield.IsLoaded, Is.False);
            Assert.That(entries.Single(entry => entry.HitObject == tap).LifetimeStart, Is.EqualTo(start));
            Assert.That(entries.Single(entry => entry.HitObject == tap).LifetimeEnd, Is.EqualTo(tapEnd));
            Assert.That(entries.Single(entry => entry.HitObject == hold).LifetimeEnd, Is.EqualTo(holdEnd));
            Assert.That(playfield.CreateNoteLifetimeEntry(tap).GetPracticeVisibilityStart(), Is.EqualTo(start));
        });
    }

    [TestCase(0)]
    [TestCase(34.4828448267217)]
    [TestCase(1000)]
    public void AutomaticHoldLifetimeCoversLateHeadAndTail(double duration)
    {
        var note = new BmsLongNote { StartTime = 1000, Duration = duration, Column = 1, Kind = KeyNoteKind.Hold };
        var beatmap = attachBeatmap(new BmsBeatmap
        {
            Rank = 2, TotalColumns = BmsLayout.BME7_KEY_COLUMNS, LayoutVariant = BmsLayoutVariant.Bme7K, HitObjects = [note],
        });
        var compiled = new BmsEngineChart(beatmap);
        var playfield = new BmsPlayfield(beatmap, () => compiled);
        var entry = playfield.CreateNoteLifetimeEntry(note);
        entry.RefreshLifetime();
        Assert.That(entry.LifetimeEnd, Is.EqualTo(Math.Max(1210, note.EndTime + .001) + 100).Within(1e-9));
        foreach (var press in new[] { false, true })
        {
            var session = new JudgementSession(compiled.Chart, compiled.Configuration);
            var batch = session.AdvanceTo(Time.FromMilliseconds(entry.LifetimeEnd), press ? [new InputEvent(1_200_000, 1, true)] : []);
            Assert.That(session.GetNote(0).Completed, Is.True);
            Assert.That(batch.Transitions.SelectMany(transition => transition.Judgements).Select(result => result.Event.Time / 1000.0),
                Is.All.LessThan(entry.LifetimeEnd));
        }
    }

    [Test]
    public void TestPlayfieldRoutesHitObjectsToPerColumnContainers()
    {
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
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

    [TestCase(HitResult.Meh, false)]
    [TestCase(HitResult.Ok, true)]
    [TestCase(HitResult.Good, true)]
    [TestCase(HitResult.Great, true)]
    [TestCase(HitResult.Perfect, true)]
    [TestCase(HitResult.Miss, false)]
    public void TestHitExplosionResultFilter(HitResult result, bool expected)
    {
        Assert.That(BmsGameplayProfile.INSTANCE.ShowHitEffect(result), Is.EqualTo(expected));
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
            StartTime = timingMap.ProjectTickToTime(192),
            Column = 1,
        };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
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
    public void TestFastHitObjectLifetimeStartsDuringLeadIn()
    {
        var timingMap = new BmsTimingMap(
            192,
            [
                new BmsMeasureInfo(0, 0, 192, 1),
                new BmsMeasureInfo(1, 192, 192, 1),
            ],
            [new BmsBpmEvent(0, 120, 0)],
            [],
            [],
            [],
            120);
        var hitObject = new BmsHitObject
        {
            StartTime = 500,
            Column = 1,
        };
        hitObject.ScrollPositionAtStartTime = timingMap.GetScrollPositionAtTime(hitObject.StartTime);

        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = timingMap,
            HitObjects = { hitObject },
        }));

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[hitObject.Column].HitObjectContainer.Entries.Single();
        var expectedVisibleStart = hitObject.StartTime - BmsDrawableRuleset.ComputeScrollTime(8);

        Assert.That(entry.LifetimeStart, Is.EqualTo(expectedVisibleStart).Within(1));
        Assert.That(entry.LifetimeStart, Is.LessThan(0));
    }

    [Test]
    public void TestLifetimeCoversFastEmptyPoorWindow()
    {
        var hitObject = new BmsHitObject
        {
            StartTime = 1000,
            Column = 1,
        };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { hitObject },
        }));
        playfield.ScrollController.SetConfiguredScrollSpeed(100);
        playfield.ScrollController.ConstantScrollActive = true;

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[hitObject.Column].HitObjectContainer.Entries.Single();
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, hitObject.Column, BmsEngineRules.RateFor(hitObject), tail: false);

        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(hitObject.StartTime - table.FastWindowFor(HitResult.Miss)));
    }

    [Test]
    public void TestTapLifetimeOutlivesPassivePoorWindow()
    {
        var hitObject = new BmsNote
        {
            StartTime = 1000,
            Column = 1,
        };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { hitObject },
        }));

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[hitObject.Column].HitObjectContainer.Entries.Single();
        var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, hitObject.Column, BmsEngineRules.RateFor(hitObject), tail: false);

        Assert.That(entry.LifetimeEnd, Is.GreaterThan(hitObject.StartTime + table.SlowWindowFor(HitResult.Ok)));
    }

    [Test]
    public void TestLandmineLifetimeOutlivesStartTime()
    {
        var hitObject = new BmsLandmine
        {
            StartTime = 1000,
            Column = 1,
        };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { hitObject },
        }));

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[hitObject.Column].HitObjectContainer.Entries.Single();

        Assert.That(entry.LifetimeEnd, Is.EqualTo(hitObject.StartTime + 100));
    }

    [Test]
    public void TestReverseScrollMineDoesNotStayAliveWhileFarBelowViewport()
    {
        var timingMap = new BmsTimingMap(192,
            Enumerable.Range(0, 8).Select(i => new BmsMeasureInfo(i, i * 192, 192, 1)),
            [new BmsBpmEvent(0, 120, 0)], [], [new BmsScrollEvent(0, -1, 0)], [], 120);
        var mine = new BmsLandmine { StartTime = 10000, Column = 1 };
        mine.ScrollPositionAtStartTime = timingMap.GetScrollPositionAtTime(mine.StartTime);
        var beatmap = attachBeatmap(new BmsBeatmap
        {
            TotalColumns = 8,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = timingMap,
            HitObjects = { mine },
        });
        var playfield = createPlayfield(beatmap);
        playfield.Add(mine);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[1].HitObjectContainer.Entries.Single();
        var container = (BmsColumnHitObjectContainer)playfield.Stage.Columns[1].HitObjectContainer;
        playfield.ScrollController.Update(0);
        container.UpdateScrollEntries();
        Assert.That(entry.LifetimeStart, Is.GreaterThan(mine.StartTime));
        playfield.ScrollController.Update(mine.StartTime);
        container.UpdateScrollEntries();
        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(mine.StartTime));
        Assert.That(entry.LifetimeEnd, Is.EqualTo(mine.StartTime + 100));
    }

    [Test]
    public void TestPositiveVisualOffsetStartsLifetimeEarlier()
    {
        var hitObject = new BmsHitObject
        {
            StartTime = 3000,
            Column = 1,
        };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { hitObject },
        }));

        playfield.VisualOffset.Value = 500;
        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[hitObject.Column].HitObjectContainer.Entries.Single();
        var expectedVisibleStart = hitObject.StartTime - BmsDrawableRuleset.ComputeScrollTime(8) - 500;

        Assert.That(entry.LifetimeStart, Is.EqualTo(expectedVisibleStart).Within(1));
    }

    [Test]
    public void TestVisualOffsetOnlyExpandsCachedLifetimeStart()
    {
        var hitObject = new BmsHitObject
        {
            StartTime = 3000,
            Column = 1,
        };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            HitObjects = { hitObject },
        }));

        playfield.Add(hitObject);
        playfield.RefreshAllLifetimes();

        var entry = playfield.Stage.Columns[hitObject.Column].HitObjectContainer.Entries.Single();
        var baseLifetimeStart = entry.LifetimeStart;

        playfield.VisualOffset.Value = 500;
        playfield.ApplyVisualOffsetToAllLifetimes();
        Assert.That(entry.LifetimeStart, Is.EqualTo(baseLifetimeStart - 500).Within(1));

        playfield.VisualOffset.Value = -500;
        playfield.ApplyVisualOffsetToAllLifetimes();
        Assert.That(entry.LifetimeStart, Is.EqualTo(baseLifetimeStart - 500).Within(1));

        playfield.VisualOffset.Value = 250;
        playfield.ApplyVisualOffsetToAllLifetimes();
        Assert.That(entry.LifetimeStart, Is.EqualTo(baseLifetimeStart - 500).Within(1));
    }

    [Test]
    public void TestAlephAnotherCombo841LifetimeStartsBeforeVisibleWindow()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "Aleph-0 (by LeaF)", "_14ANOTHER.bms"));
        var hitObject = beatmap.HitObjects.Where(h => h is not BmsLandmine).OrderBy(h => h.StartTime).ElementAt(840);
        var playfield = createPlayfield(beatmap);

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
        var playfield = createPlayfield(beatmap);

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
        var playfield = createPlayfield(beatmap);

        foreach (var hitObject in beatmap.HitObjects.Where(h => h is not BmsLandmine).Take(64))
            playfield.Add(hitObject);

        playfield.RefreshAllLifetimes();

        var lateEntries = playfield.Stage.Columns
            .SelectMany(c => c.HitObjectContainer.Entries)
            .Where(e => e.HitObject is BmsHitObject)
            .Where(e => e.LifetimeStart > e.HitObject.StartTime)
            .Select(e => $"{e.HitObject.StartTime:F1} lifetime={e.LifetimeStart:F1}")
            .ToArray();

        Assert.That(lateEntries, Is.Empty);
    }

    [Test]
    public void TestOutlawScrollplusNotesAroundCombo316AreAliveBeforeFirstVisibleWindow()
    {
        var beatmap = decodeFilesystemBeatmap(Path.Combine(findTestSongsRoot(), "103_outlaw_ogg", "99_outlaw_scrollplus.bms"));
        var playableObjects = beatmap.HitObjects.Where(h => h is not BmsLandmine).OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        var playfield = createPlayfield(beatmap);

        foreach (var hitObject in playableObjects.Skip(300).Take(40))
            playfield.Add(hitObject);

        playfield.RefreshAllLifetimes();

        var lateEntries = playfield.Stage.Columns
            .SelectMany(c => c.HitObjectContainer.Entries)
            .Select(e => (Entry: e, HitObject: (BmsHitObject)e.HitObject))
            .Select(x => (x.Entry, x.HitObject, Combo: Array.IndexOf(playableObjects, x.HitObject) + 1, FirstVisibleTime: findEarliestVisibleTime(beatmap, x.HitObject)))
            .Where(x => x.Entry.LifetimeStart > x.FirstVisibleTime + 1)
            .Select(x => $"combo={x.Combo} col={x.HitObject.Column} start={x.HitObject.StartTime:F1} firstVisible={x.FirstVisibleTime:F1} lifetime={x.Entry.LifetimeStart:F1}")
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

    private static BmsPlayfield createPlayfield(BmsBeatmap beatmap)
    {
        var compiled = new BmsEngineChart(beatmap);
        return new BmsPlayfield(beatmap, () => compiled);
    }

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
