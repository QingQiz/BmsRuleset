using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsHealthDisplay : OsuTestScene
{
    private BmsHealthProcessor healthProcessor = null!;
    private BmsHealthDisplay healthDisplay = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create display", () =>
        {
            healthProcessor = new BmsHealthProcessor(0);
            healthProcessor.ApplyBeatmap(new BmsBeatmap());

            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies =
                [
                    (typeof(BmsHealthProcessor), healthProcessor),
                ],
                Child = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = healthDisplay = new BmsHealthDisplay(),
                },
            };
        });
    }

    [Test]
    public void TestInitialState()
    {
        AddAssert("health is 20%", () => healthProcessor.Health.Value, () => Is.EqualTo(0.2).Within(0.001));
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
    public void TestClearZone()
    {
        AddStep("set health to 80%", () => healthProcessor.Health.Value = 0.80);
    }

    [Test]
    public void TestFullHealth()
    {
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
    }

    [Test]
    public void TestEmpty()
    {
        AddStep("set health to 0%", () => healthProcessor.Health.Value = 0.0);
    }

    [Test]
    public void TestDrainAndRecover()
    {
        AddStep("set health to 100%", () => healthProcessor.Health.Value = 1.0);
        AddStep("drain to 10%", () => healthProcessor.Health.Value = 0.10);
        AddStep("recover to 85%", () => healthProcessor.Health.Value = 0.85);
    }
}
