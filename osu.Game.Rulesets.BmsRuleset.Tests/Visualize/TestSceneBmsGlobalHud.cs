using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsGlobalHud : BmsPlayerTestScene
{
    [Cached]
    private readonly SkinEditorOverlay skinEditor = new(new ScalingContainer());

    private ArgonSkin skin = null!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
    {
        skin = new ArgonSkin(this);
        return new BmsTestSkins.SkinnedTestPlayer(new SkinProvidingContainer(skin), null);
    }

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestPlayfieldHudPositionsSurviveLayoutReload()
    {
        SkinnableContainer playfieldHud() => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>()
            .Single(container => container.Lookup.Lookup == GlobalSkinnableContainers.Playfield);
        BmsJudgementDisplay judgementDisplay() => playfieldHud().Components.OfType<BmsJudgementDisplay>().Single();
        BmsComboCounter comboCounter() => playfieldHud().Components.OfType<BmsComboCounter>().Single();
        var judgementPosition = new Vector2(30, 0);
        var comboPosition = new Vector2(-40, -60);

        AddStep("load player", LoadPlayer);
        AddUntilStep("playfield HUD loaded", () => Player.IsLoaded && playfieldHud().ComponentsLoaded);
        AddAssert("default judgement position comes from the embedded skin", () => judgementDisplay().Y,
            () => Is.EqualTo(250 * LegacySkin.STABLE_MAGIC_SCALE_FACTOR));
        AddAssert("default combo position is applied", () => comboCounter().Y, () => Is.EqualTo(300));
        AddStep("move playfield components and save layout", () =>
        {
            var display = judgementDisplay();
            display.Anchor = Anchor.Centre;
            display.Origin = Anchor.Centre;
            display.Position = judgementPosition;

            var combo = comboCounter();
            combo.Anchor = Anchor.BottomRight;
            combo.Origin = Anchor.BottomRight;
            combo.Position = comboPosition;
            skin.UpdateDrawableTarget(playfieldHud());
        });
        AddStep("reload saved layout", () => playfieldHud().Reload());
        AddUntilStep("saved playfield HUD loaded", () => playfieldHud().ComponentsLoaded);
        AddAssert("saved judgement anchor is restored", () => judgementDisplay().Anchor, () => Is.EqualTo(Anchor.Centre));
        AddAssert("saved judgement position is restored", () => judgementDisplay().Position, () => Is.EqualTo(judgementPosition));
        AddAssert("saved combo anchor and origin are restored", () => (comboCounter().Anchor, comboCounter().Origin),
            () => Is.EqualTo((Anchor.BottomRight, Anchor.BottomRight)));
        AddAssert("saved combo position is restored", () => comboCounter().Position, () => Is.EqualTo(comboPosition));
    }

    [Test]
    public void TestBgaUndoRestoresFixedSizeFromFullScreen()
    {
        SkinnableContainer rulesetHud() => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>()
            .Single(container => container.Lookup is { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: not null });
        BmsBgaDisplay display() => rulesetHud().Components.OfType<BmsBgaDisplay>().Single();
        var size = new Vector2(320, 240);
        var position = new Vector2(20, 40);
        SkinEditorChangeHandler changes = null!;

        AddStep("load player", LoadPlayer);
        AddUntilStep("ruleset HUD loaded", () => Player.IsLoaded && rulesetHud().ComponentsLoaded);
        AddStep("set a fixed BGA layout", () =>
        {
            display().FillScreen.Value = false;
            display().Size = size;
            display().Position = position;
            changes = new SkinEditorChangeHandler(rulesetHud());
        });
        AddStep("enable full screen", () =>
        {
            changes.BeginChange();
            display().FillScreen.Value = true;
            changes.EndChange();
        });
        AddStep("undo full screen", () => changes.RestoreState(-1));
        AddAssert("fixed BGA layout restored", () => (display().RelativeSizeAxes, display().Size, display().Position),
            () => Is.EqualTo((Axes.None, size, position)));
        AddStep("redo full screen", () => changes.RestoreState(1));
        AddAssert("full-screen BGA layout restored", () => (display().RelativeSizeAxes, display().Size, display().Position),
            () => Is.EqualTo((Axes.Both, Vector2.One, Vector2.Zero)));
    }

    [Test]
    public void TestGlobalHudSurvivesEditingAndReload()
    {
        SkinnableContainer globalHud() => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>()
            .Single(container => container.Lookup is { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: null });
        Drawable globalContent() => ((Drawable)globalHud().Components.First()).Parent!;
        SerialisedDrawableInfo[] serialiseGlobalHud() => ((ISerialisableDrawableContainer)globalHud()).CreateSerialisedInfo().ToArray();

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("global HUD loaded", () => globalHud().ComponentsLoaded);
        AddStep("stop clock", () => Player.GameplayClockContainer.Stop());
        AddAssert("native components retained", () => globalHud().Components.Count > 0);
        AddAssert("global HUD visible during BMS play", () => globalContent().Alpha == 1);

        AddStep("enter skin editing", () => skinEditor.State.Value = Visibility.Visible);
        AddUntilStep("global HUD visible in editor", () => globalContent().Alpha == 1);
        AddStep("save shared HUD layer", () => skin.UpdateDrawableTarget(globalHud()));
        AddAssert("saved layout contains all native components", () =>
            skin.LayoutInfos[GlobalSkinnableContainers.MainHUDComponents].TryGetDrawableInfo(null, out var components)
            && components.Length == globalHud().Components.Count);

        AddStep("leave skin editing", () => skinEditor.State.Value = Visibility.Hidden);
        AddAssert("global HUD stays visible after editing", () => globalContent().Alpha == 1);
        AddAssert("editing preserves saved component properties", () =>
            Newtonsoft.Json.JsonConvert.SerializeObject(serialiseGlobalHud()) ==
            Newtonsoft.Json.JsonConvert.SerializeObject(skin.LayoutInfos[GlobalSkinnableContainers.MainHUDComponents].DrawableInfo["global"]));

        AddStep("reload saved HUD", () => globalHud().Reload());
        AddUntilStep("saved HUD loaded", () => globalHud().ComponentsLoaded);
        AddAssert("saved HUD remains visible during play", () => globalContent().Alpha == 1);
        AddStep("reopen skin editing", () => skinEditor.State.Value = Visibility.Visible);
        AddUntilStep("saved HUD visible in editor", () => globalContent().Alpha == 1);
        AddAssert("native components survive reload", () => globalHud().Components.Count > 0);
        AddStep("close skin editing", () => skinEditor.State.Value = Visibility.Hidden);
    }
}
