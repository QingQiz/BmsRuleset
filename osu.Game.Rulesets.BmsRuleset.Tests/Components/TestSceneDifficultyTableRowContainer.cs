using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;
using osu.Game.Tests.Visual;
using osuTK;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Components;

[HeadlessTest]
public partial class TestSceneDifficultyTableRowContainer : OsuTestScene
{
    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    [TestCase(false, 0, 1)]
    [TestCase(true, 0, 1)]
    [TestCase(false, 16000.5f, 1.1f)]
    [TestCase(false, 24000.3f, 1.25f)]
    public void TestExpandAfterCollapse(bool belowViewport, float scrollOffset, float scale)
    {
        var clock = new ManualClock();
        Container viewport = null!;
        DifficultyTableRowContainer row = null!;
        OsuClickableContainer header = null!;

        AddStep("create table row", () =>
        {
            Clear();
            Add(viewport = new Container
            {
                Position = new Vector2(50.3f, 100.7f),
                Size = new Vector2(400, 100),
                Scale = new Vector2(scale),
                Masking = true,
                Clock = new FramedClock(clock),
                // Preserve the large local coordinates of a long settings page after scrolling it into view.
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Y = -scrollOffset,
                    Child = row = new DifficultyTableRowContainer(new DT
                    {
                        Name = "Test table",
                        Symbol = "T",
                        Source = TableSource.RemoteUrl,
                    }, null, false, false, _ => { })
                    {
                        Y = scrollOffset,
                    },
                },
            });
            header = row.ChildrenOfType<OsuClickableContainer>()
                .Single(drawable => drawable.Name.StartsWith("Difficulty table header", StringComparison.Ordinal));
        });
        AddStep("finish initial collapse", () => clock.CurrentTime += 1000);
        AddUntilStep("row is collapsed", () => row.Height, () => Is.EqualTo(header.Height).Within(0.01));
        AddStep("position row in viewport", () => row.Y = scrollOffset + (belowViewport ? viewport.Height - header.Height / 2 : 0));
        AddWaitStep("update masking", 2);

        for (var i = 0; i < 2; i++)
        {
            AddStep("expand table", () => header.TriggerClickWithSound());
            // A click can share its timestamp with the first layout update, before the content starts fading in.
            AddWaitStep("update at click timestamp", 2);
            AddStep("advance expansion animation", () => clock.CurrentTime += 1000);
            AddStep("finish expansion autosize", () => clock.CurrentTime += 1000);
            AddAssert("chevron shows expanded state", () => header.ChildrenOfType<SpriteIcon>().Single().Rotation,
                () => Is.EqualTo(90).Within(0.01));
            AddUntilStep("row expands", () => row.Height, () => Is.GreaterThan(header.Height + 40));
            AddStep("collapse table", () => header.TriggerClickWithSound());
            AddStep("advance collapse animation", () => clock.CurrentTime += 1000);
            AddUntilStep("row collapses again", () => row.Height, () => Is.EqualTo(header.Height).Within(0.01));
        }

        AddStep("move row into view", () => row.Y = scrollOffset);
        AddStep("expand visible table", () => header.TriggerClickWithSound());
        AddStep("advance visible expansion", () => clock.CurrentTime += 1000);
        AddStep("finish visible autosize", () => clock.CurrentTime += 1000);
        AddUntilStep("visible row expands", () => row.Height, () => Is.GreaterThan(header.Height + 40));
        AddUntilStep("buttons are visible", () => row.ChildrenOfType<RoundedButton>()
            .All(button => button.DrawColourInfo.Colour.MaxAlpha == 1));
    }
}
