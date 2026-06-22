using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osuTK.Graphics;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Components;

[TestFixture]
public partial class TestSceneBmsHealthBar : OsuTestScene
{
    private BmsHealthProcessor healthProcessor = null!;
    private BmsBeatmap beatmap = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create display", () =>
        {
            healthProcessor = new BmsHealthProcessor();
            healthProcessor.ApplyBeatmap(beatmap = new BmsBeatmap
            {
                LayoutVariant = BmsLayoutVariant.Bme7K,
                TotalColumns = 8,
                HitObjects =
                {
                    new BmsHitObject { StartTime = 1000, Column = 1 },
                    new BmsHitObject { StartTime = 2000, Column = 2, IsMine = true, LandmineDamagePercent = 25 },
                },
            });

            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies =
                [
                    (typeof(BmsHealthProcessor), healthProcessor),
                    (typeof(HealthProcessor), healthProcessor),
                ],
                Child = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = new BmsHealthDisplay(),
                },
            };
        });
    }

    [Test]
    public void TestClearZone()
    {
        AddStep("set health to 80%", () => healthProcessor.Health.Value = 0.80);
    }

    [Test]
    public void TestDrainAndRecover()
    {
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
        AddStep("drain to 10%", () => healthProcessor.Health.Value = 0.10);
        AddStep("recover to 85%", () => healthProcessor.Health.Value = 0.85);
    }

    [Test]
    public void TestEmpty()
    {
        AddStep("set health to 0%", () => healthProcessor.Health.Value = 0.0);
    }

    [Test]
    public void TestEmptyPoorDrain()
    {
        AddStep("set health to 80%", () => healthProcessor.Health.Value = 0.80);
        AddStep("register empty poor", () => healthProcessor.RegisterEmptyPoor());
        AddUntilStep("health display drained", () => healthProcessor.Health.Value, () => Is.EqualTo(0.78).Within(0.001));
    }

    [Test]
    public void TestFullHealth()
    {
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
    }

    [Test]
    public void TestInitialState()
    {
        AddAssert("health is 20%", () => healthProcessor.Health.Value, () => Is.EqualTo(0.2).Within(0.001));
    }

    [Test]
    public void TestLandmineDrain()
    {
        AddStep("set health to 80%", () => healthProcessor.Health.Value = 0.80);
        AddStep("detonate landmine", () => healthProcessor.ApplyResult(new JudgementResult(beatmap.HitObjects[1], beatmap.HitObjects[1].CreateJudgement())
        {
            Type = HitResult.Meh,
        }));
        AddUntilStep("health display drained", () => healthProcessor.Health.Value, () => Is.EqualTo(0.55).Within(0.001));
    }

    [Test]
    public void TestRedZone()
    {
        AddStep("set health to 10%", () => healthProcessor.Health.Value = 0.10);
    }

    [Test]
    public void TestYellowZone()
    {
        AddStep("set health to 50%", () => healthProcessor.Health.Value = 0.50);
    }

    [Test]
    public void TestHardGaugeDisplay()
    {
        AddStep("set Hard gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.Hard));
        AddAssert("display is Fixed red", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ColourMode == BmsGaugeColourMode.Fixed && dp.FillColour == new Color4(220, 55, 50, 255);
        });
        AddAssert("no clear line", () => !((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ShowClearLine);
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
    }

    [Test]
    public void TestExHardGaugeDisplay()
    {
        AddStep("set EX Hard gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.ExHard));
        AddAssert("display is Fixed purple", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ColourMode == BmsGaugeColourMode.Fixed && dp.FillColour == new Color4(195, 55, 210, 255);
        });
        AddAssert("no clear line", () => !((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ShowClearLine);
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
    }

    [Test]
    public void TestHazardGaugeDisplay()
    {
        AddStep("set Hazard gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.Hazard));
        AddAssert("display is Fixed gold", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ColourMode == BmsGaugeColourMode.Fixed && dp.FillColour == new Color4(255, 215, 0, 255);
        });
        AddAssert("no clear line", () => !((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ShowClearLine);
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
    }

    [Test]
    public void TestEasyGaugeGrooveDisplay()
    {
        AddStep("set Easy gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.Easy));
        AddAssert("display is GrooveDynamic", () =>
            ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ColourMode == BmsGaugeColourMode.GrooveDynamic);
        AddAssert("has clear line at 80%", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ShowClearLine && dp.ClearThreshold == 0.8;
        });
    }

    [Test]
    public void TestAssistEasyGaugeGrooveDisplay()
    {
        AddStep("set Assist Easy gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.AssistEasy));
        AddAssert("display is GrooveDynamic", () =>
            ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ColourMode == BmsGaugeColourMode.GrooveDynamic);
        AddAssert("has clear line at 60%", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ShowClearLine && dp.ClearThreshold == 0.6;
        });
        AddStep("set health above clear", () => healthProcessor.Health.Value = 0.65);
    }

    [Test]
    public void TestHazardGaugeHealthLevels()
    {
        AddStep("set Hazard gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.Hazard));
        AddStep("set health to 50%", () => healthProcessor.Health.Value = 0.50);
        AddAssert("display still Fixed gold", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ColourMode == BmsGaugeColourMode.Fixed && dp.FillColour == new Color4(255, 215, 0, 255);
        });
        AddStep("drop health to 5%", () => healthProcessor.Health.Value = 0.05);
        AddAssert("display colour unchanged (Fixed)", () =>
            ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ColourMode == BmsGaugeColourMode.Fixed);
    }

    [Test]
    public void TestSwitchFromHardToNormalRestoresGroove()
    {
        AddStep("set Hard gauge", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.Hard));
        AddAssert("display is Fixed", () =>
            ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ColourMode == BmsGaugeColourMode.Fixed);
        AddStep("switch back to Normal", () => ((BmsHealthProcessor)healthProcessor).SetGaugeType(BmsGaugeType.Normal));
        AddAssert("display is GrooveDynamic", () =>
            ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value.ColourMode == BmsGaugeColourMode.GrooveDynamic);
        AddAssert("clear line restored at 80%", () =>
        {
            var dp = ((BmsHealthProcessor)healthProcessor).DisplayProfile.Value;
            return dp.ShowClearLine && dp.ClearThreshold == 0.8;
        });
    }
}
