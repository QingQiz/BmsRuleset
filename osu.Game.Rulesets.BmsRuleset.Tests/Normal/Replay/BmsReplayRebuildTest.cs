using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsReplayRebuildTest
{
    [Test]
    public void ScoreResetClearsFailureAndTheUnmodifiedScore()
    {
        var note = new BmsNote { StartTime = 1000, Column = 1 };
        using var processor = new BmsScoreProcessor();
        processor.ApplyBeatmap(new BmsBeatmap { HitObjects = { note } });
        processor.ApplyResult(new BmsJudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        processor.FailScore(new ScoreInfo());
        Assert.That(processor.Rank.Value, Is.EqualTo(ScoreRank.F));
        Assert.That(processor.TotalScoreWithoutMods.Value, Is.GreaterThan(0));

        processor.ResetReplayState();
        Assert.Multiple(() =>
        {
            Assert.That(processor.Rank.Value, Is.EqualTo(ScoreRank.X));
            Assert.That(processor.TotalScore.Value, Is.Zero);
            Assert.That(processor.TotalScoreWithoutMods.Value, Is.Zero);
            Assert.That(processor.Accuracy.Value, Is.EqualTo(1));
            Assert.That(processor.Combo.Value, Is.Zero);
            Assert.That(processor.HasCompleted.Value, Is.False);
            Assert.That(processor.JudgementEvents, Is.Empty);
        });
    }

    [Test]
    public void GaugeResetPreservesTheOriginalSeed([Values] bool course, [Values] bool explicitSeed)
    {
        using var processor = new BmsHealthProcessor();
        processor.ConfigureGaugeContext(course, BmsGaugeProfileFamily.Lr2);
        processor.SetGaugeTypes([BmsGaugeType.Hard], profileFamilyOverride: BmsGaugeProfileFamily.Lr2,
            initialStates: explicitSeed ? [new BmsGaugeStateSnapshot(BmsGaugeType.Hard, 0.43, false)] : null);
        if (!explicitSeed)
            processor.Health.Value = 0.43;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            processor.ApplyHellChargeTick(false, eventTime: 1000);
            processor.ResetReplayState();
            Assert.Multiple(() =>
            {
                Assert.That(processor.Health.Value, Is.EqualTo(0.43));
                Assert.That(processor.CurrentGaugeStates.Single().Health, Is.EqualTo(0.43));
                Assert.That(processor.GaugeHistory, Has.Count.EqualTo(explicitSeed ? 1 : 0));
                Assert.That(processor.HasFailed, Is.False);
                Assert.That(processor.HasEverFailed, Is.False);
            });
        }
    }

    [Test]
    public void SilentRebuildPreservesFailurePolicyAndRestoresNotifications([Values] bool allowFailure)
    {
        using var score = new BmsScoreProcessor();
        using var health = new BmsHealthProcessor();
        health.SetGaugeType(BmsGaugeType.Hazard);
        var failures = 0;
        var judgements = 0;
        var nonConsuming = 0;
        health.Failed += () => { failures++; return true; };
        health.NewJudgement += _ => judgements++;
        score.NewJudgement += _ => judgements++;
        score.NonConsumingJudgementRegistered += _ => nonConsuming++;
        var note = new BmsNote { StartTime = 1000, Column = 1 };
        using (new BmsReplayRebuildScope(score, health, () => allowFailure))
        {
            var result = new BmsJudgementResult(note, note.CreateJudgement()) { Type = HitResult.Ok };
            health.ApplyResult(result);
            score.ApplyResult(result);
            score.RegisterEmptyPoor(1001);
            Assert.That(health.HasFailed, Is.EqualTo(allowFailure));
            Assert.That(health.HasEverFailed, Is.True);
        }

        Assert.That(failures + judgements + nonConsuming, Is.Zero);
        health.ResetReplayState();
        health.TriggerFailure();
        score.ApplyResult(new BmsJudgementResult(note, note.CreateJudgement()) { Type = HitResult.Perfect });
        score.RegisterEmptyPoor(2000);
        Assert.Multiple(() =>
        {
            Assert.That(failures, Is.EqualTo(1));
            Assert.That(judgements, Is.EqualTo(1));
            Assert.That(nonConsuming, Is.EqualTo(1));
        });
    }
}
