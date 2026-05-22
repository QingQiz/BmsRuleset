using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Screens;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsSettings : OsuTestScene
{
    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    [BackgroundDependencyLoader]
    private void load()
    {
        var ruleset = new BmsRuleset();
        var section = ruleset.CreateSettings();

        Add(new PopoverContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children = [section],
            },
        });
    }

    [Test]
    public void TestShow()
    {
        AddStep("visible", () => { });
    }
}

[TestFixture]
public partial class TestSceneBmsFileImportScreen : ScreenTestScene
{
    private BmsFileImportScreen importScreen = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
    }

    [Test]
    public void TestNavigate()
    {
        AddStep("load screen", () => LoadScreen(importScreen = new BmsFileImportScreen()));
        AddUntilStep("wait for load", () => importScreen.IsLoaded);
    }

    [Test]
    public void TestShow()
    {
        AddStep("load screen", () => LoadScreen(importScreen = new BmsFileImportScreen()));
    }
}
