using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture, NonParallelizable]
public class BmsBeatorajaReferenceTest
{
    [Test]
    public void NonConsumingPmsBadRemainsInTimingStatistics()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Pms9K, TotalColumns = 9 };
        using var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);
        processor.RegisterNonConsumingJudgement(HitResult.Ok, 850, 1000, 1);
        var saved = new ScoreInfo();
        processor.PopulateScore(saved);
        var statistics = BmsHitScatterStatistic.CreateStatistics(beatmap, saved.HitEvents);
        Assert.That(statistics.Keys[1].Data.Points.Single().Offset, Is.EqualTo(-150));
        Assert.That(statistics.Keys[0].Data.Points, Is.Empty);
        Assert.That(processor.ScoringJudgementEventCount, Is.Zero);
        Assert.That(saved.Statistics.GetValueOrDefault(HitResult.Ok), Is.EqualTo(1));
        Assert.That(statistics.Overall.Points, Has.Count.EqualTo(1), "beatoraja updateMicro records BAD in its recent timing observations even when it does not consume the note.");
    }

    [Test]
    public void SuppressedPmsPoorDoesNotProduceHitErrorMarker()
    {
        var note = new BmsNote { StartTime = 1000 };
        var result = new BmsJudgementResult(note, note.CreateJudgement()) { Type = HitResult.Meh, SuppressPenalty = true };
        Assert.That(BmsHitErrorMeter.GetTimingObservations(result), Is.Empty,
            "OnNewJudgement sends these observations to the hit error meter without a suppression check.");
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
                var layout = fields[0] switch { "FIVEKEYS" => BmsLayoutVariant.Bms5K, "SEVENKEYS" => BmsLayoutVariant.Bme7K, _ => BmsLayoutVariant.Pms9K };
                var column = fields[1].Contains("SCRATCH") ? 0 : 1;
                var table = BmsJudgementProfileProvider.GetTable(layout, column, int.Parse(fields[2]), fields[1].Contains("END"));
                var result = results[int.Parse(fields[3])];
                Assert.That(table.SlowWindowFor(result), Is.EqualTo(-long.Parse(fields[4])/1000d), line + " slow");
                Assert.That(table.FastWindowFor(result), Is.EqualTo(long.Parse(fields[5])/1000d), line + " fast");
            }
        });
    }

    [Test]
    public void SuppressedPmsPoorKeepsPersonalBestNotePosition()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Pms9K,
            HitObjects = [new BmsNote { StartTime = 1000 }, new BmsNote { StartTime = 2000 }, new BmsNote { StartTime = 3000 }] };
        using var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(beatmap);
        var notes = beatmap.HitObjects;
        processor.ApplyResult(new JudgementResult(notes[0], notes[0].CreateJudgement()) { Type = HitResult.Perfect });
        processor.RegisterEmptyPoor(1600, 2000, 0);
        processor.ApplyResult(new BmsJudgementResult(notes[1], notes[1].CreateJudgement()) { Type = HitResult.Meh, SuppressPenalty = true });
        processor.ApplyResult(new JudgementResult(notes[2], notes[2].CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(BmsScoreGraph.ScoreAtProgress(4, BmsExScore.CreateProgression(processor.JudgementEvents), 2, 3), Is.EqualTo(2),
            "After two consumed notes the recorded PB has only its first PGREAT; the third note must not advance early.");
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    public void PmsDeferredReleasePreservesReleaseTiming(BmsLongNoteMode mode)
    {
        var (controller, hooks) = makeController(mode, BmsLayoutVariant.Pms9K, 2000);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(1500, -1500, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Pms9K, 1, 2, true));
        controller.UpdatePostResult(1700, 200, false);
        Assert.That(hooks.Endpoints.Last().TimeOffset, Is.EqualTo(-1500), "JudgeManager uses tailTime - releasetime when the grace period expires.");
    }

    [Test]
    public void ExactEarlySearchLimitDoesNotCauseEmptyPoor(
        [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)] BmsLayoutVariant layout,
        [Values(499.999, 500, 500.001)] double pressTime)
    {
        var candidate = new BmsJudgementCandidate(1000, 1000, 1, BmsJudgementProfileProvider.RateForRank(layout, 2), false);
        Assert.That(BmsJudgementSelector.SelectPress(layout, 1, [candidate], pressTime).Result, Is.EqualTo(pressTime > 500 ? HitResult.Miss : HitResult.None),
            "JudgeManager exits the search at dmtime >= mjudgeend (500000us). The row itself has an inclusive boundary.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HcnBodyStopsBeforeEndpointFrame(bool holding)
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, BmsLayoutVariant.Bme7K, 201);
        controller.TryHit(1000, HitResult.Perfect);
        controller.UpdatePostResult(1200, 200, holding);
        Assert.That(hooks.Ticks, Is.Empty);
        controller.UpdatePostResult(1201, 1, holding);
        Assert.That(hooks.Ticks, Is.Empty, "JudgeManager clears passing at the tail before updating the body accumulator.");
    }

    [Test]
    public void HcnDelayedFrameAppliesAtMostOneBodyTick()
    {
        var (controller, hooks) = makeController(BmsLongNoteMode.HellChargeNote, BmsLayoutVariant.Bme7K, 2000);
        controller.TryHit(1000, HitResult.Perfect);
        controller.UpdatePostResult(1650, 650, true);
        Assert.That(hooks.Ticks, Has.Count.EqualTo(1), "JudgeManager has one if, rather than a catch-up loop, per update.");
    }

    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void ScratchChargeTailWaitsForReversePress(BmsLongNoteMode mode)
    {
        var note = new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 0,
            Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2, LockedLongNoteMode = mode } };
        var hooks = new Hooks();
        var controller = new BmsLongNoteJudgementController();
        controller.Bind(note, hooks);
        controller.TryHit(1000, HitResult.Perfect);
        controller.TryRelease(2000, 0, BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 0, 2, true));
        Assert.That(controller.TailJudged, Is.False,
            "JudgeManager ignores scratch key-up inside tail hit windows; the reverse-direction key-down judges the tail.");
    }

    [Test]
    public void DefExRankUsesIntegerValidatedRank()
    {
        // BMSPlayerRule.validate: DEFEXRANK 75 => 75 * 75 / 100 = 56 (integer percent).
        var rate = BmsJudgementProfileProvider.RateForExRank(BmsLayoutVariant.Bme7K, 75);
        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, rate, false);
        Assert.That(table.ResultForOffset(11.225), Is.EqualTo(HitResult.Great));
    }

    [TestCase(BmsLayoutVariant.Bms5K, -1, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bms5K, 0, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bms5K, 75, 0.56, 11.2)]
    [TestCase(BmsLayoutVariant.Bms5K, 99, 0.74, 14.8)]
    [TestCase(BmsLayoutVariant.Bms5K, 101, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bms5K, 133, 0.99, 19.8)]
    [TestCase(BmsLayoutVariant.Bme7K, -1, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bme7K, 0, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bme7K, 75, 0.56, 11.2)]
    [TestCase(BmsLayoutVariant.Bme7K, 99, 0.74, 14.8)]
    [TestCase(BmsLayoutVariant.Bme7K, 101, 0.75, 15)]
    [TestCase(BmsLayoutVariant.Bme7K, 133, 0.99, 19.8)]
    [TestCase(BmsLayoutVariant.Pms9K, -1, 0.7, 20)]
    [TestCase(BmsLayoutVariant.Pms9K, 0, 0.7, 20)]
    [TestCase(BmsLayoutVariant.Pms9K, 75, 0.52, 20)]
    [TestCase(BmsLayoutVariant.Pms9K, 99, 0.69, 20)]
    [TestCase(BmsLayoutVariant.Pms9K, 101, 0.7, 20)]
    [TestCase(BmsLayoutVariant.Pms9K, 133, 0.93, 20)]
    public void DefExRankMatchesExecutedJavaAtPgreatBoundary(BmsLayoutVariant layout, int exrank, double rate, double pgreat)
    {
        // DefExReference.java executes the fixed upstream JudgeProperty after BMSPlayerRule's integer validation.
        var actualRate = BmsJudgementProfileProvider.RateForExRank(layout, exrank);
        Assert.That(actualRate, Is.EqualTo(rate));
        var table = BmsJudgementProfileProvider.GetTable(layout, 1, actualRate, false);
        foreach (var direction in new[] { -1, 1 })
        {
            Assert.That(table.ResultForOffset(direction * pgreat), Is.EqualTo(HitResult.Perfect));
            Assert.That(table.ResultForOffset(direction * (pgreat + 0.001)), Is.EqualTo(HitResult.Great));
        }
    }

    private static (BmsLongNoteJudgementController, Hooks) makeController(BmsLongNoteMode mode, BmsLayoutVariant layout, double duration)
    {
        var note = new BmsLongNote { StartTime = 1000, Duration = duration, Column = 1,
            Beatmap = new BmsBeatmap { LayoutVariant = layout, Rank = 2, LockedLongNoteMode = mode } };
        var hooks = new Hooks();
        var controller = new BmsLongNoteJudgementController();
        controller.Bind(note, hooks);
        return (controller, hooks);
    }

    private sealed class Hooks : IBmsLongNoteHooks
    {
        public List<BmsLongNoteEndpointResult> Endpoints { get; } = [];
        public List<(bool Holding, double Scale)> Ticks { get; } = [];
        public void OnUserHeadJudged() { }
        public void OnHellChargeHeadPoor(double eventTime, double lifetimeEnd) { }
        public void ApplyJudgementResult(HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints) => Endpoints.AddRange(endpoints);
        public void ApplySyntheticEndpoint(HitResult result, BmsLongNoteEndpointResult endpoint) => Endpoints.Add(endpoint);
        public void ApplyHellChargeTick(bool holding, double scale) => Ticks.Add((holding, scale));
        public void ClearVisualIfTailWasNotPoor(HitResult result) { }
        public void Retire() { }
    }
}
