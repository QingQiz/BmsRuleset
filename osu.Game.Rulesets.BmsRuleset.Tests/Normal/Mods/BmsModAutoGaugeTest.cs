using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModAutoGaugeTest
{
    [Test]
    public void TestAutoGaugeIncompatibleWithHardGauge()
    {
        var autoGauge = new BmsModAutoGauge();
        var hardGauge = new BmsModHardGauge();

        Assert.That(autoGauge.IncompatibleMods, Does.Contain(typeof(BmsModHardGauge)));
    }

    [Test]
    public void TestAutoGaugeIncompatibleWithBmsModGauge()
    {
        var autoGauge = new BmsModAutoGauge();

        Assert.That(autoGauge.IncompatibleMods, Does.Contain(typeof(BmsModGauge)));
    }

    [Test]
    public void TestAutoGaugeIsAutomationType()
    {
        var mod = new BmsModAutoGauge();

        Assert.That(mod.Type, Is.EqualTo(ModType.Automation));
    }

    [Test]
    public void TestAutoGaugeAppendsResolvedGaugeModOnApplyToScore()
    {
        var (autoGauge, hp) = createAutoGaugeResolvedToExHard();
        Assert.That(hp.WorstGaugeType, Is.EqualTo(BmsGaugeType.ExHard));

        var score = new ScoreInfo { Mods = new Mod[] { autoGauge } };

        autoGauge.ApplyToScore(score);

        // Auto Gauge is preserved; the resolved tier mod (ExHard) is appended alongside.
        Assert.That(score.Mods, Has.One.TypeOf<BmsModExHardGauge>());
        Assert.That(score.Mods, Contains.Item(autoGauge));
    }

    [Test]
    public void TestAutoGaugeAttributionIsIdempotent()
    {
        var (autoGauge, _) = createAutoGaugeResolvedToExHard();
        var score = new ScoreInfo { Mods = new Mod[] { autoGauge } };

        autoGauge.ApplyToScore(score);
        autoGauge.ApplyToScore(score);

        Assert.That(score.Mods.Count(m => m is BmsModExHardGauge), Is.EqualTo(1));
    }

    [Test]
    public void TestAutoGaugeAttributionDispatchedFromPopulateScore()
    {
        var (autoGauge, _) = createAutoGaugeResolvedToExHard();

        var scoreProcessor = new BmsScoreProcessor();
        scoreProcessor.Mods.Value = new Mod[] { autoGauge };
        var score = new ScoreInfo { Mods = new Mod[] { autoGauge } };

        scoreProcessor.PopulateScore(score);

        Assert.That(score.Mods, Has.One.TypeOf<BmsModExHardGauge>());
    }

    /// <summary>
    /// Builds an Auto Gauge whose resolved worst gauge is ExHard: the six-tier chain is
    /// installed via <see cref="BmsModAutoGauge.ApplyToHealthProcessor"/>, then a single
    /// BAD (Ok) fails Hazard (survival gauge, −1) so the first non-failed survival tier
    /// (ExHard) becomes the resolved worst once <see cref="BmsHealthProcessor.HasPassedAtEnd"/> runs.
    /// </summary>
    private static (BmsModAutoGauge autoGauge, BmsHealthProcessor hp) createAutoGaugeResolvedToExHard()
    {
        var hp = new BmsHealthProcessor();
        var autoGauge = new BmsModAutoGauge();
        autoGauge.ApplyToHealthProcessor(hp);

        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        hp.ApplyBeatmap(beatmap);

        hp.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Ok });
        hp.HasPassedAtEnd();

        return (autoGauge, hp);
    }
}
