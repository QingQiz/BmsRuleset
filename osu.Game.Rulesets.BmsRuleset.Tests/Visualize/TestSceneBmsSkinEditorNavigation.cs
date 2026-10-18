using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSkinEditorNavigation : OsuGameTestScene
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestGameplayPreviewRetainsBmsSongSelect(bool converted)
    {
        BmsSoloSongSelect songSelect = null!;
        SkinEditorOverlay overlay = null!;
        SkinEditor editor = null!;
        Player player = null!;

        AddStep("select BMS", () => Game.Ruleset.Value = Game.Dependencies.Get<RulesetStore>().AvailableRulesets
            .Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME).Clone());
        AddAssert("skin editor compatibility installed", () => BmsSkinEditorPatcher.IsInstalled);
        PushAndConfirm(() => songSelect = new BmsSoloSongSelect());
        AddStep("track ruleset changes", () => BmsSongSelectEntryPatcher.TrackRulesetChanges(songSelect));
        AddUntilStep("song select ready", () => songSelect.CarouselItemsPresented);
        AddStep("select preview beatmap", () =>
        {
            var native = BmsTestBeatmaps.CreateBeatmap();
            BmsTestBeatmaps.SetupBeatmapInfo(native, Game.Ruleset.Value.Clone());
            IBeatmap beatmap = native;

            if (converted)
            {
                var sourceRuleset = new TestManiaRuleset().RulesetInfo;
                sourceRuleset.OnlineID = 3;
                beatmap = new Beatmap
                {
                    BeatmapInfo = new BeatmapInfo(sourceRuleset, new BeatmapDifficulty { CircleSize = 7 }),
                    HitObjects = [new TestManiaNote { StartTime = 5000 }, new TestManiaNote { StartTime = 30000 }],
                };
            }

            var set = new BeatmapSetInfo();
            set.Beatmaps.Add(beatmap.BeatmapInfo);
            beatmap.BeatmapInfo.BeatmapSet = set;
            Game.Realm.Write(realm => realm.Add(set, update: true));
            beatmap.BeatmapInfo = beatmap.BeatmapInfo.Detach();
            Game.Beatmap.Value = new TestWorkingBeatmap(beatmap, audioManager: Game.Dependencies.Get<AudioManager>());
        });
        AddStep("open skin editor and gameplay preview", () =>
        {
            overlay = Game.ChildrenOfType<SkinEditorOverlay>().Single();
            Assert.That(Game.Beatmap.Value, Is.TypeOf<TestWorkingBeatmap>());
            Assert.That(overlay.IsLoaded, Is.True);
            // This test host doesn't load the official mania assembly or its toolbar samples.
            if (converted)
                Game.ChildrenOfType<ToolbarRulesetSelector>().Single().Current.UnbindBindings();
            overlay.Show();
            Assert.That(overlay.State.Value, Is.EqualTo(Visibility.Visible));
            Assert.That(Game.Ruleset.Value.CreateInstance().GetAutoplayMod(), Is.Not.Null);
            overlay.PresentGameplay();
        });
        AddUntilStep("BMS preview loaded", () =>
        {
            DismissAnyNotifications();
            return Game.ScreenStack.CurrentScreen is Player { IsLoaded: true } preview && preview.ChildrenOfType<BmsDrawableRuleset>().Any();
        });
        AddStep("capture preview", () => player = (Player)Game.ScreenStack.CurrentScreen);
        AddAssert("BMS remains selected", () => Game.Ruleset.Value.ShortName, () => Is.EqualTo(Constant.SHORT_NAME));
        AddAssert("preview returns to BMS song select", () => player.GetParentScreen(), () => Is.SameAs(songSelect));
        AddUntilStep("skin editor ready", () => (editor = Game.ChildrenOfType<SkinEditor>().SingleOrDefault()!)?.IsLoaded == true);
        AddUntilStep("HUD blueprints ready", () => editor.ChildrenOfType<SkinBlueprintContainer>().Any());
        AddStep("update preview target twice in one frame", () =>
        {
            editor.UpdateTargetScreen(player);
            editor.UpdateTargetScreen(player);
        });
        AddUntilStep("HUD blueprints restored", () => editor.ChildrenOfType<SkinBlueprintContainer>().Any());
        AddStep("return to song-select target twice", () =>
        {
            editor.UpdateTargetScreen(songSelect);
            editor.UpdateTargetScreen(songSelect);
        });
        AddUntilStep("song-select blueprints ready", () => editor.ChildrenOfType<SkinBlueprintContainer>().Any());
        Container emptyTarget = null!;
        AddStep("change to a target without skin layers", () => editor.UpdateTargetScreen(emptyTarget = new Container()));
        AddUntilStep("empty-target placeholder ready", () => editor.ChildrenOfType<NonSkinnableScreenPlaceholder>().Any());
        AddStep("restore gameplay target", () => editor.UpdateTargetScreen(player));
        AddUntilStep("gameplay blueprints restored", () => editor.ChildrenOfType<SkinBlueprintContainer>().Any());
        AddStep("dispose empty target", () => emptyTarget.Dispose());
        AddStep("close skin editor", () => overlay.Hide());
    }

    private class TestManiaNote : HitObject, IHasColumn, IHasXPosition
    {
        public int Column { get; set; }
        public float X { get; set; } = 32;
    }

    public class TestManiaRuleset : BmsRuleset
    {
        public override string ShortName => "mania";
    }
}
