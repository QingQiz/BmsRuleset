using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Components;

[TestFixture]
public partial class TestSceneBmsModIcons : OsuTestScene
{
    private readonly List<ModIcon> icons = [];

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create mod icon previews", () =>
        {
            icons.Clear();

            Child = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Children =
                [
                    new BmsRulesetIcon { Alpha = 0 },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(20, 0),
                        Children =
                        [
                            createIcon(new BmsModNoGood()),
                            createIcon(new BmsModNoGreat()),
                            createIcon(new BmsModHideScratch()),
                            createIcon(new BmsModAutoScratch()),
                            createIcon(new BmsModAutoGauge()),
                        ],
                    },
                ],
            };
        });
    }

    [Test]
    public void TestCustomModGlyphsLoad()
    {
        AddUntilStep("all mod icons loaded", () => icons.All(icon => icon.IsLoaded));
        AddAssert("custom glyphs are selected", () => icons.All(icon =>
            icon.ChildrenOfType<SpriteIcon>().Any(sprite => sprite.Icon.FontName == "bmsIcons")));
    }

    private ModIcon createIcon(Mod mod)
    {
        var icon = new ModIcon(mod, showTooltip: false, showExtendedInformation: false);
        icons.Add(icon);
        return icon;
    }
}
