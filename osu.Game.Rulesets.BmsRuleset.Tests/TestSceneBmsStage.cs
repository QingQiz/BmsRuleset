using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsStage : OsuTestScene
{
    private BmsStage singleStage = null!;
    private BmsStage doubleStage = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create stages", () =>
        {
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(40, 0),
                Children =
                [
                    singleStage = new BmsStage(8, BmsLayoutVariant.Bme7K),
                    doubleStage = new BmsStage(16, BmsLayoutVariant.Bme7KDouble),
                ],
            };
        });
    }

    [Test]
    public void TestStageCreatesColumnsAndCentresKeys()
    {
        AddAssert("single column count", () => singleStage.Columns.Length, () => Is.EqualTo(8));
        AddAssert("double column count", () => doubleStage.Columns.Length, () => Is.EqualTo(16));
        AddAssert("single has scratch", () => singleStage.Columns.Any(c => c.IsScratch));
        AddAssert("double has two scratches", () => doubleStage.Columns.Count(c => c.IsScratch), () => Is.EqualTo(2));
        AddAssert("judgement area loaded", () => singleStage.JudgementArea.IsLoaded);
    }
}
