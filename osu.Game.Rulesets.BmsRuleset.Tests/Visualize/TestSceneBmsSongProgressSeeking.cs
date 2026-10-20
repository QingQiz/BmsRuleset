#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsSongProgressSeeking : BmsPlayerTestScene
{
    private bool replay = true;
    private int seekCount;

    private BmsSongProgress progress => Player.HUDOverlay.ChildrenOfType<BmsSongProgress>().Single();
    private Container marker => progress.Children.OfType<Container>().Single();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(replay ? _ => [new BmsReplayFrame(0), new BmsReplayFrame(20000)] : null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            HitObjects =
            [
                new BmsNote { StartTime = 1000, Column = 1 },
                new BmsLongNote { StartTime = 9000, Duration = 2000, Column = 2 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(0)]
    [TestCase(0.5)]
    [TestCase(1)]
    public void TestClickSeeksToPlayableTime(double position)
    {
        loadPlayer();
        AddAssert("replay enables seeking", () => progress.Interactive.Value);
        AddStep("click beside narrow indicator", () =>
        {
            InputManager.MoveMouseTo(positionAt(position, 8));
            InputManager.Click(MouseButton.Left);
        });
        AddAssert("click seeks once", () => seekCount == 1);
        AddAssert("seek includes first note offset and long note tail", () => atTime(position));
    }

    [TestCase(0.75, 0)]
    [TestCase(0.75, 30)]
    [TestCase(-0.5, 30)]
    [TestCase(1.5, 30)]
    public void TestDragOnlySeeksOnRelease(double destination, float xOffset)
    {
        loadPlayer();
        AddStep("press indicator", () =>
        {
            InputManager.MoveMouseTo(positionAt(0.25));
            InputManager.PressButton(MouseButton.Left);
        });
        AddAssert("press does not seek", () => seekCount == 0);
        for (var i = 1; i <= 8; i++)
        {
            var position = 0.25 + (destination - 0.25) * i / 8;
            AddStep("drag to next position", () => InputManager.MoveMouseTo(positionAt(position, xOffset)));
        }
        AddAssert("drag previews clamped destination", () =>
            Math.Abs(marker.Y - Math.Clamp(destination, 0, 1) * (progress.DrawHeight - marker.DrawHeight)) < 0.01);
        AddAssert("drag has not sought", () => seekCount == 0 && atTime(0));
        AddStep("release mouse", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("release seeks once", () => seekCount == 1);
        AddAssert("release seeks to clamped destination", () => atTime(destination));
    }

    [Test]
    public void TestRightClickDoesNotSeek()
    {
        loadPlayer();
        AddStep("right click and drag", () =>
        {
            InputManager.MoveMouseTo(positionAt(0.25));
            InputManager.PressButton(MouseButton.Right);
            InputManager.MoveMouseTo(positionAt(0.75));
            InputManager.ReleaseButton(MouseButton.Right);
        });
        AddAssert("no seek requested", () => seekCount == 0 && atTime(0));
    }

    [Test]
    public void TestManualPlayDoesNotSeek()
    {
        loadPlayer(withReplay: false);
        AddAssert("manual play disables seeking", () => !progress.Interactive.Value);
        AddStep("click and drag during manual play", () =>
        {
            InputManager.MoveMouseTo(positionAt(0.25));
            InputManager.Click(MouseButton.Left);
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(positionAt(0.75));
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("no seek requested", () => seekCount == 0 && atTime(0));
    }

    [Test]
    public void TestDisablingInteractionCancelsDrag()
    {
        loadPlayer();
        AddStep("begin drag", () =>
        {
            InputManager.MoveMouseTo(positionAt(0.25));
            InputManager.PressButton(MouseButton.Left);
            InputManager.MoveMouseTo(positionAt(0.75));
        });
        AddAssert("drag started", () => progress.IsDragged);
        AddStep("disable interaction", () => progress.Interactive.Value = false);
        AddStep("re-enable interaction", () => progress.Interactive.Value = true);
        AddStep("finish cancelled drag", () =>
        {
            InputManager.MoveMouseTo(positionAt(0.5));
            InputManager.ReleaseButton(MouseButton.Left);
        });
        AddAssert("cancelled drag does not seek", () => seekCount == 0 && atTime(0));
        AddUntilStep("marker returns to current time", () => marker.Y == 0);
    }

    private void loadPlayer(bool withReplay = true)
    {
        AddStep("load player", () =>
        {
            replay = withReplay;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("progress loaded", () => progress.IsLoaded);
        AddStep("freeze at first note and count seeks", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.Seek(1000);
            seekCount = 0;
            Player.GameplayClockContainer.OnSeek += () => seekCount++;
        });
    }

    private Vector2 positionAt(double position, float xOffset = 0)
        => progress.ToScreenSpace(new Vector2(progress.DrawWidth / 2 + xOffset,
            marker.DrawHeight / 2 + (float)position * (progress.DrawHeight - marker.DrawHeight)));

    private bool atTime(double position)
        => Math.Abs(Player.GameplayClockContainer.CurrentTime - (1000 + Math.Clamp(position, 0, 1) * 10000)) < 1;
}
