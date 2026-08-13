using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Components;

[TestFixture]
public partial class TestSceneBmsRulesetIcon : OsuTestScene
{
    private static readonly float[] preview_sizes = [9, 12, 16, 20, 40];

    private readonly List<BmsRulesetIcon> icons = [];

    [SetUpSteps]
    public void SetUpSteps()
    {
        AddStep("create icon previews", () =>
        {
            icons.Clear();

            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 600,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 20),
                Children =
                [
                    createPreviewRow(new Colour4(24, 20, 32, 255), Colour4.White),
                    createPreviewRow(new Colour4(238, 235, 244, 255), new Colour4(84, 42, 128, 255)),
                ],
            };
        });
    }

    [Test]
    public void TestIconAtCommonSizes()
    {
        AddUntilStep("all icons loaded", () => icons.All(icon => icon.IsLoaded));
        AddAssert("all preview sizes shown", () => icons.Count == preview_sizes.Length * 2);
        AddAssert("outer ring matches standard ruleset weight", () => icons.All(icon =>
        {
            var ring = icon.ChildrenOfType<CircularContainer>().Single(container => container.Name == "Outer ring");

            return Math.Abs(ring.BorderThickness - 4.2f) < 0.001f;
        }));
        AddAssert("five keys use a two-three layout", () => icons.All(icon =>
        {
            var keys = icon.ChildrenOfType<Container>().Where(container => container.Name == "Key").ToArray();
            var rows = keys.GroupBy(key => key.Y).OrderBy(row => row.Key).ToArray();

            return keys.Length == 5
                   && keys.All(key => key.Size == new Vector2(8, 12) && Math.Abs(key.CornerRadius - 1.5) < 0.001f)
                   && rows.Length == 2
                   && rows[0].Count() == 2
                   && rows[1].Count() == 3
                   && rows.All(row => Math.Abs((row.Min(key => key.X) + row.Max(key => key.X) + 8) / 2 - 20) < 0.001f)
                   && Math.Abs(rows[0].Key - 7) < 0.001f
                   && Math.Abs(rows[1].Key - (rows[0].Key + 12) - 0.75f) < 0.001f;
        }));
        AddAssert("content fits requested sizes", () => icons.Select((icon, index) => (icon, index)).All(item =>
        {
            var content = item.icon.ChildrenOfType<Container>().Single(container => container.Size == new Vector2(40));
            var expectedScale = preview_sizes[item.index % preview_sizes.Length] / 40;

            return Math.Abs(content.Scale.X - expectedScale) < 0.001f
                   && Math.Abs(content.Scale.Y - expectedScale) < 0.001f;
        }));
    }

    private Container createPreviewRow(Colour4 backgroundColour, Colour4 iconColour) => new()
    {
        Width = 600,
        Height = 120,
        Masking = true,
        CornerRadius = 12,
        Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = backgroundColour,
            },
            new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(12, 0),
                Children = [.. preview_sizes.Select(size => createPreviewCell(size, iconColour))],
            },
        ],
    };

    private Container createPreviewCell(float size, Colour4 colour)
    {
        var icon = new BmsRulesetIcon
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(size),
            Colour = colour,
        };

        icons.Add(icon);

        return new Container
        {
            Size = new Vector2(90, 100),
            Child = icon,
        };
    }
}
