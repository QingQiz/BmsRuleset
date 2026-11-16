#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
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
    private static readonly GameplayProfile lr2_profile = GameplayProfile.FromMods([new BmsModLr2()]);

    [Test]
    public void ModRegistrationAndCompatibility()
    {
        var mod = new BmsRuleset().CreateModFromAcronym("R2");
        Assert.That(mod, Is.TypeOf<BmsModLr2>());
        Assert.That(mod!.Type, Is.EqualTo(ModType.Fun));
        Assert.That(mod.Icon?.FontName, Is.EqualTo("bmsIcons"));
        Assert.That(mod.IncompatibleMods, Does.Contain(typeof(BmsModChargeNote)).And.Contain(typeof(BmsModHellChargeNote)));
        Assert.That(new BmsModChargeNote().IncompatibleMods, Does.Contain(typeof(BmsModLr2)));
        Assert.That(new BmsModHellChargeNote().IncompatibleMods, Does.Contain(typeof(BmsModLr2)));
        Assert.That(new BmsModLongNote().IncompatibleMods, Does.Not.Contain(typeof(BmsModLr2)));
    }

    // Values are transcribed from LR2oraja's JudgeProperty.LR2_SCALING, independently of production interpolation.
    [Test]
    public void RankWindows([Values(0, 1, 2, 3, 4)] int rank,
                            [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Popn9K)]
                            BmsLayoutVariant layout,
                            [Values(0, 1)] int column)
    {
        var (perfect, great, good) = rank switch { 0 => (8, 24, 40), 1 => (15, 30, 60), 3 => (21, 60, 120), _ => (18, 40, 100) };

        var table = BmsEngineRules.GetTiming(layout, column, rank, false, BmsJudgementModifiers.Lr2oraja);
        Assert.That(table.FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(perfect));
        Assert.That(table.FrameworkWindowFor(HitResult.Great), Is.EqualTo(great));
        Assert.That(table.FrameworkWindowFor(HitResult.Good), Is.EqualTo(good));
        Assert.That(table.SlowWindowFor(HitResult.Ok), Is.EqualTo(200));
        Assert.That(table.FastWindowFor(HitResult.Ok), Is.EqualTo(200));
        Assert.That(table.FastWindowFor(HitResult.Miss), Is.EqualTo(1000));
        Assert.That(table.HitAt(perfect), Is.EqualTo(HitResult.Perfect));
        Assert.That(table.HitAt(perfect + 0.001), Is.EqualTo(HitResult.Great));
    }

    [Test]
    public void SourceExRankSurvivesParsingAndCloning()
    {
        var parsed = BmsChartParser.Parse([
            "#PLAYER 1", "#RANK 4", "#WAV01 note.wav", "#EXRANK01 100", "#00011:01", "#001A0:01", "#00111:01",
        ]);
        Assert.That(parsed.HitObjects.Select(note => note.ExRank), Is.EqualTo(new double?[] { null, 100 }));
        var beatmap = createBeatmap(parsed);
        Assert.That(beatmap.HitObjects.Select(note => BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja)), Is.EqualTo(new[] { .75, .75 }));

        foreach (var note in beatmap.HitObjects)
            Assert.That(BmsEngineRules.GetTiming(note.CloneForConversion(), false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(18));
    }

    [TestCase("rank.bms", 4, 167, 18, 26.302)]
    [TestCase("rank.pms", 0, 48, 8, 11.08)]
    public void SelectedLr2ProfileUsesNativeChartRates(string path, int rank, int exRank,
                                                               double firstPerfect, double secondPerfect)
    {
        string[] lines =
        [
            $"#RANK {rank}", "#WAV01 note.wav", $"#EXRANK01 {exRank}", "#00011:01", "#001A0:01", "#00111:01",
        ];
        var source = createDecodedBeatmap(BmsChartParser.Parse(lines, path));
        source.RawLines = lines;
        source.BeatmapInfo.BeatmapSet = new BeatmapSetInfo
        {
            Files = { new RealmNamedFileUsage(new RealmFile { Hash = source.BeatmapInfo.Hash }, path) },
        };
        var sourceNotes = source.HitObjects.Cast<BmsHitObject>().ToArray();
        Assert.That(BmsEngineRules.RateFor(sourceNotes[0]), Is.EqualTo(rank == 4 ? 1.25 : .33));
        Assert.That(BmsEngineRules.RateFor(sourceNotes[1]), Is.EqualTo(rank == 4 ? 1.2525 : .336).Within(1e-12));
        // A stale materialised override must not replace the difficulty in the native chart text.
        sourceNotes[0].ExRank = 200;
        var sourceRates = sourceNotes.Select(note => BmsEngineRules.RateFor(note)).ToArray();

        var beatmap = convertBeatmap(source);
        var first = beatmap.HitObjects[0];
        var second = beatmap.HitObjects[1];
        Assert.That(BmsEngineRules.RateFor(first, BmsJudgementModifiers.Lr2oraja), Is.EqualTo(rank == 4 ? .75 : .25));
        Assert.That(BmsEngineRules.RateFor(second, BmsJudgementModifiers.Lr2oraja), Is.EqualTo(rank == 4 ? 1.2525 : .36).Within(1e-12));
        Assert.That(source.HitObjects.Cast<BmsHitObject>().Select(note => BmsEngineRules.RateFor(note)), Is.EqualTo(sourceRates));

        for (var i = 0; i < sourceNotes.Length; i++)
        {
            Assert.That(beatmap.HitObjects[i], Is.Not.SameAs(sourceNotes[i]));
            Assert.That(beatmap.HitObjects[i].Beatmap, Is.SameAs(beatmap));
            Assert.That(sourceNotes[i].Beatmap, Is.SameAs(source));
        }

        Assert.That(BmsEngineRules.GetTiming(first, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(firstPerfect).Within(0.001));
        Assert.That(BmsEngineRules.GetTiming(second, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(secondPerfect).Within(0.001));
        Assert.That(BmsEngineRules.GetTiming(first, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(firstPerfect).Within(0.001));
        first.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        second.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2()];
        score.ApplyBeatmap(beatmap);
        first.HitWindows.SetDifficulty(5);
        second.HitWindows.SetDifficulty(5);
        Assert.That(first.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(firstPerfect).Within(0.001));
        Assert.That(second.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(secondPerfect).Within(0.001));

        var output = new BmsRecordingJudgementOutput();
        var engine = BmsEngineTestAccess.CreateEngine(beatmap, first.Column, output, lr2_profile);
        var offset = (firstPerfect + secondPerfect) / 2;
        engine.QueueInput(first.StartTime + offset, first.Column, true, false);
        engine.ProcessFrame(new BmsFrameContext(first.StartTime + offset, 1), 0);
        engine.QueueInput(first.StartTime + offset + .001, first.Column, false, false);
        engine.QueueInput(second.StartTime + offset, second.Column, true, false);
        engine.ProcessFrame(new BmsFrameContext(second.StartTime + offset, 1), 0);
        Assert.That(output.GetMainResult(first).Type, Is.EqualTo(HitResult.Great));
        Assert.That(output.GetMainResult(second).Type, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void SwitchingProfileOnMaterialisedPmsNotesUsesSourceExRank()
    {
        var source = createBeatmap(BmsChartParser.Parse([
            "#RANK 0", "#WAV01 note.wav", "#EXRANK01 48", "#00011:01", "#001A0:01", "#00111:01",
        ], "rank.pms"));
        var originalRates = source.HitObjects.Select(note => BmsEngineRules.RateFor(note)).ToArray();
        var converted = convertBeatmap(source);
        Assert.That(originalRates, Is.EqualTo(new[] { .33, .336 }).Within(1e-12));
        Assert.That(converted.HitObjects.Select(note => BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja)), Is.EqualTo(new[] { .25, .36 }).Within(1e-12));
        Assert.That(source.HitObjects.Select(note => BmsEngineRules.RateFor(note)), Is.EqualTo(originalRates));
        Assert.That(converted.HitObjects.Select(note => note.ExRank), Is.EqualTo(source.HitObjects.Select(note => note.ExRank)));
    }

    [Test]
    public void SelectedLr2ProfileUsesConvertedManiaRates()
    {
        var source = new Beatmap
        {
            BeatmapInfo = new BeatmapInfo(new RulesetInfo { OnlineID = 3, ShortName = "mania" }, new BeatmapDifficulty { CircleSize = 7 }),
            HitObjects = [new TestManiaNote { Column = 0, StartTime = 1000 }],
        };
        var converted = convertBeatmap(source);
        var note = converted.HitObjects.Single();
        Assert.That(BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja), Is.EqualTo(.75));

        Assert.That(BmsEngineRules.GetTiming(note, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(source.HitObjects.Single(), Is.TypeOf<TestManiaNote>());
    }

    [Test]
    public void PerNoteExRankTakesPrecedenceOverHeaderDifficulty()
    {
        var beatmap = createBeatmap(BmsChartParser.Parse([
            "#RANK 4", "#WAV01 note.wav", "#EXRANK01 167", "#00011:01", "#001A0:01", "#00111:01",
        ]));
        var note = beatmap.HitObjects[1];

        Assert.That(BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja), Is.EqualTo(1.2525).Within(1e-12));
        Assert.That(BmsEngineRules.GetTiming(note, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.302));
        beatmap.Rank = 0;
        beatmap.ExRank = 1;
        Assert.That(BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja), Is.EqualTo(1.2525).Within(1e-12));
        Assert.That(BmsEngineRules.GetTiming(note, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.302));
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2()];
        score.ApplyBeatmap(beatmap);
        note.HitWindows.SetDifficulty(5);
        Assert.That(note.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(26.302));
    }

    [Test]
    public void ExRankEventsKeepParsedTiming()
    {
        var parsed = BmsChartParser.Parse([
            "#BPM 120", "#RANK 4", "#WAV01 note.wav", "#EXRANK01 167", "#00011:01", "#001A0:01", "#00111:01",
        ]);
        var beatmap = createBeatmap(parsed);
        Assert.That(beatmap.HitObjects.Select(note => note.StartTime), Is.EqualTo(new[] { 0d, 2000 }));
        Assert.That(beatmap.HitObjects.Select(note => BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja)), Is.EqualTo(new[] { .75, 1.2525 }));

        Assert.That(BmsEngineRules.GetTiming(beatmap.HitObjects[0], false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(BmsEngineRules.GetTiming(beatmap.HitObjects[1], false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.302));
    }

    [Test]
    public void CoincidentExRankEventsUseTheLastDefinitionAcrossStopsAndPractice()
    {
        var parsed = BmsChartParser.Parse([
            "#BPM 120", "#RANK 4", "#WAV01 note.wav", "#STOP01 192", "#EXRANK01 80", "#EXRANK02 167",
            "#00011:01", "#00109:01", "#001A0:01", "#001A0:02", "#00111:01", "#00211:01",
        ]);
        var beatmap = createBeatmap(parsed);
        var second = beatmap.HitObjects[1];
        Assert.That(second.StartTime, Is.EqualTo(2000));
        Assert.That(beatmap.HitObjects[2].StartTime, Is.EqualTo(6000));
        Assert.That(beatmap.HitObjects.Select(note => BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja)), Is.EqualTo(new[] { .75, 1.2525, 1.2525 }));
        var practice = BmsPracticeBeatmap.Create(beatmap, new BmsPracticeSettings { StartTime = second.StartTime, EndTime = second.StartTime + 100 });
        Assert.That(practice.TimingMap, Is.SameAs(beatmap.TimingMap));

        Assert.That(BmsEngineRules.GetTiming(practice.HitObjects.Single(), false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(26.302));
        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2()];
        score.ApplyBeatmap(practice);
        practice.HitObjects.Single().HitWindows.SetDifficulty(5);
        Assert.That(practice.HitObjects.Single().HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(26.302));
    }

    [TestCase(0, 0)]
    [TestCase(.75, 18)]
    [TestCase(1, 21)]
    public void ExplicitRateOverrideStillTakesPrecedence(double rate, double perfectWindow)
    {
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, ExRank = rate * 100 / .75, Column = 1 };

        Assert.That(BmsEngineRules.GetTiming(note, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Perfect), Is.EqualTo(perfectWindow));
    }

    [Test]
    public void FrameworkWindowsUseSelectedConfigurationAndPreserveSource()
    {
        var source = createBeatmap();
        source.Rank = 4;
        var note = new BmsNote { Beatmap = source, ExRank = 100, Column = 1 };
        source.HitObjects.Add(note);
        // Conversion requires the parser's timing/layout contract.
        source.CopyFrom(BmsChartParser.Parse(["#BPM 120", "#00018:01"]));
        var converted = convertBeatmap(source);
        using var originalScore = new BmsScoreProcessor();
        originalScore.ApplyBeatmap(source);
        using var convertedScore = new BmsScoreProcessor();
        convertedScore.Mods.Value = [new BmsModLr2()];
        convertedScore.ApplyBeatmap(converted);
        var original = note.HitWindows;
        var windows = converted.HitObjects.Single().HitWindows;
        Assert.That(original.WindowFor(HitResult.Perfect), Is.EqualTo(15));
        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(18));
        windows.SetDifficulty(5);
        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(original.WindowFor(HitResult.Perfect), Is.EqualTo(15));
    }

    [Test]
    public void WindowConstraintsAreOrderIndependent()
    {
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = BmsLayoutVariant.Popn9K;
        beatmap.TotalColumns = 9;
        beatmap.Rank = 4;
        var note = new BmsNote { Beatmap = beatmap, StartTime = 1000, Column = 1 };
        beatmap.HitObjects.Add(note);
        var firstProfile = GameplayProfile.FromMods([new BmsModNoGood(), new BmsModLr2(), new BmsModNoBad()]);
        var secondProfile = GameplayProfile.FromMods([new BmsModNoBad(), new BmsModLr2(), new BmsModNoGood()]);
        var first = new BmsEngineChart(beatmap, firstProfile).TimingFor(note);
        var second = new BmsEngineChart(beatmap, secondProfile).TimingFor(note);
        Assert.That(second.HeadWindows, Is.SameAs(first.HeadWindows));
        Assert.That(second.HeadWindows.ResultFor(70_000), Is.Null);
        Assert.That(second.HeadWindows.ResultFor(20_000), Is.EqualTo(BmsResults.GREAT));
        Assert.That(second.HeadWindows, Is.SameAs(BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 2, false,
            BmsJudgementModifiers.Lr2oraja | BmsJudgementModifiers.NoGood | BmsJudgementModifiers.NoBad).Windows));
        Assert.That(second.HeadWindows.Entries.Min(window => window.Early), Is.EqualTo(-1_000_000));
        Assert.That(second.NonResolvingHeadResults, Does.Not.Contain(BmsResults.BAD));
    }

    [Test]
    public void EmptyPoorIsEarlyOnlyAndSearchExtendsPast500ms()
    {
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000 };
        var output = new BmsRecordingJudgementOutput();
        beatmap.HitObjects.AddRange([note]);
        var column = BmsEngineTestAccess.CreateEngine(beatmap, 1, output, lr2_profile).GetColumn(1);
        Assert.That(column.HandleNotePress(new BmsPressContext(100, new BmsFrameContext(100, 1))).Kind, Is.EqualTo(PressOutcomeKind.EmptyPoor));
        var judged = new BmsJudgementCandidate(1000, 1000, 1, 0.75, false, IsJudged: true);
        Assert.That(BmsTestJudgementSelector.SelectPress(beatmap.LayoutVariant, 1, [judged], 1050, modifiers: BmsJudgementModifiers.Lr2oraja).Result, Is.EqualTo(HitResult.None));
    }

    [Test]
    public void ModOverridesUserNoteSelection([Values] BmsJudgementAlgorithm algorithm)
    {
        BmsJudgementCandidate[] notes = [new(1000, 1000, 1, 0.75, false), new(1160, 1160, 1, 0.75, false)];
        var selection = BmsTestJudgementSelector.SelectPress(BmsLayoutVariant.Bme7K, 1, notes, 1150, algorithm, BmsJudgementModifiers.Lr2oraja);
        Assert.That(selection.Candidate?.StartTime, Is.EqualTo(1160));
        Assert.That(selection.Result, Is.EqualTo(HitResult.Perfect));
    }

    [TestCase(1400, HitResult.Perfect, BmsLayoutVariant.Bme7K)]
    [TestCase(1399.99, HitResult.Ok, BmsLayoutVariant.Bme7K)]
    [TestCase(1500, HitResult.Perfect, BmsLayoutVariant.Bme7K)]
    [TestCase(1400, HitResult.Perfect, BmsLayoutVariant.Popn9K)]
    [TestCase(1399.99, HitResult.Ok, BmsLayoutVariant.Popn9K)]
    [TestCase(1500, HitResult.Perfect, BmsLayoutVariant.Popn9K)]
    public void LongNoteReleaseKeepsHeadOrBecomesBad(double release, HitResult expected,
                                                     BmsLayoutVariant layout)
    {
        var context = createLongNoteContext(layout);
        context.Press(1000);
        context.Release(release);
        Assert.That(context.Results, Is.EqualTo([expected]));
    }

    [Test]
    public void LongNoteEarlyBadIsImmediateAndLateHeadExpiresAtGoodWindow()
    {
        var context = createLongNoteContext();
        context.Press(850);
        Assert.That(context.Results, Is.EqualTo([HitResult.Ok]));
        var late = createLongNoteContext();
        late.Passive(1100.001);
        Assert.That(late.Results, Is.EqualTo([HitResult.Meh]));
    }

    [Test]
    public void LongNoteDeadlineSurvivesWindowConstraints([Values] bool noGreat)
    {
        var beatmap = createBeatmap();
        var profile = GameplayProfile.FromMods([new BmsModLr2(), noGreat ? new BmsModNoGreat() : new BmsModNoGood()]);
        var note = new BmsLongNote { Kind = beatmap.LockedLongNoteMode.ToNoteKind(), Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000, Duration = 500 };
        var output = new BmsRecordingJudgementOutput();
        beatmap.HitObjects.AddRange([note]);
        var engine = BmsEngineTestAccess.CreateEngine(beatmap, 1, output, profile);
        engine.QueueInput(1010, 1, true, false);
        engine.ProcessFrame(new BmsFrameContext(1010, 1), 1010);
        engine.QueueInput(1400, 1, false, false);
        engine.ProcessFrame(new BmsFrameContext(1400, 1), 390);
        Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void VeryShortMissedLongNoteSettlesWithoutRepeatedDeadlines()
    {
        var beatmap = createBeatmap();
        var note = new BmsLongNote { Kind = beatmap.LockedLongNoteMode.ToNoteKind(), Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000, Duration = 10 };
        var output = new BmsRecordingJudgementOutput();
        beatmap.HitObjects.AddRange([note]);
        var engine = BmsEngineTestAccess.CreateEngine(beatmap, 1, output, lr2_profile);
        engine.ProcessFrame(new BmsFrameContext(2000, 1), 2000);
        Assert.That(output.GetMainResult(note).Type, Is.EqualTo(HitResult.Meh));
        Assert.That(engine.GetColumn(note.Column).GetNote(note).Completed, Is.True);
    }

    [Test]
    public void ComboRulesBelongToTheSelectedProfile([Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Popn9K)] BmsLayoutVariant layout)
    {
        using var normal = new BmsScoreProcessor();
        using var lr2 = new BmsScoreProcessor();
        var beatmap = new BmsBeatmap { LayoutVariant = layout };
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        normal.ApplyBeatmap(beatmap);
        lr2.Mods.Value = [new BmsModLr2()];
        lr2.ApplyBeatmap(beatmap);
        normal.Combo.Value = 5;
        lr2.Combo.Value = 5;
        normal.ApplyTestEmptyPoor();
        lr2.ApplyTestEmptyPoor();
        Assert.That(normal.Combo.Value, Is.Zero);
        Assert.That(lr2.Combo.Value, Is.EqualTo(5));
    }

    [Test]
    public void EquivalentBaseDifficultiesShareWindowTables()
    {
        var normal = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 2, false, BmsJudgementModifiers.Lr2oraja);
        Assert.That(BmsEngineRules.GetTiming(BmsLayoutVariant.Popn9K, 1, 2, false, BmsJudgementModifiers.Lr2oraja), Is.SameAs(normal));
        Assert.That(BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 4, false, BmsJudgementModifiers.Lr2oraja), Is.SameAs(normal));
        Assert.That(BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, .75, false, BmsJudgementModifiers.Lr2oraja), Is.SameAs(normal));
    }

    [TestCase(1, 160.16)]
    [TestCase(400, 224)]
    [TestCase(500, 256)]
    [TestCase(600, 288)]
    [TestCase(1000, 352)]
    public void DefaultTotal(int notes, double expected)
    {
        var profile = ((BmsGameplayProfile)GameplayProfile.FromMods([new BmsModLr2()])).CreateGaugeProfile(BmsGaugeType.Normal, BmsGaugeProfileFamily.SevenKeys);
        Assert.That(new BmsGaugeCalculator(profile, null, notes).Total, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase(144, 1000, 2.5)]
    [TestCase(160, 1000, 2)]
    [TestCase(240, 20, 10)]
    [TestCase(240, 500, 2)]
    [TestCase(240, 1000, 1)]
    public void HardDamageCorrection(double total, int notes, double scale)
    {
        var profile = ((BmsGameplayProfile)GameplayProfile.FromMods([new BmsModLr2()])).CreateGaugeProfile(BmsGaugeType.Hard, BmsGaugeProfileFamily.SevenKeys);
        var calculator = new BmsGaugeCalculator(profile, total, notes);
        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 1), Is.EqualTo(-0.06 * scale).Within(1e-9));
        Assert.That(calculator.GetDeltaFor(BmsResults.PGREAT, 0.5), Is.EqualTo(0.001));
    }

    [Test]
    public void HardGutsAndDeathThreshold()
    {
        var profile = ((BmsGameplayProfile)GameplayProfile.FromMods([new BmsModLr2()])).CreateGaugeProfile(BmsGaugeType.Hard, BmsGaugeProfileFamily.SevenKeys);
        var calculator = new BmsGaugeCalculator(profile, 300, 1000);
        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 0.32), Is.EqualTo(-0.06).Within(1e-9));
        Assert.That(calculator.GetDeltaFor(BmsResults.BAD, 0.3199), Is.EqualTo(-0.036).Within(1e-9));
        Assert.That(calculator.ApplyDelta(0.04, -0.02), Is.EqualTo(0.02).Within(1e-9));
        Assert.That(calculator.ApplyDelta(0.03, -0.01001), Is.Zero);
    }

    [Test]
    public void ProcessorFailsBelowTwoPercentAndResetRestoresSeed()
    {
        var beatmap = createBeatmap();
        for (var i = 0; i < 1000; i++) beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = i * 100 });
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(beatmap);
        health.Mods = [new BmsModLr2()];
        health.Gauge().SetGaugeTypes([BmsGaugeType.Hard], replaceExisting: true, initialStates: [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.03, false)]);
        health.ApplyTestEmptyPoor();
        Assert.That(health.Health.Value, Is.Zero);
        Assert.That(health.CurrentHealth.HasFailed, Is.True);
        Assert.That(health.Gauge().CurrentGaugeStates.Single().Failed, Is.True);
        health.ResetReplayState();
        Assert.That(health.Health.Value, Is.EqualTo(0.03));
        Assert.That(health.Gauge().CurrentGaugeStates.Single().Failed, Is.False);
    }

    [Test]
    public void PractisedIntervalUsesTheFullChartDefaultTotal()
    {
        var beatmap = createBeatmap();
        beatmap.Total = null;
        for (var i = 0; i < 1000; i++) beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = i * 100 });
        beatmap = BmsPracticeBeatmap.Create(beatmap, new BmsPracticeSettings { StartTime = 0, EndTime = 10000 }, [new BmsModLr2()]);
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(beatmap);
        health.Mods = [new BmsModLr2()];
        health.ApplyTestBodyTick(true);
        health.ApplyTestBodyTick(true);
        Assert.That(health.Health.Value, Is.EqualTo(0.20352).Within(1e-9));
    }

    [Test]
    public void GaugeApplicationOrderAndAutoGaugeAreStable([Values] bool lr2First,
                                                           [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Popn9K)]
                                                           BmsLayoutVariant layout)
    {
        var mod = new BmsModLr2();
        var auto = new BmsModAutoGauge();
        using var health = new BmsHealthProcessor();
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = layout;
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000 });
        health.ApplyBeatmap(beatmap);
        if (lr2First) health.Mods = [mod, auto];
        auto.ApplyToHealthProcessor(health);
        if (!lr2First) health.Mods = [auto, mod];
        foreach (var state in health.Gauge().CurrentGaugeStates.Where(state => state.GaugeType is BmsGaugeType.Normal or BmsGaugeType.Easy or BmsGaugeType.AssistEasy))
            Assert.That(state.Health, Is.EqualTo(0.2).Within(1e-9));
        health.ApplyTestEmptyPoor();
        Assert.That(health.Gauge().CurrentGaugeStates.Single(state => state.GaugeType == BmsGaugeType.Hazard).Health, Is.EqualTo(1));
        Assert.That(health.Gauge().CurrentGaugeStates.Single(state => state.GaugeType == BmsGaugeType.Normal).Health, Is.EqualTo(0.18).Within(1e-9));
        var expected = health.Gauge().CurrentGaugeStates.ToArray();
        health.Mods = [.. health.Mods];
        Assert.That(health.Gauge().CurrentGaugeStates, Is.EqualTo(expected));
    }

    [Test]
    public void ProfileMigrationPreservesExplicitSeedsAndStartedGameplay([Values] bool explicitSeed)
    {
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = BmsLayoutVariant.Popn9K;
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000 });
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(beatmap);

        if (explicitSeed)
            health.Gauge().SetGaugeTypes([BmsGaugeType.Normal], replaceExisting: true, initialStates: [new BmsGaugeStateSnapshot(BmsGaugeType.Normal, .3, false)]);
        else
            health.ApplyTestEmptyPoor();

        var startingHealth = health.Health.Value;
        health.Mods = [new BmsModLr2()];
        Assert.That(health.Gauge().GaugeProfile.InitialHealth, Is.EqualTo(.2));
        Assert.That(health.Health.Value, Is.EqualTo(startingHealth).Within(1e-9));
        health.ApplyTestEmptyPoor();
        Assert.That(health.Health.Value, Is.EqualTo(startingHealth - .02).Within(1e-9));
        health.Mods = [];
        Assert.That(health.Gauge().GaugeProfile.InitialHealth, Is.EqualTo(.3));
        Assert.That(health.Health.Value, Is.EqualTo(startingHealth - .02).Within(1e-9));
        health.ApplyTestNonConsumingJudgement(HitResult.Ok);
        Assert.That(health.Health.Value, Is.EqualTo(startingHealth - .04).Within(1e-9));
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
    public void ConvertsChartLongNotes()
    {
        var beatmap = createBeatmap();
        beatmap.LockedLongNoteMode = BmsLongNoteMode.HellChargeNote;
        new BmsModLr2().ApplyToBeatmap(beatmap);
        Assert.That(beatmap.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.LongNote));
    }

    [Test]
    public void DifficultyUsesOriginalRankRatherThanLayoutPercentage([Values] bool extendedRank)
    {
        var beatmap = createBeatmap();
        beatmap.TimingMap = BmsChartParser.Parse([]).TimingMap;
        beatmap.LayoutVariant = BmsLayoutVariant.Popn9K;
        beatmap.TotalColumns = 9;
        beatmap.Rank = 4;
        beatmap.ExRank = extendedRank ? 100 : null;
        beatmap.HitObjects.AddRange([
            new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 0 },
            new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 2, StartTime = 250 },
        ]);
        new BmsDifficultyInfo { Rank = 4, ExRank = beatmap.ExRank, LayoutVariant = BmsLayoutVariant.Popn9K }.WriteToOsuDifficulty(beatmap);
        var calculator = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new TestWorkingBeatmap(beatmap));
        calculator.Calculate([new BmsModLr2()]);
        Assert.That(calculator.StarRatingProcessor.HitLeniencyX, Is.EqualTo(BmsHitLeniency.FromGreatWindow(40)).Within(1e-12));
    }

    [Test]
    public void DisplayMetricsUseExplicitModsWithoutChangingOtherProfiles()
    {
        var info = new BeatmapInfo(new BmsRuleset().RulesetInfo);
        new BmsDifficultyInfo { Rank = 2, LayoutVariant = BmsLayoutVariant.Bme7K, Total = 300, LockedLongNoteMode = BmsLongNoteMode.ChargeNote }.WriteToOsuDifficulty(info);
        var defaultTable = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 2, false);
        var attributes = new BmsRuleset().GetBeatmapAttributesForDisplay(info, [new BmsModLr2()]).ToArray();
        Assert.That(attributes, Is.Not.Empty);
        Assert.That(attributes.Single(attribute => attribute.Acronym == "RK").AdditionalMetrics
            .Single(metric => metric.Name.ToString() == "Normal note PGREAT").Value.ToString(), Is.EqualTo("-18 to +18 ms"));
        Assert.That(attributes.Single(attribute => attribute.Acronym == "LM").AdjustedValue, Is.EqualTo((float)BmsLongNoteMode.LongNote));
        Assert.That(BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 1, 2, false), Is.SameAs(defaultTable));
    }

    [Test]
    public void ExtendedRankUsesRawPercentageInEveryLayout([Values(0, 80, 100)] int exRank,
                                                           [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Popn9K)]
                                                           BmsLayoutVariant layout)
    {
        var beatmap = createBeatmap(BmsChartParser.Parse([
            "#RANK 3", $"#DEFEXRANK {exRank}", "#WAV01 note.wav", "#00011:01",
        ], layout == BmsLayoutVariant.Popn9K ? "rank.pms" : "rank.bms"));
        var note = beatmap.HitObjects.Single();
        Assert.That(BmsEngineRules.RateFor(note, BmsJudgementModifiers.Lr2oraja), Is.EqualTo(exRank * .75 / 100).Within(1e-12));
        Assert.That(BmsEngineRules.GetTiming(note, false, BmsJudgementModifiers.Lr2oraja).FrameworkWindowFor(HitResult.Great), Is.EqualTo(exRank == 0 ? 0 : exRank == 80 ? 34 : 40));
    }

    [Test]
    public void ReplayAppliesProfilesWithAutoGaugeAndCourseSeeds([Values] bool course)
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000 });
        var replay = new Game.Replays.Replay
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
        Assert.That(initial.Layers.Single(layer => layer.Name == BmsGaugeProfileFactory.NameFor(course ? BmsGaugeType.Class : BmsGaugeType.Normal)).Value,
            Is.EqualTo(course ? 0.48 : 0.18).Within(1e-9));
    }

    [Test]
    public void LiveInputsArchiveAndOfflineReplayProduceTheSameResults([Values(1, 1.5)] double rate, [Values] bool autoFirst,
                                                                       [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Popn9K)]
                                                                       BmsLayoutVariant layout)
    {
        var mod = new BmsModLr2();
        Mod[] mods = autoFirst ? [new BmsModAutoGauge(), mod] : [mod, new BmsModAutoGauge()];
        if (rate != 1) mods = [new BmsModDoubleTime(), .. mods];

        var beatmap = createBeatmap();
        beatmap.LayoutVariant = layout;
        beatmap.HitObjects.AddRange([
            new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000 },
            new BmsLongNote { Kind = beatmap.LockedLongNoteMode.ToNoteKind(), Beatmap = beatmap, ExRank = 100, Column = 2, StartTime = 1500, Duration = 500 },
            new BmsLandmine { Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1800, LandmineDamagePercent = 10 },
            new BmsNote { Beatmap = beatmap, ExRank = 100, Column = 3, StartTime = 2200 },
        ]);
        foreach (var note in beatmap.HitObjects) note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2()];
        using var health = new BmsHealthProcessor();
        health.Mods = mods;
        score.Mods.Value = mods;
        score.ApplyBeatmap(beatmap);
        health.ApplyBeatmap(beatmap);
        foreach (var item in mods.OfType<IApplicableToHealthProcessor>()) item.ApplyToHealthProcessor(health);
        var gauges = new List<BmsGaugeStateSnapshot[]>();
        var output = new BmsReplaySimulation.SimulationOutput(score, health, _ => gauges.Add(health.Gauge().CurrentGaugeStates.ToArray()));
        var engine = BmsEngineTestAccess.CreateEngine(beatmap, Enumerable.Range(1, 3)
                .Select(column => new BmsJudgementColumnDefinition(column, output)).ToArray(),
            health.CreateEngineHealth, lr2_profile);
        (double Time, int Column, bool Pressed)[] inputs = [(100, 1, true), (110, 1, false), (1017, 1, true), (1025, 1, false), (1500, 2, true), (1900, 2, false)];
        var replay = new Game.Replays.Replay { HasReceivedAllFrames = true };

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
        Assert.That(result.Gauges.Skip(1).SelectMany(entry => entry.Health.Layers), Is.EqualTo(gauges.SelectMany(entry => BmsTestHealth.ToState(entry).Layers)));
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
        return (BmsBeatmap)new BmsBeatmapConverter(source, new BmsRuleset()).Convert();
    }

    private static BmsLongNoteTestContext createLongNoteContext(BmsLayoutVariant layout = BmsLayoutVariant.Bme7K)
    {
        var beatmap = createBeatmap();
        beatmap.LayoutVariant = layout;
        beatmap.TotalColumns = layout == BmsLayoutVariant.Popn9K ? 9 : 8;
        return new BmsLongNoteTestContext(new BmsLongNote { Kind = beatmap.LockedLongNoteMode.ToNoteKind(), Beatmap = beatmap, ExRank = 100, Column = 1, StartTime = 1000, Duration = 500 }, profile: lr2_profile);
    }

    private sealed class TestManiaNote : HitObject, IHasColumn
    {
        public int Column { get; init; }
    }
}
