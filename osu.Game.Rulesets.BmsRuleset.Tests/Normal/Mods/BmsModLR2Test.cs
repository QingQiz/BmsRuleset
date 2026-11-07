#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModLr2Test
{
    [Test]
    public void ModRegistrationAndCompatibility()
    {
        var mod = new BmsRuleset().CreateModFromAcronym("R2");
        Assert.That(mod, Is.TypeOf<BmsModLr2>());
        Assert.That(mod.Type, Is.EqualTo(ModType.Fun));
        Assert.That(mod.Icon?.FontName, Is.EqualTo("bmsIcons"));
        Assert.That(mod.IncompatibleMods, Does.Contain(typeof(BmsModChargeNote)).And.Contain(typeof(BmsModHellChargeNote)));
        Assert.That(new BmsModChargeNote().IncompatibleMods, Does.Contain(typeof(BmsModLr2)));
        Assert.That(new BmsModHellChargeNote().IncompatibleMods, Does.Contain(typeof(BmsModLr2)));
        Assert.That(new BmsModLongNote().IncompatibleMods, Does.Not.Contain(typeof(BmsModLr2)));
    }

    // Values are transcribed from LR2oraja's JudgeProperty.LR2_SCALING, independently of production interpolation.
    [Test]
    public void RankWindows([Values(0, 1, 2, 3, 4)] int rank,
                            [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Pms9K)]
                            BmsLayoutVariant layout,
                            [Values(0, 1)] int column)
    {
        var (perfect, great, good) = rank switch { 0 => (8, 24, 40), 1 => (15, 30, 60), 3 => (21, 60, 120), _ => (18, 40, 100) };
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var table = BmsJudgementProfileProvider.GetTable(layout, column, rank, false);
        Assert.That(table.FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(perfect));
        Assert.That(table.FrameworkWindowFor(HitResult.Great), Is.EqualTo(great));
        Assert.That(table.FrameworkWindowFor(HitResult.Good), Is.EqualTo(good));
        Assert.That(table.SlowWindowFor(HitResult.Ok), Is.EqualTo(200));
        Assert.That(table.FastWindowFor(HitResult.Ok), Is.EqualTo(200));
        Assert.That(table.FastWindowFor(HitResult.Miss), Is.EqualTo(1000));
        Assert.That(table.ResultForOffset(perfect), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.ResultForOffset(perfect + 0.001), Is.EqualTo(HitResult.Great));
    }

    [Test]
    public void PrecomputedRatesSurviveParsingAndCloning()
    {
        var parsed = BmsChartParser.Parse([
            "#PLAYER 1", "#RANK 4", "#WAV01 note.wav", "#EXRANK01 100", "#00011:01", "#001A0:01", "#00111:01",
        ], judgementRateCalculator: new BmsModLr2().JudgementRateCalculator);
        Assert.That(parsed.HitObjects.Select(note => note.JudgementRate), Is.EqualTo(new[] { .75, .75 }));
        var beatmap = createBeatmap(parsed);
        Assert.That(beatmap.HitObjects.Select(note => note.JudgementRate), Is.EqualTo(new[] { .75, .75 }));
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        foreach (var note in beatmap.HitObjects)
            Assert.That(BmsJudgementProfileProvider.GetTable(note.CloneForConversion(), false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(18));
    }

    [TestCase("rank.bms", 4, 167, 18, 26.25)]
    [TestCase("rank.pms", 0, 48, 8, 11.08)]
    public void NativeConversionPrecomputesDistinctLr2RatesDespiteDefaultRoundingCollisions(string path, int rank, int exRank,
                                                                                         double firstPerfect, double secondPerfect)
    {
        string[] lines = [
            $"#RANK {rank}", "#WAV01 note.wav", $"#EXRANK01 {exRank}", "#00011:01", "#001A0:01", "#00111:01",
        ];
        var source = createDecodedBeatmap(BmsChartParser.Parse(lines, path));
        source.RawLines = lines;
        source.BeatmapInfo.BeatmapSet = new BeatmapSetInfo
        {
            Files = { new RealmNamedFileUsage(new RealmFile { Hash = source.BeatmapInfo.Hash }, path) },
        };
        var sourceNotes = source.HitObjects.Cast<BmsHitObject>().ToArray();
        Assert.That(sourceNotes[0].JudgementRate, Is.EqualTo(sourceNotes[1].JudgementRate));
        // A stale materialised override must not replace the difficulty in the native chart text.
        sourceNotes[0].JudgementRate = 2;
        var sourceRates = sourceNotes.Select(note => note.JudgementRate).ToArray();

        var beatmap = convertBeatmap(source);
        var first = beatmap.HitObjects[0];
        var second = beatmap.HitObjects[1];
        Assert.That(first.JudgementRate, Is.EqualTo(rank == 4 ? .75 : .25));
        Assert.That(second.JudgementRate, Is.EqualTo(rank == 4 ? 1.25 : .36));
        Assert.That(source.HitObjects.Cast<BmsHitObject>().Select(note => note.JudgementRate), Is.EqualTo(sourceRates));
        for (var i = 0; i < sourceNotes.Length; i++)
        {
            Assert.That(beatmap.HitObjects[i], Is.Not.SameAs(sourceNotes[i]));
            Assert.That(beatmap.HitObjects[i].Beatmap, Is.SameAs(beatmap));
            Assert.That(sourceNotes[i].Beatmap, Is.SameAs(source));
        }

        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        Assert.That(BmsJudgementProfileProvider.GetTable(first, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(firstPerfect).Within(0.001));
        Assert.That(BmsJudgementProfileProvider.GetTable(second, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(secondPerfect).Within(0.001));
        Assert.That(BmsJudgementProfileProvider.GetTable(first, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(firstPerfect).Within(0.001));
        first.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        second.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        first.HitWindows.SetDifficulty(5);
        second.HitWindows.SetDifficulty(5);
        Assert.That(first.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(firstPerfect).Within(0.001));
        Assert.That(second.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(secondPerfect).Within(0.001));

        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine(beatmap.HitObjects.ToArray(), beatmap.LayoutVariant, first.Column, output);
        var offset = (firstPerfect + secondPerfect) / 2;
        engine.QueueInput(first.StartTime + offset, first.Column, true, false);
        engine.ProcessFrame(new BmsFrameContext(first.StartTime + offset, 1), 0);
        engine.QueueInput(second.StartTime + offset, second.Column, true, false);
        engine.ProcessFrame(new BmsFrameContext(second.StartTime + offset, 1), 0);
        Assert.That(output.GetMainResult(first).Type, Is.EqualTo(HitResult.Great));
        Assert.That(output.GetMainResult(second).Type, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void ForeignManiaConversionPrecomputesLr2Rates()
    {
        var source = new Beatmap
        {
            BeatmapInfo = new BeatmapInfo(new RulesetInfo { OnlineID = 3, ShortName = "mania" }, new BeatmapDifficulty { CircleSize = 7 }),
            HitObjects = [new TestManiaNote { Column = 0, StartTime = 1000 }],
        };
        var converted = convertBeatmap(source);
        var note = converted.HitObjects.Single();
        Assert.That(note.JudgementRate, Is.EqualTo(.75));
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        Assert.That(BmsJudgementProfileProvider.GetTable(note, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(source.HitObjects.Single(), Is.TypeOf<TestManiaNote>());
    }

    [Test]
    public void PrecomputedNoteWindowsDoNotReadChangedHeaderDifficulty()
    {
        var beatmap = createBeatmap(BmsChartParser.Parse([
            "#RANK 4", "#WAV01 note.wav", "#EXRANK01 167", "#00011:01", "#001A0:01", "#00111:01",
        ], judgementRateCalculator: new BmsModLr2().JudgementRateCalculator));
        var note = beatmap.HitObjects[1];
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        Assert.That(note.JudgementRate, Is.EqualTo(1.25));
        Assert.That(BmsJudgementProfileProvider.GetTable(note, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.25));
        beatmap.Rank = 0;
        beatmap.ExRank = 1;
        Assert.That(note.EffectiveJudgementRate, Is.EqualTo(1.25));
        Assert.That(BmsJudgementProfileProvider.GetTable(note, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.25));
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        note.HitWindows.SetDifficulty(5);
        Assert.That(note.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(26.25));
    }

    [TestCase(2500)]
    [TestCase(-500)]
    public void PrecomputedExRankRatesSurviveLeadInShifts(double offset)
    {
        var parsed = BmsChartParser.Parse([
            "#BPM 120", "#RANK 4", "#WAV01 note.wav", "#EXRANK01 167", "#00011:01", "#001A0:01", "#00111:01",
        ], judgementRateCalculator: new BmsModLr2().JudgementRateCalculator).ShiftedBy(offset);
        var beatmap = createBeatmap(parsed);
        Assert.That(beatmap.HitObjects.Select(note => note.StartTime), Is.EqualTo(new[] { offset, 2000 + offset }));
        Assert.That(beatmap.HitObjects.Select(note => note.JudgementRate), Is.EqualTo(new[] { .75, 1.25 }));
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        Assert.That(BmsJudgementProfileProvider.GetTable(beatmap.HitObjects[0], false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(BmsJudgementProfileProvider.GetTable(beatmap.HitObjects[1], false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.25));
    }

    [Test]
    public void CoincidentExRankEventsUseTheLastDefinitionAcrossStopsAndPractice()
    {
        var parsed = BmsChartParser.Parse([
            "#BPM 120", "#RANK 4", "#WAV01 note.wav", "#STOP01 192", "#EXRANK01 80", "#EXRANK02 167",
            "#00011:01", "#00109:01", "#001A0:01", "#001A0:02", "#00111:01", "#00211:01",
        ], judgementRateCalculator: new BmsModLr2().JudgementRateCalculator);
        var beatmap = createBeatmap(parsed);
        var second = beatmap.HitObjects[1];
        Assert.That(second.StartTime, Is.EqualTo(2000));
        Assert.That(beatmap.HitObjects[2].StartTime, Is.EqualTo(6000));
        Assert.That(beatmap.HitObjects.Select(note => note.JudgementRate), Is.EqualTo(new[] { .75, 1.25, 1.25 }));
        var practice = BmsPracticeBeatmap.Create(beatmap, new BmsPracticeSettings { StartTime = second.StartTime, EndTime = second.StartTime + 100 });
        Assert.That(practice.TimingMap, Is.SameAs(beatmap.TimingMap));
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        Assert.That(BmsJudgementProfileProvider.GetTable(practice.HitObjects.Single(), false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.25));
        practice.HitObjects.Single().HitWindows.SetDifficulty(5);
        Assert.That(practice.HitObjects.Single().HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(26.25));
    }

    [TestCase(0, 0)]
    [TestCase(.75, 18)]
    [TestCase(1, 21)]
    public void ExplicitRateOverrideStillTakesPrecedence(double rate, double perfectWindow)
    {
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, JudgementRate = rate, Column = 1 };
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        Assert.That(BmsJudgementProfileProvider.GetTable(note, false).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(perfectWindow));
    }

    [Test]
    public void FrameworkWindowsUsePrecomputedRatesAndFallbackToDefaultHeaderDifficulty()
    {
        var beatmap = createBeatmap();
        beatmap.Rank = 4;
        var note = new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1 };
        var windows = new BmsHitWindows(note);
        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(15));
        using (BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]))
        {
            windows.SetDifficulty(5);
            Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(18));
            note.JudgementRate = double.NaN;
            windows.SetDifficulty(5);
            Assert.That(note.EffectiveJudgementRate, Is.EqualTo(1.25));
            Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(26.25));
            note.JudgementRate = 1;
            windows.SetDifficulty(5);
            Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(21));
        }

        windows.SetDifficulty(5);
        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(20));
    }

    [Test]
    public void ScopeAndWindowConstraintsAreOrderIndependent()
    {
        var original = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false);
        BmsJudgementWindowTable first;
        using (BmsJudgementProfileProvider.UseWindowMods([new BmsModNoGood(), new BmsModLr2(), new BmsModNoBad()]))
        {
            first = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false);
            Assert.That(BmsJudgementProfileProvider.ResolveJudgementRate(BmsLayoutVariant.Pms9K, 4, null), Is.EqualTo(.75));
        }
        using (BmsJudgementProfileProvider.UseWindowMods([new BmsModNoBad(), new BmsModLr2(), new BmsModNoGood()]))
        {
            var second = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false);
            Assert.That(second.HitWindows, Is.EqualTo(first.HitWindows));
            Assert.That(second.ResultForOffset(70), Is.EqualTo(HitResult.None));
            Assert.That(second.ResultForOffset(20), Is.EqualTo(HitResult.Great));
            Assert.That(BmsJudgementProfileProvider.ResolveJudgementRate(BmsLayoutVariant.Pms9K, 4, null), Is.EqualTo(.75));
            var behaviour = BmsJudgementProfileProvider.GetBehaviour(BmsLayoutVariant.Pms9K);
            Assert.That(behaviour.SearchWindow, Is.EqualTo(1000));
            Assert.That(behaviour.NonConsumingBad, Is.False);
        }

        Assert.That(BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false), Is.SameAs(original));
    }

    [Test]
    public void EmptyPoorIsEarlyOnlyAndSearchExtendsPast500ms()
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        var column = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output).GetColumn(1);
        Assert.That(column.HandleNotePress(new BmsPressContext(100, new BmsFrameContext(100, 1))).Kind, Is.EqualTo(PressOutcomeKind.EmptyPoor));
        var judged = new BmsJudgementCandidate(1000, 1000, 1, 0.75, false, IsJudged: true);
        Assert.That(BmsJudgementSelector.SelectPress(beatmap.LayoutVariant, 1, [judged], 1050).Result, Is.EqualTo(HitResult.None));
    }

    [Test]
    public void ModOverridesUserNoteSelection([Values] BmsJudgementAlgorithm algorithm)
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        BmsJudgementCandidate[] notes = [new(1000, 1000, 1, 0.75, false), new(1160, 1160, 1, 0.75, false)];
        var selection = BmsJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, notes, 1150, algorithm);
        Assert.That(selection.Candidate?.StartTime, Is.EqualTo(1160));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Perfect));
    }

    [TestCase(1400, HitResult.Perfect, BmsLayoutVariant.Bme7K)]
    [TestCase(1399.99, HitResult.Ok, BmsLayoutVariant.Bme7K)]
    [TestCase(1500, HitResult.Perfect, BmsLayoutVariant.Bme7K)]
    [TestCase(1400, HitResult.Perfect, BmsLayoutVariant.Pms9K)]
    [TestCase(1399.99, HitResult.Ok, BmsLayoutVariant.Pms9K)]
    [TestCase(1500, HitResult.Perfect, BmsLayoutVariant.Pms9K)]
    public void LongNoteReleaseKeepsHeadOrBecomesBad(double release, HitResult expected,
                                                     BmsLayoutVariant layout)
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var context = createLongNoteContext(layout);
        context.Press(1000, HitResult.Perfect);
        context.Release(release);
        Assert.That(context.Results, Is.EqualTo([expected]));
    }

    [Test]
    public void LongNoteEarlyBadIsImmediateAndLateHeadExpiresAtGoodWindow()
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var context = createLongNoteContext();
        context.Press(850, HitResult.Ok);
        Assert.That(context.Results, Is.EqualTo([HitResult.Ok]));
        var late = createLongNoteContext();
        late.Passive(1100.001);
        Assert.That(late.Results, Is.EqualTo([HitResult.Meh]));
    }

    [Test]
    public void LongNoteDeadlineSurvivesWindowConstraints([Values] bool noGreat)
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([noGreat ? new BmsModNoGreat() : new BmsModNoGood(), new BmsModLr2()]);
        var beatmap = createBeatmap();
        var note = new BmsLongNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000, Duration = 500 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        engine.QueueInput(1010, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1010, 1), 1010);
        engine.QueueInput(1400, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(1400, 1), 390);
        Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void VeryShortMissedLongNoteSettlesWithoutRepeatedDeadlines()
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var beatmap = createBeatmap();
        var note = new BmsLongNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000, Duration = 10 };
        var output = new BmsRecordingJudgementOutput();
        var engine = new BmsJudgementEngine([note], beatmap.LayoutVariant, 1, output);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 2000);
        Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Meh));
        Assert.That(engine.NextEventTime, Is.EqualTo(double.PositiveInfinity));
    }

    [Test]
    public void EmptyPoorPreservesComboInAllLayouts([Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Pms9K)] BmsLayoutVariant layout)
    {
        using var score = new BmsScoreProcessor();
        score.ApplyBeatmap(new BmsBeatmap { LayoutVariant = layout });
        new BmsModLr2().ApplyToScoreProcessor(score);
        score.Combo.Value = 5;
        score.RegisterEmptyPoor();
        Assert.That(score.Combo.Value, Is.EqualTo(5));
    }

    [Test]
    public void ComboRulesBelongToTheScoreInstance([Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Pms9K)] BmsLayoutVariant layout)
    {
        using var score = new BmsScoreProcessor();
        score.ApplyBeatmap(new BmsBeatmap { LayoutVariant = layout });
        score.Combo.Value = 5;
        using (BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]))
            score.RegisterEmptyPoor();
        Assert.That(score.Combo.Value, Is.Zero);
        new BmsModLr2().ApplyToScoreProcessor(score);
        score.Combo.Value = 5;
        using (BmsJudgementProfileProvider.UseWindowMods([]))
            score.RegisterEmptyPoor();
        Assert.That(score.Combo.Value, Is.EqualTo(5));
    }

    [Test]
    public void EquivalentBaseDifficultiesShareWindowTables()
    {
        using var windows = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var normal = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false);
        Assert.That(BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, false), Is.SameAs(normal));
        Assert.That(BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 4, false), Is.SameAs(normal));
        Assert.That(BmsJudgementProfileProvider.GetTable(new BmsJudgementWindowContext(BmsLayoutVariant.Bme7K, .75, false), 1), Is.SameAs(normal));
    }

    [TestCase(1, 160.16)]
    [TestCase(400, 224)]
    [TestCase(500, 256)]
    [TestCase(600, 288)]
    [TestCase(1000, 352)]
    public void DefaultTotal(int notes, double expected)
    {
        var profile = new BmsModLr2().ApplyToGaugeProfile(BmsGaugeType.Normal, BmsGaugeProfileFactory.Create(BmsGaugeType.Normal));
        Assert.That(new BmsGaugeCalculator(profile, 0, notes).Total, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase(144, 1000, 2.5)]
    [TestCase(160, 1000, 2)]
    [TestCase(240, 20, 10)]
    [TestCase(240, 500, 2)]
    [TestCase(240, 1000, 1)]
    public void HardDamageCorrection(double total, int notes, double scale)
    {
        var profile = new BmsModLr2().ApplyToGaugeProfile(BmsGaugeType.Hard, BmsGaugeProfileFactory.Create(BmsGaugeType.Hard));
        var calculator = new BmsGaugeCalculator(profile, total, notes);
        Assert.That(calculator.GetDeltaFor(HitResult.Ok, 1), Is.EqualTo(-0.06 * scale).Within(1e-9));
        Assert.That(calculator.GetDeltaFor(HitResult.Perfect, 0.5), Is.EqualTo(0.001));
    }

    [Test]
    public void HardGutsAndDeathThreshold()
    {
        var profile = new BmsModLr2().ApplyToGaugeProfile(BmsGaugeType.Hard, BmsGaugeProfileFactory.Create(BmsGaugeType.Hard));
        var calculator = new BmsGaugeCalculator(profile, 300, 1000);
        Assert.That(calculator.GetDeltaFor(HitResult.Ok, 0.32), Is.EqualTo(-0.06).Within(1e-9));
        Assert.That(calculator.GetDeltaFor(HitResult.Ok, 0.3199), Is.EqualTo(-0.036).Within(1e-9));
        Assert.That(calculator.ApplyDelta(0.04, -0.02), Is.EqualTo(0.02).Within(1e-9));
        Assert.That(calculator.ApplyDelta(0.03, -0.01001), Is.Zero);
    }

    [Test]
    public void ProcessorFailsBelowTwoPercentAndResetRestoresSeed()
    {
        var beatmap = createBeatmap();
        for (var i = 0; i < 1000; i++) beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = i * 100 });
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(beatmap);
        health.Mods = [new BmsModLr2()];
        health.SetGaugeTypes([BmsGaugeType.Hard], replaceExisting: true, initialStates: [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.03, false)]);
        health.RegisterEmptyPoor();
        Assert.That(health.Health.Value, Is.Zero);
        Assert.That(health.HasEverFailed, Is.True);
        Assert.That(health.CurrentGaugeStates.Single().Failed, Is.True);
        health.ResetReplayState();
        Assert.That(health.Health.Value, Is.EqualTo(0.03));
        Assert.That(health.CurrentGaugeStates.Single().Failed, Is.False);
    }

    [Test]
    public void PractisedIntervalUsesTheFullChartDefaultTotal()
    {
        var beatmap = createBeatmap();
        beatmap.Total = 0;
        beatmap.PracticeFullNoteCount = 1000;
        for (var i = 0; i < 100; i++) beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = i * 100 });
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(beatmap);
        health.Mods = [new BmsModLr2()];
        health.ApplyHellChargeTick(true, 1);
        Assert.That(health.Health.Value, Is.EqualTo(0.20352).Within(1e-9));
    }

    [Test]
    public void GaugeApplicationOrderAndAutoGaugeAreStable([Values] bool lr2First,
                                                           [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)]
                                                           BmsLayoutVariant layout)
    {
        var mod = new BmsModLr2();
        var auto = new BmsModAutoGauge();
        using var health = new BmsHealthProcessor();
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = layout;
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000 });
        health.ApplyBeatmap(beatmap);
        if (lr2First) health.Mods = [mod, auto];
        auto.ApplyToHealthProcessor(health);
        if (!lr2First) health.Mods = [auto, mod];
        foreach (var state in health.CurrentGaugeStates.Where(state => state.GaugeType is BmsGaugeType.Normal or BmsGaugeType.Easy or BmsGaugeType.AssistEasy))
            Assert.That(state.Health, Is.EqualTo(0.2).Within(1e-9));
        health.RegisterEmptyPoor();
        Assert.That(health.CurrentGaugeStates.Single(state => state.GaugeType == BmsGaugeType.Hazard).Health, Is.EqualTo(1));
        Assert.That(health.CurrentGaugeStates.Single(state => state.GaugeType == BmsGaugeType.Normal).Health, Is.EqualTo(0.18).Within(1e-9));
        var expected = health.CurrentGaugeStates.ToArray();
        health.Mods = [.. health.Mods];
        Assert.That(health.CurrentGaugeStates, Is.EqualTo(expected));
    }

    [Test]
    public void ProfileMigrationPreservesExplicitSeedsAndStartedGameplay([Values] bool explicitSeed)
    {
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = BmsLayoutVariant.Pms9K;
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000 });
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(beatmap);
        if (explicitSeed)
            health.SetGaugeTypes([BmsGaugeType.Normal], replaceExisting: true, initialStates: [new BmsGaugeStateSnapshot(BmsGaugeType.Normal, .3, false)]);
        else
        {
            health.RegisterEmptyPoor();
            health.Health.Value = .3;
        }

        health.Mods = [new BmsModLr2()];
        Assert.That(health.GaugeProfile.InitialHealth, Is.EqualTo(.2));
        Assert.That(health.Health.Value, Is.EqualTo(.3).Within(1e-9));
        health.RegisterEmptyPoor();
        Assert.That(health.Health.Value, Is.EqualTo(.28).Within(1e-9));
        health.Mods = [];
        Assert.That(health.GaugeProfile.InitialHealth, Is.EqualTo(.3));
        Assert.That(health.Health.Value, Is.EqualTo(.28).Within(1e-9));
        health.RegisterNonConsumingJudgement(HitResult.Ok);
        Assert.That(health.Health.Value, Is.EqualTo(.26).Within(1e-9));
    }

    [Test]
    public void ScoresAreSeparatedBothWays()
    {
        var score = new ScoreInfo { Mods = [new BmsModLr2()] };
        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, []), Is.False);
        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(score, [new BmsModLr2()]), Is.True);
        Assert.That(BmsLampScoreSelector.MatchesSelectedMods(new ScoreInfo(), [new BmsModLr2()]), Is.False);
    }

    [Test]
    public void ConvertsChartLongNotesAndHonoursCourseConstraints()
    {
        var beatmap = createBeatmap();
        beatmap.LockedLongNoteMode = BmsLongNoteMode.HellChargeNote;
        new BmsModLr2().ApplyToBeatmap(beatmap);
        Assert.That(beatmap.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.LongNote));
        Assert.That(BmsCourseSession.ResolveForbiddenModTypes(["cn", "gauge_7k"]), Does.Contain(typeof(BmsModLr2)));
        Assert.That(BmsCourseSession.ResolveForbiddenModTypes(["ln", "gauge_lr2"]), Does.Not.Contain(typeof(BmsModLr2)));
    }

    [Test]
    public void DifficultyUsesOriginalRankRatherThanLayoutPercentage([Values] bool extendedRank)
    {
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = BmsLayoutVariant.Pms9K;
        beatmap.TotalColumns = 9;
        beatmap.Rank = 4;
        beatmap.ExRank = extendedRank ? 100 : null;
        beatmap.HitObjects.AddRange([
            new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 0 },
            new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 2, StartTime = 250 },
        ]);
        new BmsDifficultyInfo { Rank = 4, ExRank = beatmap.ExRank, KeyCount = 9 }.WriteToOsuDifficulty(beatmap);
        var calculator = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new TestWorkingBeatmap(beatmap));
        calculator.Calculate([new BmsModLr2()]);
        Assert.That(calculator.StarRatingProcessor.HitLeniencyX, Is.EqualTo(BmsHitLeniency.FromGreatWindow(40)).Within(1e-12));
    }

    [Test]
    public void DisplayMetricsUseModsAndRestoreTheirScope()
    {
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo);
        new BmsDifficultyInfo { Rank = 2, KeyCount = 8, Total = 300, LockedLongNoteMode = BmsLongNoteMode.ChargeNote }.WriteToOsuDifficulty(info);
        var defaultTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false);
        var attributes = new BmsRuleset().GetBeatmapAttributesForDisplay(info, [new BmsModLr2()]).ToArray();
        Assert.That(attributes, Is.Not.Empty);
        Assert.That(attributes.Single(attribute => attribute.Acronym == "RK").AdditionalMetrics
            .Single(metric => metric.Name.ToString() == "Normal note PGREAT").Value.ToString(), Is.EqualTo("-18 to +18 ms"));
        Assert.That(attributes.Single(attribute => attribute.Acronym == "LM").AdjustedValue, Is.EqualTo((float)BmsLongNoteMode.LongNote));
        Assert.That(BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 2, false), Is.SameAs(defaultTable));
    }

    [Test]
    public void ExtendedRankUsesRawPercentageInEveryLayout([Values(0, 80, 100)] int exRank,
                                                           [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)]
                                                           BmsLayoutVariant layout)
    {
        using var scope = BmsJudgementProfileProvider.UseWindowMods([new BmsModLr2()]);
        var beatmap = createBeatmap(BmsChartParser.Parse([
            "#RANK 3", $"#DEFEXRANK {exRank}", "#WAV01 note.wav", "#00011:01",
        ], layout == BmsLayoutVariant.Pms9K ? "rank.pms" : "rank.bms", judgementRateCalculator: new BmsModLr2().JudgementRateCalculator));
        var note = beatmap.HitObjects.Single();
        Assert.That(note.JudgementRate, Is.EqualTo(exRank == 80 ? .6 : .75));
        Assert.That(BmsJudgementProfileProvider.GetTable(note, false).FrameworkWindowFor(HitResult.Great), Is.EqualTo(exRank == 80 ? 34 : 40));
    }

    [Test]
    public void ReplayAppliesProfilesWithAutoGaugeAndCourseSeeds([Values] bool course)
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000 });
        var replay = new osu.Game.Replays.Replay
        {
            HasReceivedAllFrames = true,
            Frames = [new BmsReplayFrame(100, BmsAction.Key1), new BmsReplayFrame(110), new BmsReplayFrame(2000)],
        };
        var score = new ScoreInfo { Mods = [new BmsModAutoGauge(), new BmsModLr2()] };
        var result = BmsReplaySimulation.Run(beatmap, replay, score,
            courseGaugeStates: course ? [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.5, false)] : null,
            courseGaugeProfileFamily: course ? BmsGaugeProfileFamily.Lr2 : null);
        Assert.That(result.Judgements.First().Result, Is.EqualTo(HitResult.Miss));
        var initial = result.Gauges.First(entry => entry.Time == 100);
        Assert.That(initial.States.Single(state => state.GaugeType == (course ? BmsGaugeType.Class : BmsGaugeType.Normal)).Health,
            Is.EqualTo(course ? 0.48 : 0.18).Within(1e-9));
    }

    [Test]
    public void LiveInputsArchiveAndOfflineReplayProduceTheSameResults([Values(1, 1.5)] double rate, [Values] bool autoFirst,
                                                                       [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)]
                                                                       BmsLayoutVariant layout)
    {
        var mod = new BmsModLr2();
        Mod[] mods = autoFirst ? [new BmsModAutoGauge(), mod] : [mod, new BmsModAutoGauge()];
        if (rate != 1) mods = [new BmsModDoubleTime(), .. mods];
        using var scope = BmsJudgementProfileProvider.UseWindowMods([mod]);
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = layout;
        beatmap.HitObjects.AddRange([
            new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000 },
            new BmsLongNote { Beatmap = beatmap, JudgementRate = .75, Column = 2, StartTime = 1500, Duration = 500 },
            new BmsLandmine { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1800, LandmineDamagePercent = 10 },
            new BmsNote { Beatmap = beatmap, JudgementRate = .75, Column = 3, StartTime = 2200 },
        ]);
        foreach (var note in beatmap.HitObjects) note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        using var score = new BmsScoreProcessor();
        using var health = new BmsHealthProcessor { Mods = mods };
        score.Mods.Value = mods;
        score.ApplyBeatmap(beatmap);
        mod.ApplyToScoreProcessor(score);
        health.ApplyBeatmap(beatmap);
        foreach (var item in mods.OfType<IApplicableToHealthProcessor>()) item.ApplyToHealthProcessor(health);
        var gauges = new List<BmsGaugeStateSnapshot[]>();
        var output = new BmsReplaySimulation.SimulationOutput(score, health, _ => gauges.Add(health.CurrentGaugeStates.ToArray()));
        var engine = new BmsJudgementEngine(Enumerable.Range(1, 3).Select(column => new BmsJudgementColumnDefinition(
            beatmap.HitObjects.Where(note => note.Column == column).ToArray(), beatmap.LayoutVariant, column, output)).ToArray());
        (double Time, int Column, bool Pressed)[] inputs = [(100, 1, true), (110, 1, false), (1017, 1, true), (1025, 1, false), (1500, 2, true), (1900, 2, false)];
        var replay = new osu.Game.Replays.Replay { HasReceivedAllFrames = true };
        foreach (var input in inputs)
        {
            engine.QueueInput(input.Time, input.Column, input.Pressed, false);
            engine.ProcessFrame(new BmsFrameContext(input.Time, rate), 0);
            var action = BmsKeyBindingConfiguration.ActionForColumn(layout, input.Column)!.Value;
            replay.Frames.Add(new BmsReplayFrame(input.Time, input.Pressed ? [action] : []) { Algorithm = BmsJudgementAlgorithm.Lowest });
        }

        engine.ProcessFrame(new BmsFrameContext(3000, rate), 0);
        replay.Frames.Add(new BmsReplayFrame(3000));
        var original = new Score { ScoreInfo = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = mods }, Replay = replay };
        var restored = BmsTestReplayArchive.RoundTrip(original);
        Assert.That(restored.ScoreInfo.Mods.Any(item => item is BmsModLr2), Is.True);
        var result = BmsReplaySimulation.Run(beatmap, restored.Replay, restored.ScoreInfo);
        Assert.That(result.Judgements.Select(entry => (entry.Source, entry.Result)), Is.EqualTo(score.JudgementEvents.Select(entry => (entry.Source, entry.Result))));
        Assert.That(result.Judgements.SelectMany(entry => entry.TimingObservations), Is.EqualTo(score.JudgementEvents.SelectMany(entry => entry.TimingObservations)));
        Assert.That(result.Gauges.SelectMany(entry => entry.States), Is.EqualTo(gauges.SelectMany(entry => entry)));
    }

    private static BmsBeatmap createBeatmap() => new()
    {
        LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 300, LockedLongNoteMode = BmsLongNoteMode.LongNote,
    };

    private static BmsBeatmap createBeatmap(BmsParseResult parsed)
        => convertBeatmap(createDecodedBeatmap(parsed));

    private static BmsDecodedBeatmap createDecodedBeatmap(BmsParseResult parsed)
    {
        var decoded = new BmsDecodedBeatmap();
        decoded.CopyFrom(parsed);
        foreach (var note in parsed.HitObjects)
            decoded.HitObjects.Add(BmsBeatmapDecoder.CreateHitObject(note, decoded));
        return decoded;
    }

    private static BmsBeatmap convertBeatmap(IBeatmap source)
    {
        var converter = new BmsBeatmapConverter(source, new BmsRuleset());
        new BmsModLr2().ApplyToBeatmapConverter(converter);
        return (BmsBeatmap)converter.Convert();
    }

    private static BmsLongNoteTestContext createLongNoteContext(BmsLayoutVariant layout = BmsLayoutVariant.Bme7K)
    {
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = layout;
        beatmap.TotalColumns = layout == BmsLayoutVariant.Pms9K ? 9 : 8;
        return new BmsLongNoteTestContext(new BmsLongNote { Beatmap = beatmap, JudgementRate = .75, Column = 1, StartTime = 1000, Duration = 500 });
    }

    private sealed class TestManiaNote : HitObject, IHasColumn
    {
        public int Column { get; init; }
    }
}
