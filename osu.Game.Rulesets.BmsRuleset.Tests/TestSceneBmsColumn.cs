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
public partial class TestSceneBmsColumn : OsuTestScene
{
    private BmsColumn keyColumn = null!;
    private BmsColumn scratchColumn = null!;

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create columns", () =>
        {
            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Height = 0.85f,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(20, 0),
                Children =
                [
                    scratchColumn = new BmsColumn(0, BmsLayoutVariant.Bme7K),
                    keyColumn = new BmsColumn(1, BmsLayoutVariant.Bme7K),
                ],
            };
        });
    }

    [Test]
    public void TestColumnWidthsAndAreas()
    {
        AddAssert("scratch column detected", () => scratchColumn.IsScratch);
        AddAssert("key column detected", () => !keyColumn.IsScratch);
        AddAssert("scratch width", () => scratchColumn.DrawWidth, () => Is.EqualTo(BmsColumn.SCRATCH_COLUMN_WIDTH).Within(0.5));
        AddAssert("key width", () => keyColumn.DrawWidth, () => Is.EqualTo(BmsColumn.COLUMN_WIDTH).Within(0.5));
        AddAssert("hit object area loaded", () => keyColumn.HitObjectArea.IsLoaded);
        AddAssert("hit explosion area loaded", () => keyColumn.HitExplosionArea.IsLoaded);
    }
}
