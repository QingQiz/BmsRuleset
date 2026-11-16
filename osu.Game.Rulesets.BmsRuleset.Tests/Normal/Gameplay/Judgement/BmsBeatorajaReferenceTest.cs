using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture, NonParallelizable]
public class BmsBeatorajaReferenceTest
{
    [Test]
    public void NonConsumingPmsBadRemainsInTimingStatistics()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Popn9K, TotalColumns = 9 };
        using var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);
        processor.ApplyTestNonConsumingJudgement(HitResult.Ok, 850, 1000, 1);
        var saved = new ScoreInfo();
        processor.PopulateScore(saved);
        var statistics = BmsHitScatterStatistic.CreateStatistics(BmsGameplayProfile.INSTANCE, beatmap, saved.HitEvents);
        Assert.That(statistics.Keys[1].Data.Points.Single().DisplayOffset(statistics.Keys[1].Data.OffsetRange), Is.EqualTo(-150));
        Assert.That(statistics.Keys[0].Data.Points, Is.Empty);
        Assert.That(processor.ScoringJudgementEventCount, Is.Zero);
        Assert.That(saved.Statistics.GetValueOrDefault(HitResult.Ok), Is.EqualTo(1));
        Assert.That(statistics.Overall.Points, Has.Count.EqualTo(1), "beatoraja updateMicro records BAD in its recent timing observations even when it does not consume the note.");
    }

    [Test]
    public void StandardRankTablesMatchExecutedJavaReference()
    {
        using var stream = typeof(BmsBeatorajaReferenceTest).Assembly.GetManifestResourceStream(
            "osu.Game.Rulesets.BmsRuleset.Tests.reference_fixtures.beatoraja_windows.csv")!;
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Miss];
        Assert.Multiple(() =>
        {
            foreach (var line in lines)
            {
                var fields = line.Split(',');
                var layout = fields[0] switch { "FIVEKEYS" => BmsLayoutVariant.Bms5K, "SEVENKEYS" => BmsLayoutVariant.Bme7K, _ => BmsLayoutVariant.Popn9K };
                var column = fields[1].Contains("SCRATCH") ? 0 : 1;
                var table = BmsEngineRules.GetTiming(layout, column, int.Parse(fields[2]), fields[1].Contains("END"));
                var result = results[int.Parse(fields[3])];
                Assert.That(table.SlowWindowFor(result), Is.EqualTo(-long.Parse(fields[4]) / 1000d), line + " slow");
                Assert.That(table.FastWindowFor(result), Is.EqualTo(long.Parse(fields[5]) / 1000d), line + " fast");
            }
        });
    }

    [Test]
    public void SuppressedPmsPoorKeepsPersonalBestNotePosition()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Popn9K,
            HitObjects = [new BmsNote { StartTime = 1000 }, new BmsNote { StartTime = 2000 }, new BmsNote { StartTime = 3000 }]
        };
        foreach (var note in beatmap.HitObjects)
            note.Beatmap = beatmap;
        using var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);
        var notes = beatmap.HitObjects;
        processor.ApplyTestResult(new BmsJudgementResult(notes[0], notes[0].CreateJudgement()) { Type = HitResult.Perfect });
        processor.ApplyTestEmptyPoor(1600, 2000, 0);
        processor.ApplyTestResult(new BmsJudgementResult(notes[1], notes[1].CreateJudgement()) { Type = HitResult.Meh, SuppressPenalty = true });
        processor.ApplyTestResult(new BmsJudgementResult(notes[2], notes[2].CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(BmsScoreGraph.ScoreAtProgress(4, BmsTestExScore.States(BmsTestExScore.CreateProgression(processor.JudgementEvents)), 2, 3), Is.EqualTo(2),
            "After two consumed notes the recorded PB has only its first PGREAT; the third note must not advance early.");
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void PmsDeferredReleasePreservesReleaseTiming(BmsLongNoteMode mode)
    {
        var run = new BmsLongNoteTestContext(mode, duration: 2000, layout: BmsLayoutVariant.Popn9K);
        run.Press(1000);
        run.Release(1500);
        run.Frame(1700, 200, false);
        Assert.That((mode == BmsLongNoteMode.LongNote ? run.MainResults.Last().Endpoints.Last() : run.Output.SyntheticEndpoints.Last()).TimeOffset, Is.EqualTo(-1500),
            "JudgeManager uses tailTime - releasetime when the grace period expires.");
    }

    [Test]
    public void ExactEarlySearchLimitDoesNotCauseEmptyPoor(
        [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Popn9K)]
        BmsLayoutVariant layout,
        [Values(499.999, 500, 500.001)] double pressTime)
    {
        var candidate = new BmsJudgementCandidate(1000, 1000, 1, BmsEngineRules.RateForRank(layout, 2), false);
        Assert.That(BmsTestJudgementSelector.SelectPress(layout, 1, [candidate], pressTime).Result, Is.EqualTo(pressTime > 500 ? HitResult.Miss : HitResult.None),
            "JudgeManager exits the search at dmtime >= mjudgeend (500000us). The row itself has an inclusive boundary.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HcnBodySettlesElapsedTimeThroughTheTail(bool holding)
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, duration: 201, layout: BmsLayoutVariant.Bme7K);
        run.Press(1000);
        run.Frame(1200, 200, holding);
        Assert.That(run.Output.BodyTicks, Is.Empty);
        run.Frame(1201, 1, holding);
        Assert.That(run.Output.BodyTicks.Single().Time, Is.EqualTo(1200.001));
        run.Frame(1400, 199, holding);
        Assert.That(run.Output.BodyTicks, Has.Count.EqualTo(1));
    }

    [Test]
    public void HcnDelayedFrameSettlesAllElapsedTicks()
    {
        var run = new BmsLongNoteTestContext(BmsLongNoteMode.HellChargeNote, duration: 2000, layout: BmsLayoutVariant.Bme7K);
        run.Press(1000);
        run.Frame(1650, 650, true);
        Assert.That(run.Output.BodyTicks.Select(tick => tick.Time), Is.EqualTo(new[]
        {
            1200.001, 1400.001, 1600.001,
        }));
    }

    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void ScratchChargeTailWaitsForReversePress(BmsLongNoteMode mode)
    {
        var note = new BmsLongNote
        {
            Kind = mode.ToNoteKind(),
            StartTime = 1000, Duration = 1000, Column = 0,
            Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode }
        };
        var run = new BmsLongNoteTestContext(note);
        run.Press(1000);
        run.Release(2000);
        Assert.That(run.State.TailJudged, Is.False,
            "JudgeManager ignores scratch key-up inside tail hit windows; the reverse-direction key-down judges the tail.");
    }

    [TestCase(BmsLayoutVariant.Bms5K, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bms5K, 0.56, 11.2)]
    [TestCase(BmsLayoutVariant.Bms5K, 0.74, 14.8)]
    [TestCase(BmsLayoutVariant.Bms5K, 0.99, 19.8)]
    [TestCase(BmsLayoutVariant.Bme7K, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bme7K, 0.56, 11.2)]
    [TestCase(BmsLayoutVariant.Bme7K, 0.74, 14.8)]
    [TestCase(BmsLayoutVariant.Bme7K, 0.99, 19.8)]
    [TestCase(BmsLayoutVariant.Popn9K, 0.7, 20)]
    [TestCase(BmsLayoutVariant.Popn9K, 0.52, 20)]
    [TestCase(BmsLayoutVariant.Popn9K, 0.69, 20)]
    [TestCase(BmsLayoutVariant.Popn9K, 0.93, 20)]
    public void ValidatedJavaRatesMatchPgreatBoundaries(BmsLayoutVariant layout, double rate, double pgreat)
    {
        // Keep checking the executed Java window rows independently of its integer EXRANK conversion.
        var table = BmsEngineRules.GetTiming(layout, 1, rate, false);

        foreach (var direction in new[] { -1, 1 })
        {
            Assert.That(table.HitAt(direction * pgreat), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.HitAt(direction * (pgreat + 0.001)), Is.EqualTo(HitResult.Great));
        }
    }
}
