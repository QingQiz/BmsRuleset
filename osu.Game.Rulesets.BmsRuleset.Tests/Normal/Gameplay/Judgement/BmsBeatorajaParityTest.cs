using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Scoring;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

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
            processor.RegisterEmptyPoor();
        Assert.That(processor.Health.Value, Is.EqualTo(0.02));
        Assert.That(processor.HasEverFailed, Is.False);
        processor.ApplyResult(new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
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
        processor.ApplyResult(new JudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        Assert.That(processor.Health.Value, Is.EqualTo(expected).Within(1e-9));
    }

    [TestCase(BmsLayoutVariant.Bme7K, true)]
    [TestCase(BmsLayoutVariant.Pms9K, false)]
    public void ConsumedNoteCanCauseEmptyPoorExceptInPms(BmsLayoutVariant layout, bool expected)
    {
        var candidate = new BmsJudgementCandidate(1000, 1000, 1, 0.75, false, IsJudged: true);
        var selection = BmsJudgementSelector.SelectPress(layout, 1, [candidate], 1050);
        Assert.That(selection.IsEmptyPoor, Is.EqualTo(expected));
    }

    [Test]
    public void PmsMistakeBlocksRepeatedBadButAllowsSuccessfulRehit()
    {
        var candidate = new BmsJudgementCandidate(1000, 1000, 1, 0.7, false, HasMistake: true);
        Assert.That(BmsJudgementSelector.SelectPress(BmsLayoutVariant.Pms9K, 1, [candidate], 850).Result, Is.EqualTo(HitResult.None));
        Assert.That(BmsJudgementSelector.SelectPress(BmsLayoutVariant.Pms9K, 1, [candidate], 1000).Result, Is.EqualTo(HitResult.Perfect));
    }

    [Test]
    public void NonConsumingPressHistoryRewindsIndependentlyOfConsumedNotes()
    {
        var history = new BmsNotePressHistory();
        var note = new BmsNote();
        history.Record(note, 800, false, true);
        history.Record(note, 1000, true);
        history.Rewind(900);
        Assert.That(history.Get(note), Is.EqualTo((false, true)));
        history.Rewind(700);
        Assert.That(history.Get(note), Is.EqualTo((false, false)));
    }

    [TestCase(BmsLayoutVariant.Bme7K, 3)]
    [TestCase(BmsLayoutVariant.Bms5K, 0)]
    [TestCase(BmsLayoutVariant.Pms9K, 0)]
    public void EmptyPoorComboRuleAndRewind(BmsLayoutVariant layout, int expectedCombo)
    {
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(new BmsBeatmap { LayoutVariant = layout });
        processor.Combo.Value = 3;
        processor.RegisterEmptyPoor(1000, 1100, 1);
        Assert.That(processor.Combo.Value, Is.EqualTo(expectedCombo));
        processor.RewindEmptyPoors(900);
        Assert.That(processor.Combo.Value, Is.EqualTo(3));
    }

    [Test]
    public void PmsBadDoesNotConsumeScoringJudgement()
    {
        var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Pms9K });
        processor.Combo.Value = 3;
        processor.RegisterNonConsumingJudgement(HitResult.Ok, 850, 1000, 1);
        Assert.That(processor.Combo.Value, Is.Zero);
        Assert.That(processor.ScoringJudgementEventCount, Is.Zero);
        Assert.That(processor.JudgementEvents[0].Source.IsScoring, Is.False);
        processor.RewindEmptyPoors(800);
        Assert.That(processor.Combo.Value, Is.EqualTo(3));
        Assert.That(processor.JudgementEvents, Is.Empty);
    }

    [Test]
    public void SuppressedPmsPoorStillCountsForAccuracyAndRevertsWithoutPenalty()
    {
        var first = new BmsNote();
        var missed = new BmsNote { StartTime = 1000 };
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Pms9K, HitObjects = { first, missed } };
        var score = new BmsScoreProcessor();
        var health = new BmsHealthProcessor();
        score.ApplyBeatmap(beatmap);
        health.ApplyBeatmap(beatmap);
        score.ApplyResult(new JudgementResult(first, first.CreateJudgement()) { Type = HitResult.Perfect });
        var result = new BmsJudgementResult(missed, missed.CreateJudgement()) { Type = HitResult.Meh, SuppressPenalty = true };
        var oldHealth = health.Health.Value;
        score.ApplyResult(result);
        health.ApplyResult(result);
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
        score.ApplyResult(result);
        Assert.That(score.Accuracy.Value, Is.EqualTo(0.5));
        Assert.That(score.Combo.Value, Is.EqualTo(1));
        Assert.That(score.JudgementEvents.Count(e => e.SuppressPenalty), Is.EqualTo(1));
    }
}
