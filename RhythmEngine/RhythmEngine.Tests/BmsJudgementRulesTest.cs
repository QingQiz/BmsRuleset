using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Tests;

[TestFixture]
public class BmsJudgementRulesTest
{
    [TestCase(BmsJudgementMode.FiveKey, false, false, .75, 75_000, "good")]
    [TestCase(BmsJudgementMode.FiveKey, true, true, .75, 120_000, "great")]
    [TestCase(BmsJudgementMode.FiveKey, true, true, .75, 120_001, "bad")]
    [TestCase(BmsJudgementMode.SevenKey, false, false, 1.3580245, 27_160, "pgreat")]
    [TestCase(BmsJudgementMode.SevenKey, false, false, 1.3580245, 27_161, "great")]
    [TestCase(BmsJudgementMode.Pms, false, false, 0, 20_000, "pgreat")]
    [TestCase(BmsJudgementMode.Pms, false, false, 0, 20_001, "bad")]
    [TestCase(BmsJudgementMode.Pms, false, true, 1.33, 283_000, "good")]
    [TestCase(BmsJudgementMode.Keyboard, false, true, .75, -18_750, "pgreat")]
    [TestCase(BmsJudgementMode.Keyboard, false, true, .75, -18_751, "great")]
    public void ModeSpecificBoundaries(BmsJudgementMode mode, bool directional, bool tail, double rate, long offset, string expected)
    {
        var windows = BmsJudgementRules.For(mode).GetProfile(rate).GetTiming(directional, tail).Windows;
        Assert.That(windows.ResultFor(offset)?.Value, Is.EqualTo(expected));
    }

    [TestCase(BmsJudgementMode.FiveKey, .75, true)]
    [TestCase(BmsJudgementMode.SevenKey, .75, false)]
    [TestCase(BmsJudgementMode.Pms, .70, true)]
    [TestCase(BmsJudgementMode.Keyboard, .75, false)]
    public void RankAndComboPoliciesShareTheSelectedMode(BmsJudgementMode mode, double normalRate, bool breaksCombo)
    {
        var rules = BmsJudgementRules.For(mode);
        Assert.That(rules.ResolveRate(2, null), Is.EqualTo(normalRate));
        Assert.That(rules.ResolveRate(4, 0), Is.Zero);
        Assert.That(rules.ResolveRate(4, 134.25), Is.EqualTo(normalRate * 1.3425).Within(1e-12));
        Assert.That(rules.EmptyPoorBreaksCombo, Is.EqualTo(breaksCombo));
        var lr2 = BmsJudgementRules.For(mode, BmsJudgementModifiers.Lr2oraja);
        Assert.That(lr2.ResolveRate(4, null), Is.EqualTo(.75));
        Assert.That(lr2.EmptyPoorBreaksCombo, Is.False);
    }

