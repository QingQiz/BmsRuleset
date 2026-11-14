using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsBeatorajaParityTest
{
    [TestCase(BmsGaugeType.Normal)]
    [TestCase(BmsGaugeType.Easy)]
    [TestCase(BmsGaugeType.AssistEasy)]
    public void RecoveryGaugeCanRecoverAfterRepeatedMisses(BmsGaugeType gauge)
    {
        var processor = new BmsHealthProcessor();
        var note = new BmsNote();
        processor.SetGaugeType(gauge);
        processor.ApplyBeatmap(new BmsBeatmap { Total = 10, HitObjects = { note } });
        for (var i = 0; i < 100; i++)
            processor.ApplyTestEmptyPoor();
        Assert.That(processor.Health.Value, Is.EqualTo(0.02));
        Assert.That(processor.HasEverFailed, Is.False);
        processor.ApplyTestResult(new BmsJudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(processor.Health.Value, Is.EqualTo(0.12).Within(1e-9));
    }

    [TestCase(BmsLongNoteMode.LongNote, 0.3)]
    [TestCase(BmsLongNoteMode.ChargeNote, 0.25)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 0.25)]
    public void ChargeEndpointsShareTotalRecovery(BmsLongNoteMode mode, double expected)
    {
        var note = new BmsLongNote();
        var beatmap = new BmsBeatmap { Total = 10, LockedLongNoteMode = mode, HitObjects = { note } };
        note.Beatmap = beatmap;
        var processor = new BmsHealthProcessor();
        processor.ApplyBeatmap(beatmap);
        processor.ApplyTestResult(new BmsJudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(processor.Health.Value, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase(BmsLayoutVariant.Bme7K, true)]
    [TestCase(BmsLayoutVariant.Popn9K, false)]
    public void ConsumedNoteCanCauseEmptyPoorExceptInPms(BmsLayoutVariant layout, bool expected)
    {
        var candidate = new BmsJudgementCandidate(1000, 1000, 1, 0.75, false, IsJudged: true);
        var selection = BmsTestJudgementSelector.SelectPress(layout, 1, [candidate], 1050);
        Assert.That(selection.IsEmptyPoor, Is.EqualTo(expected));
    }

    [Test]
    public void PmsMistakeBlocksRepeatedBadButAllowsSuccessfulRehit()
    {
        var candidate = new BmsJudgementCandidate(1000, 1000, 1, 0.7, false, HasMistake: true);
        Assert.That(BmsTestJudgementSelector.SelectPress(BmsLayoutVariant.Popn9K, 1, [candidate], 850).Result, Is.EqualTo(HitResult.None));
        Assert.That(BmsTestJudgementSelector.SelectPress(BmsLayoutVariant.Popn9K, 1, [candidate], 1000).Result, Is.EqualTo(HitResult.Perfect));
    }

    [TestCase(BmsLayoutVariant.Bme7K, 3)]
    [TestCase(BmsLayoutVariant.Bms5K, 0)]
    [TestCase(BmsLayoutVariant.Popn9K, 0)]
    public void EmptyPoorComboRuleAndReset(BmsLayoutVariant layout, int expectedCombo)
    {
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(new BmsBeatmap { LayoutVariant = layout });
        processor.Combo.Value = 3;
        processor.ApplyTestEmptyPoor(1000, 1100, 1);
        Assert.That(processor.Combo.Value, Is.EqualTo(expectedCombo));
        processor.ResetReplayState();
        Assert.That(processor.Combo.Value, Is.Zero);
    }

    [Test]
    public void PmsBadDoesNotConsumeScoringJudgement()
    {
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Popn9K });
        processor.Combo.Value = 3;
        processor.ApplyTestNonConsumingJudgement(HitResult.Ok, 850, 1000, 1);
        Assert.That(processor.Combo.Value, Is.Zero);
        Assert.That(processor.ScoringJudgementEventCount, Is.Zero);
        Assert.That(processor.JudgementEvents[0].Source.IsScoring, Is.False);
        processor.ResetReplayState();
        Assert.That(processor.Combo.Value, Is.Zero);
        Assert.That(processor.JudgementEvents, Is.Empty);
    }

    [Test]
    public void SuppressedPmsPoorStillCountsForAccuracyAndRevertsWithoutPenalty()
    {
        var first = new BmsNote();
        var missed = new BmsNote { StartTime = 1000 };
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Popn9K, HitObjects = { first, missed } };
        first.Beatmap = beatmap;
        missed.Beatmap = beatmap;
        var score = new BmsScoreProcessor();
        var health = new BmsHealthProcessor();
        score.ApplyBeatmap(beatmap);
        health.ApplyBeatmap(beatmap);
        score.ApplyTestResult(new BmsJudgementResult(first, first.CreateJudgement()) { Type = HitResult.Perfect });
        var result = new BmsJudgementResult(missed, missed.CreateJudgement()) { Type = HitResult.Meh, SuppressPenalty = true };
        var oldHealth = health.Health.Value;
        score.ApplyTestResult(result);
        health.ApplyTestResult(result);
        Assert.That(score.Accuracy.Value, Is.EqualTo(0.5));
        Assert.That(score.Combo.Value, Is.EqualTo(1));
        Assert.That(score.HighestCombo.Value, Is.EqualTo(1));
        Assert.That(score.ScoringJudgementEventCount, Is.EqualTo(2));
        Assert.That(score.JudgementEvents, Has.Count.EqualTo(2));
        Assert.That(score.JudgementEvents[1].Source.IsScoring, Is.True);
        Assert.That(score.JudgementEvents[1].SuppressPenalty, Is.True);
        Assert.That(BmsExScore.CreateProgression(score.JudgementEvents), Is.EqualTo(new[] { 0, 2, 2 }));
        var saved = new ScoreInfo();
        score.PopulateScore(saved);
        Assert.That(saved.Statistics.GetValueOrDefault(HitResult.Meh), Is.Zero);
        Assert.That(saved.HitEvents.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect }));
        Assert.That(health.Health.Value, Is.EqualTo(oldHealth));
        score.RevertResult(result);
        Assert.That(score.Accuracy.Value, Is.EqualTo(1));
        Assert.That(score.Combo.Value, Is.EqualTo(1));
        Assert.That(score.HighestCombo.Value, Is.EqualTo(1));
        Assert.That(score.ScoringJudgementEventCount, Is.EqualTo(1));
        Assert.That(BmsExScore.CreateProgression(score.JudgementEvents), Is.EqualTo(new[] { 0, 2 }));
        score.ApplyTestResult(result);
        Assert.That(score.Accuracy.Value, Is.EqualTo(0.5));
        Assert.That(score.Combo.Value, Is.EqualTo(1));
        Assert.That(score.JudgementEvents.Count(e => e.SuppressPenalty), Is.EqualTo(1));
    }
}
