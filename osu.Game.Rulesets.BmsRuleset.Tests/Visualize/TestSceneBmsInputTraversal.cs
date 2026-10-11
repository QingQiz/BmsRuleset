#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsInputTraversal : BmsPlayerTestScene
{
    private BmsTestSkins.SkinKind skin;
    private BmsInputManager input = null!;
    private IEnumerable<IKeyBinding>? originalBindings;
    private readonly FieldInfo bindings = typeof(KeyBindingContainer).GetField("KeyBindings", BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null, skin);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            HitObjects = [new BmsNote { StartTime = 10000, Column = 1 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void RealPlayerPositionalQueuesDependOnCursorAndBindings(BmsTestSkins.SkinKind selectedSkin)
    {
        AddStep("load manual input player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("manual player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("freeze before note", () =>
        {
            input = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(manager => manager.ChildrenOfType<BmsColumn>().Any());
            Player.GameplayClockContainer.Seek(4800);
            freezeGameplayClock();
        });
        AddUntilStep("manual simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddStep("compare inside and outside positional queues", () =>
        {
            Assert.That(input.UseParentInput, Is.True);
            Assert.That(input.ReplayInputHandler, Is.Null);
            var keyBindings = input.KeyBindingContainer;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var mouse = typeof(CompositeDrawable).GetMethod("BuildPositionalInputQueue", flags)!
                .CreateDelegate<Func<CompositeDrawable, Vector2, List<Drawable>, bool>>();
            var inside = keyBindings.ScreenSpaceDrawQuad.Centre;
            var outside = keyBindings.ScreenSpaceDrawQuad.TopLeft - new Vector2(10000);
            Assert.That(keyBindings.ReceivePositionalInputAt(inside), Is.True);
            Assert.That(keyBindings.ReceivePositionalInputAt(outside), Is.False);

            Vector2[] positions = [inside, outside, Playfield.Stage.ScreenSpaceDrawQuad.Centre];
            foreach (var position in positions)
            {
                var actual = new List<Drawable>();
                mouse(keyBindings, position, actual);
                Assert.That(actual.Contains(keyBindings), Is.EqualTo(keyBindings.ReceivePositionalInputAt(position)));
            }
        });
        AddStep("install positional gameplay bindings", () =>
        {
            // Test-only bindings exercise the framework's actual positional dispatch without
            // changing the user's database or production mapping reload path.
            originalBindings = (IEnumerable<IKeyBinding>)bindings.GetValue(input.KeyBindingContainer)!;
            IKeyBinding[] positional =
            [
                new KeyBinding(InputKey.MouseLeft, BmsAction.Key1),
                new KeyBinding(InputKey.MouseWheelUp, BmsAction.Key2),
            ];
            bindings.SetValue(input.KeyBindingContainer, originalBindings.Concat(positional).ToArray());
            InputManager.MoveMouseTo(input.KeyBindingContainer.ScreenSpaceDrawQuad.Centre);
        });
        AddStep("press mouse gameplay binding", () => InputManager.PressButton(MouseButton.Left));
        AddAssert("mouse binding presses column", () => ((BmsColumn)Playfield.Stage.Columns[1]).IsPressed);
        AddStep("release mouse gameplay binding", () => InputManager.ReleaseButton(MouseButton.Left));
        AddAssert("mouse binding releases column", () => !((BmsColumn)Playfield.Stage.Columns[1]).IsPressed);
        var wheelPresses = 0;
        var wheelReleases = 0;
        WheelActionObserver? observer = null;
        AddStep("attach wheel action observer", () => input.KeyBindingContainer.Add(observer = new WheelActionObserver(
            () => wheelPresses++, () => wheelReleases++)));
        AddUntilStep("wheel observer loaded", () => observer?.IsLoaded == true);
        AddStep("scroll gameplay binding", () => InputManager.ScrollVerticalBy(1));
        AddAssert("wheel emits one press and release", () => wheelPresses == 1 && wheelReleases == 1);
        AddAssert("wheel leaves column released", () => !((BmsColumn)Playfield.Stage.Columns[2]).IsPressed);
        AddStep("restore original mappings", restoreBindings);
    }

    private void freezeGameplayClock()
        => typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(Player.GameplayClockContainer, null);

    private void restoreBindings()
    {
        if (originalBindings == null) return;
        bindings.SetValue(input.KeyBindingContainer, originalBindings);
        originalBindings = null;
    }

    [TearDown]
    public void RestoreInputTraversalBindings() => restoreBindings();

    private sealed partial class WheelActionObserver(Action pressed, Action released) : Component, IKeyBindingHandler<BmsAction>
    {
        public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
        {
            if (e.Action == BmsAction.Key2) pressed();
            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
        {
            if (e.Action == BmsAction.Key2) released();
        }
    }
}