    [TestCase(BmsJudgementModifiers.NoBad)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGood)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGreat)]
    public void DisabledResultsDoNotChangeDeadlinesOrOtherSessions(BmsJudgementModifiers modifiers)
    {
        var normalRules = BmsJudgementRules.For(BmsJudgementMode.SevenKey);
        var modifiedRules = BmsJudgementRules.For(BmsJudgementMode.SevenKey, modifiers);
        var normal = session(normalRules, KeyNoteKind.Tap);
        var modified = session(modifiedRules, KeyNoteKind.Tap);
        Assert.That(results(normal.AdvanceTo(1_180_000, [new InputEvent(1_180_000, 0, true)])), Is.EqualTo(new[] { BmsResults.BAD }));
        Assert.That(results(modified.AdvanceTo(1_180_000, [new InputEvent(1_180_000, 0, true)])), Is.Empty);
        var clone = modified.Clone();
        Assert.That(results(modified.AdvanceTo(1_210_000)), Is.Empty);
        Assert.That(results(modified.AdvanceTo(1_210_001)), Is.EqualTo(new[] { BmsResults.POOR }));
        Assert.That(results(clone.AdvanceTo(1_210_001)), Is.EqualTo(new[] { BmsResults.POOR }));
        modified.Reset();
        Assert.That(results(modified.AdvanceTo(1_000_000, [new InputEvent(1_000_000, 0, true)])), Is.EqualTo(new[] { BmsResults.PGREAT }));
        Assert.That(modifiedRules.GetProfile(.75).GetTiming(false, false).WindowFor(BmsResults.BAD), Is.Null);
        Assert.That(normalRules.GetProfile(.75).GetTiming(false, false).WindowFor(BmsResults.BAD), Is.Not.Null);
    }

    [TestCase(BmsJudgementModifiers.NoGood, 500_000)]
    [TestCase(BmsJudgementModifiers.NoGreat, 500_000)]
    [TestCase(BmsJudgementModifiers.NoGood, 10_000)]
    public void Lr2OrajaLongNoteDeadlineSurvivesConstraintsAndShortDurations(BmsJudgementModifiers constraint, long duration)
    {
        var rules = BmsJudgementRules.For(BmsJudgementMode.SevenKey, BmsJudgementModifiers.Lr2oraja | constraint);
        var play = session(rules, KeyNoteKind.Hold, duration);
        var deadline = 1_000_000 + Math.Min(100_000, duration);
        Assert.That(results(play.AdvanceTo(deadline)), Is.Empty);
        var missed = play.AdvanceTo(deadline + 1).Transitions.SelectMany(t => t.Judgements).ToArray();
        Assert.That(missed.Select(j => j.Event.Part), Is.EqualTo(new[] { NoteParts.HEAD, NoteParts.TAIL }));
        Assert.That(missed.Select(j => j.Event.Result), Is.All.EqualTo(BmsResults.POOR));
        Assert.That(play.Completed, Is.True);
    }

    [Test]
    public void EmptyPoorSearchBoundaryIsExclusive()
    {
        foreach (var modifier in new[] { BmsJudgementModifiers.None, BmsJudgementModifiers.Lr2oraja })
        {
            var rules = BmsJudgementRules.For(BmsJudgementMode.SevenKey, modifier);
            var play = session(rules, KeyNoteKind.Tap);
            var boundary = 1_000_000 - rules.SearchWindow;
            Assert.That(results(play.AdvanceTo(boundary, [new InputEvent(boundary, 0, true)])), Is.Empty);
            Assert.That(results(play.AdvanceTo(boundary + 2,
                [new InputEvent(boundary + 1, 0, false), new InputEvent(boundary + 2, 0, true)])), Is.EqualTo(new[] { BmsResults.EMPTY_POOR }));
        }
    }

    [Test]
    public void SuppressedPmsMissConsumesTheWeightedAccuracyPotential()
    {
        var rules = BmsJudgementRules.For(BmsJudgementMode.Pms);
        var note = new KeyNote(0, 0, 1_000_000, 1_000_000);
        var chart = new Chart(1, [new ChartNote(0, 0, note.StartTime, note.EndTime, rules.GetProfile(.7).CreateRule(note))],
            [new ScoringOpportunity(0, NoteParts.TAP, 2, 3)]);
        var play = new JudgementSession(chart, new SessionConfiguration("weighted-pms", BmsProfiles.CreateScore(true),
            new HealthConfiguration(new HealthState(1, false), context => context.Health, _ => SessionOutcome.InProgress),
            context => BmsProfiles.SelectCandidates(context, chart, BmsSelection.Combo)));
        Assert.That(results(play.AdvanceTo(850_000, [new InputEvent(850_000, 0, true)])), Is.EqualTo(new[] { BmsResults.BAD }));
        var missed = play.AdvanceTo(2_000_000).Transitions.SelectMany(t => t.Judgements).Single();
        Assert.That(missed.Event.SuppressPenalty, Is.True);
        Assert.That(play.Score.AccuracyMaximum, Is.EqualTo(chart.ScorePlan.MaximumAccuracy));
        Assert.That(play.Score.ResultCounts.GetValueOrDefault(BmsResults.BAD), Is.EqualTo(1));
        Assert.That(play.Score.ResultCounts.GetValueOrDefault(BmsResults.POOR), Is.Zero);
    }

    private static JudgementSession session(BmsJudgementRules rules, KeyNoteKind kind, long duration = 0)
    {
        var note = new KeyNote(0, 0, 1_000_000, 1_000_000 + duration, kind);
        var chart = new Chart(1, [new ChartNote(note.Id, note.Lane, note.StartTime, note.EndTime, rules.GetProfile(rules.RateForRank(2)).CreateRule(note))],
            BmsProfiles.CreateScoringOpportunities(note.Id, note.Kind));
        return new JudgementSession(chart, new SessionConfiguration("test-bms", BmsProfiles.CreateScore(rules.EmptyPoorBreaksCombo),
            new HealthConfiguration(new HealthState(100, false), context => context.Health, context => context.Completed ? SessionOutcome.Cleared : SessionOutcome.InProgress),
            context => BmsProfiles.SelectCandidates(context, chart, rules.SelectionOverride ?? BmsSelection.Combo)));
    }

    private static ResultId[] results(JudgementBatch batch) => batch.Transitions.SelectMany(t => t.Judgements).Select(j => j.Event.Result).ToArray();
}
