using System.Linq;
using NUnit.Framework;
using osu.Game.Configuration;
using osu.Game.IO.Serialization;
using osu.Game.Online.API;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods;

[TestFixture]
public class BmsModAutoGaugeTest
{

    /// <summary>
    ///     Builds an Auto Gauge whose resolved worst gauge is ExHard: the six-tier chain is
    ///     installed via <see cref="BmsModAutoGauge.ApplyToHealthProcessor" />, then a single
    ///     BAD (Ok) fails Hazard (survival gauge, −1) so the first non-failed survival tier
    ///     (ExHard) becomes the resolved worst once <see cref="BmsHealthProcessor.HasPassedAtEnd" /> runs.
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

    [Test]
    public void TestAutoGaugeAllowsResolvedGaugeAttribution()
    {
        var autoGauge = new BmsModAutoGauge();

        Assert.That(autoGauge.IncompatibleMods, Does.Not.Contain(typeof(BmsModGauge)));
        Assert.That(autoGauge.IncompatibleMods, Does.Not.Contain(typeof(BmsModHardGauge)));
    }

    [Test]
    public void TestAutoGaugeAppendsResolvedGaugeModOnApplyToScore()
    {
        var (autoGauge, hp) = createAutoGaugeResolvedToExHard();
        Assert.That(hp.WorstGaugeType, Is.EqualTo(BmsGaugeType.ExHard));

        var score = new ScoreInfo { Mods = [autoGauge] };

        autoGauge.ApplyToScore(score);

        // Auto Gauge is preserved; the resolved tier mod (ExHard) is appended alongside.
        Assert.That(score.Mods, Has.One.TypeOf<BmsModExHardGauge>());
        Assert.That(score.Mods, Contains.Item(autoGauge));
    }

    [Test]
    public void TestAutoGaugeAttributionDispatchedFromPopulateScore()
    {
        var (autoGauge, _) = createAutoGaugeResolvedToExHard();

        var scoreProcessor = new BmsScoreProcessor();
        scoreProcessor.Mods.Value = new Mod[] { autoGauge };
        var score = new ScoreInfo { Mods = [autoGauge] };

        scoreProcessor.PopulateScore(score);

        Assert.That(score.Mods, Has.One.TypeOf<BmsModExHardGauge>());
    }

    [Test]
    public void TestAutoGaugeAttributionIsIdempotent()
    {
        var (autoGauge, _) = createAutoGaugeResolvedToExHard();
        var score = new ScoreInfo { Mods = [autoGauge] };

        autoGauge.ApplyToScore(score);
        autoGauge.ApplyToScore(score);

        Assert.That(score.Mods.Count(m => m is BmsModExHardGauge), Is.EqualTo(1));
    }

    [Test]
    public void TestAutoGaugeAttributionReplacesEarlierResolvedGauge()
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

        var score = new ScoreInfo { Mods = [autoGauge] };

        autoGauge.ApplyToScore(score);
        Assert.That(score.Mods, Has.One.TypeOf<BmsModHazardGauge>());

        hp.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Ok });
        hp.HasPassedAtEnd();

        autoGauge.ApplyToScore(score);

        Assert.That(score.Mods, Has.None.TypeOf<BmsModHazardGauge>());
        Assert.That(score.Mods, Has.One.TypeOf<BmsModExHardGauge>());
        Assert.That(score.Mods, Contains.Item(autoGauge));
    }

    [Test]
    public void TestAutoGaugeIsAutomationType()
    {
        var mod = new BmsModAutoGauge();

        Assert.That(mod.Type, Is.EqualTo(ModType.Automation));
    }

    [Test]
    public void TestAutoGaugeUsesConfiguredCourseGaugeContext()
    {
        var hp = new BmsHealthProcessor();
        hp.ApplyBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K });
        hp.ConfigureGaugeContext(true, BmsGaugeProfileFamily.FiveKeys);

        new BmsModAutoGauge().ApplyToHealthProcessor(hp);

        Assert.Multiple(() =>
        {
            Assert.That(hp.CurrentGaugeStates.Select(state => state.GaugeType), Is.EqualTo(
            [
                BmsGaugeType.ExHardClass,
                BmsGaugeType.ExClass,
                BmsGaugeType.Class,
            ]));
            Assert.That(hp.GaugeProfile.PerfectGain, Is.EqualTo(0.0001).Within(0.000001));
        });
    }

    [Test]
    public void TestCourseGaugeUsesConfiguredProfileFamily()
    {
        var hp = new BmsHealthProcessor();
        hp.ApplyBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K });
        hp.ConfigureGaugeContext(true, BmsGaugeProfileFamily.FiveKeys);

        new BmsModClassGauge().ApplyToHealthProcessor(hp);

        Assert.That(hp.GaugeProfile.PerfectGain, Is.EqualTo(0.0001).Within(0.000001));
    }

    [Test]
    public void TestCourseStartingStatesSurviveCloneAndModSerialisation()
    {
        var mod = new BmsModAutoGauge
        {
            ClassHealth = { Value = 0.81 },
            ExClassHealth = { Value = 0.42 },
            GaugeProfileFamily = { Value = BmsGaugeProfileFamily.Lr2 },
        };
        var clone = mod.DeepClone();
        var saved = new APIMod(clone).Serialize().Deserialize<APIMod>();
        var restored = (BmsModAutoGauge)saved.ToMod(new BmsRuleset());
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K });
        restored.ApplyToHealthProcessor(health);
        restored.ApplyToScore(new ScoreInfo { Mods = [restored] });
        Assert.Multiple(() =>
        {
            Assert.That(health.IsCourseGaugeMode, Is.True);
            Assert.That(health.ConfiguredProfileFamily, Is.EqualTo(BmsGaugeProfileFamily.Lr2));
            Assert.That(health.CurrentGaugeStates, Is.EqualTo(new[]
            {
                new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.42, false),
                new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.81, false),
            }));
            Assert.That(restored.ExHardClassHealth.Value, Is.Null);
        });

        foreach (var control in restored.CreateSettingsControls())
        {
            Assert.That(control.IsPresent, Is.False);
            control.Dispose();
        }
    }

    [TestCase(BmsGaugeType.Class)]
    [TestCase(BmsGaugeType.ExClass)]
    [TestCase(BmsGaugeType.ExHardClass)]
    public void TestSingleCourseStartingHealthSurvivesModSerialisation(BmsGaugeType type)
    {
        var mod = BmsModGauge.CreateForType(type)!;
        mod.InitialHealth.Value = 0.42;
        mod.GaugeProfileFamily.Value = BmsGaugeProfileFamily.FiveKeys;
        var saved = new APIMod(mod.DeepClone()).Serialize().Deserialize<APIMod>();
        var restored = (BmsModGauge)saved.ToMod(new BmsRuleset());
        using var health = new BmsHealthProcessor();
        health.ApplyBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K });
        restored.ApplyToHealthProcessor(health);
        Assert.Multiple(() =>
        {
            Assert.That(health.IsCourseGaugeMode, Is.True);
            Assert.That(health.ConfiguredProfileFamily, Is.EqualTo(BmsGaugeProfileFamily.FiveKeys));
            Assert.That(health.CurrentGaugeStates.Single(), Is.EqualTo(new BmsGaugeStateSnapshot(type, 0.42, false)));
        });
        foreach (var control in restored.CreateSettingsControls())
        {
            Assert.That(control.IsPresent, Is.False);
            control.Dispose();
        }
    }
}
