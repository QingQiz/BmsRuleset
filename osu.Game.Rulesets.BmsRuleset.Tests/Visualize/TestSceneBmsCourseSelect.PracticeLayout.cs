using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    private float? previousPracticeUiScale;
    private ManualResetEventSlim? practiceHudLoadGate;

    [TearDown]
    public void RestorePracticeLayoutPreference()
    {
        practiceHudLoadGate?.Set();
        if (previousPracticeUiScale is not { } scale)
            return;
        Schedule(() => config.SetValue(OsuSetting.UIScale, scale));
        previousPracticeUiScale = null;
    }

    [TestCase(BmsTestSkins.SkinKind.Argon, 1f, false)]
    [TestCase(BmsTestSkins.SkinKind.Argon, 1.4f, false)]
    [TestCase(BmsTestSkins.SkinKind.Classic, 1f, false)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, 1f, false)]
    [TestCase(null, 1f, false)]
    [TestCase(BmsTestSkins.SkinKind.Argon, 1f, true)]
    public void TestPracticeRepeatPreservesHudLayout(BmsTestSkins.SkinKind? skin, float uiScale, bool delayHud)
    {
        BmsPracticeSession session = null!;
        LayoutPracticeScreen practice = null!;
        PracticeIsolationPlayer first = null!;
        RealPracticeRoot root = null!;
        PracticeTransitionMonitor monitor = null!;
        Dictionary<string, Quad> expected = null!;
        var skinsCreated = 0;
        var hudLoadEntered = new ManualResetEventSlim();
        string? layoutFailure = null;

        AddStep("prepare skinned practice", () =>
        {
            previousPracticeUiScale = config.Get<float>(OsuSetting.UIScale);
            config.SetValue(OsuSetting.UIScale, uiScale);
            practiceHudLoadGate = delayHud ? new ManualResetEventSlim() : null;
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 1100, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 3000, Column = 1, Beatmap = beatmap },
            ];
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 1000, EndTime = 2000, SkipResults = true, CountIn = 5 }, []);
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root entered", () => Stack.CurrentScreen == root);
        AddStep("open practice", () => Stack.Push(practice = new LayoutPracticeScreen(session, () =>
        {
            if (delayHud)
                return new SkinProvidingContainer(new GatedPracticeSkin(new BmsBuiltInSkinTransformer(new ArgonSkin(Dependencies.Get<SkinManager>())),
                    skinsCreated++ > 0 ? practiceHudLoadGate : null, hudLoadEntered));
            return skin is { } kind ? BmsTestSkins.CreateSkinSource(kind, Dependencies.Get<SkinManager>()) : Dependencies.Get<SkinManager>();
        })));
        AddUntilStep("first count-in ready", () => practice.Countdown.IsReady);
        AddStep("start first attempt", () =>
        {
            first = practice.Players.Single();
            practice.SkipCountdown();
        });
        AddUntilStep("skin components loaded", () => first.ChildrenOfType<SkinnableContainer>().All(container => container.ComponentsLoaded));
        AddWaitStep("settle first layout", 5);
        AddStep("monitor actual HUD bounds and repeat", () =>
        {
            expected = practiceHudLayout(first);
            Assert.That(expected.Count, Is.GreaterThan(3));
            monitor = monitorPracticeTransitions();
            monitor.ObserveFrame = () =>
            {
                if (Stack.CurrentScreen is not BmsPracticePlayer { IsLoaded: true, Alpha: 1 } player)
                    return;
                var actual = practiceHudLayout(player);
                if (!actual.Keys.Order().SequenceEqual(expected.Keys.Order()))
                {
                    layoutFailure ??= "A visible repeat was missing HUD components.";
                    return;
                }
                foreach (var (name, quad) in expected)
                {
                    if ((actual[name].TopLeft - quad.TopLeft).Length >= 0.1f || (actual[name].BottomRight - quad.BottomRight).Length >= 0.1f)
                        layoutFailure ??= $"{name} moved or resized on attempt {practice.Players.IndexOf((PracticeIsolationPlayer)player) + 1}: {quad} -> {actual[name]}.";
                }
            };
            first.ChildrenOfType<GameplayClockContainer>().Single().Seek(2500);
        });
        if (delayHud)
        {
            AddUntilStep("replacement HUD loading is delayed", () => hudLoadEntered.IsSet && practice.Players.Count == 2 && practice.Players[1].IsLoaded);
            AddWaitStep("retain previous HUD during loading", 5);
            AddAssert("replacement waits for complete HUD", () => practice.Players[1].Alpha == 0 && first.IsAlive && !first.IsPracticeDisposed);
            AddStep("finish skin loading", () => practiceHudLoadGate!.Set());
        }
        AddUntilStep("second count-in ready", () => practice.Players.Count == 2 && practice.Countdown.IsReady);
        AddWaitStep("observe revealed HUD", 8);
        AddStep("start second attempt", () => practice.SkipCountdown());
        AddWaitStep("observe HUD after count-in", 8);
        AddStep("verify uninterrupted repeat", () =>
        {
            assertPracticeTransitions(monitor);
            Assert.That(layoutFailure, Is.Null);
        });
        AddStep("return to settings", () => practice.AdjustRange());
        AddUntilStep("root restored", () => Stack.CurrentScreen == root);
        AddStep("leave root", () => root.Exit());
    }

    private static Dictionary<string, Quad> practiceHudLayout(BmsPracticePlayer player)
    {
        var hud = player.ChildrenOfType<HUDOverlay>().Single();
        var playfield = (BmsPlayfield)player.PracticeRuleset.Playfield;
        var drawables = hud.ChildrenOfType<Drawable>()
            .Where(drawable => drawable is BmsStageHud or BmsHealthDisplay or BmsScoreCounter).ToList();
        drawables.Add(playfield.Stage);
        drawables.AddRange(playfield.Stage.Columns.Select(column => column.KeyArea));
        return drawables.Select((drawable, index) => (Key: $"{drawable.GetType().Name}[{index}]", Quad: drawable.ScreenSpaceDrawQuad))
            .ToDictionary(entry => entry.Key, entry => entry.Quad);
    }

    private partial class LayoutPracticeScreen : PracticeIsolationScreen
    {
        private readonly BmsPracticeSession session;
        private readonly Func<ISkinSource> createSkin;

        internal LayoutPracticeScreen(BmsPracticeSession session, Func<ISkinSource> createSkin)
            : base(session)
        {
            this.session = session;
            this.createSkin = createSkin;
        }

        protected override BmsPracticePlayer CreatePlayer()
        {
            var player = new LayoutPracticePlayer(session, createSkin());
            Players.Add(player);
            return player;
        }
    }

    private partial class LayoutPracticePlayer(BmsPracticeSession session, ISkinSource source) : PracticeIsolationPlayer(session)
    {
        [Cached(typeof(ISkinSource))]
        private readonly ISkinSource source = source;
    }

    private class GatedPracticeSkin(ISkin skin, ManualResetEventSlim? gate, ManualResetEventSlim entered) : SkinTransformer(skin)
    {
        public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
        {
            var component = base.GetDrawableComponent(lookup);
            return gate != null && lookup is GlobalSkinnableContainerLookup { Lookup: GlobalSkinnableContainers.MainHUDComponents, Ruleset: null }
                                   && component is Container container
                ? new GatedPracticeHud(container, gate, entered)
                : component;
        }
    }

    private partial class GatedPracticeHud : Container
    {
        private readonly Container content;
        private readonly ManualResetEventSlim gate;
        private readonly ManualResetEventSlim entered;
        protected override Container<Drawable> Content => content;

        internal GatedPracticeHud(Container content, ManualResetEventSlim gate, ManualResetEventSlim entered)
        {
            this.content = content;
            this.gate = gate;
            this.entered = entered;
            RelativeSizeAxes = Axes.Both;
            InternalChild = content;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            entered.Set();
            if (!gate.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The delayed practice HUD was not released.");
        }
    }
}
