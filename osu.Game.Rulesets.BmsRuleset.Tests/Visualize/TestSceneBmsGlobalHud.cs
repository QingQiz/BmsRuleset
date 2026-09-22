using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

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
