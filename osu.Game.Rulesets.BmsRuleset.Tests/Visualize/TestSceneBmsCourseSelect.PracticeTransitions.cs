using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps.Timing;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    private double? previousPracticeDim;
    private bool previousPracticeBreakLightening;

    [TearDown]
    public void RestorePracticeDimPreference()
    {
        if (previousPracticeDim is not { } dim)
            return;

        config.SetValue(OsuSetting.DimLevel, dim);
        config.SetValue(OsuSetting.LightenDuringBreaks, previousPracticeBreakLightening);
        previousPracticeDim = null;
    }

    private void configurePracticeDim()
    {
        previousPracticeDim = config.Get<double>(OsuSetting.DimLevel);
        previousPracticeBreakLightening = config.Get<bool>(OsuSetting.LightenDuringBreaks);
        config.SetValue(OsuSetting.DimLevel, 0.7);
        config.SetValue(OsuSetting.LightenDuringBreaks, true);
    }

    [TestCase(0, false)]
    [TestCase(5, false)]
    [TestCase(0, true)]
    [TestCase(5, true)]
    public void TestPracticeRepeatKeepsPresentationUntilReady(int countIn, bool exitDuringPreparation)
    {
        BmsPracticeSession session = null!;
        DeferredRepeatPracticeScreen practice = null!;
        DeferredRepeatPracticePlayer first = null!;
        DeferredRepeatPracticePlayer second = null!;
        BackgroundScreenBeatmap background = null!;
        RealPracticeRoot root = null!;
        PracticeTransitionMonitor monitor = null!;
        BackgroundScreenBeatmap.DimmableBackground dimmable = null!;
        var brightest = 0f;

        AddStep("prepare repeating practice", () =>
        {
            configurePracticeDim();
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 1100, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 3000, Column = 1, Beatmap = beatmap },
            ];
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 1000, EndTime = 2000, SkipResults = true, CountIn = countIn }, []);
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root entered", () => Stack.CurrentScreen == root);
        AddStep("open practice", () => Stack.Push(practice = new DeferredRepeatPracticeScreen(session)));
        AddUntilStep("first player ready", () => countIn == 0 ? session.Attempts == 1 : practice.Countdown.IsReady);
        AddStep("start first attempt and track background", () =>
        {
            first = (DeferredRepeatPracticePlayer)practice.Players[0];
            first.ApplyToBackground(value => background = value);
            practice.SkipCountdown();
        });
        AddUntilStep("first attempt running", () => first.HasAttemptStarted && background != null && !background.IgnoreUserSettings.Value);
        AddStep("seek into active gameplay", () => first.ChildrenOfType<GameplayClockContainer>().Single().Seek(1500));
        AddUntilStep("gameplay break has ended", () => !first.IsBreakTime.Value);
        AddStep("monitor and complete first attempt", () =>
        {
            dimmable = background.ChildrenOfType<BackgroundScreenBeatmap.DimmableBackground>().Single();
            dimmable.FinishTransforms(true);
            Assert.That(dimmable.Children.Single().Parent!.Colour.AverageColour.SRGB.R, Is.EqualTo(OsuColour.Gray(0.3f).R).Within(0.001));
            monitor = monitorPracticeTransitions();
            monitor.ObserveFrame = () => brightest = Math.Max(brightest, dimmable.Children.Single().Parent!.Colour.AverageColour.SRGB.R);
            first.ChildrenOfType<GameplayClockContainer>().Single().Seek(2500);
        });
        AddUntilStep("repeat waits for preparation", () => practice.Players.Count == 2
            && Stack.CurrentScreen == practice.Players[1] && practice.Players[1].IsLoaded);
        AddStep("verify uninterrupted frozen presentation", () =>
        {
            second = (DeferredRepeatPracticePlayer)practice.Players[1];
            Assert.That(first.IsPracticeDisposed, Is.False);
            Assert.That(first.IsAlive && first.IsPresent, Is.True);
            Assert.That(first.Alpha, Is.EqualTo(1));
            Assert.That(first.Scale, Is.EqualTo(Vector2.One));
            Assert.That(second.Alpha, Is.Zero, "The replacement must stay hidden until preparation finishes.");
            Assert.That(background.IgnoreUserSettings.Value, Is.False);
            Assert.That(first.DimmableStoryboard.IsBreakTime.Value, Is.False);
            Assert.That(second.DimmableStoryboard.IsBreakTime.Value, Is.False);
            second.ApplyToBackground(value => Assert.That(value, Is.SameAs(background)));
        });
        AddWaitStep("keep presentation during delayed preparation", 5);
        AddAssert("outgoing player remains available", () => first.IsAlive && !first.IsPracticeDisposed && first.Alpha == 1);
        AddAssert("background stays dimmed", () => !background.IgnoreUserSettings.Value);
        if (!exitDuringPreparation)
        {
            AddStep("finish replacement preparation", () => second.ContinuePreparation());
            AddUntilStep("replacement revealed", () => second.Alpha == 1 && (countIn == 0 ? second.HasAttemptStarted : practice.Countdown.IsReady));
            AddUntilStep("old player released", () => first.IsPracticeDisposed);
            AddStep("verify stable transition and pause layer", () =>
            {
                Assert.That(background.IgnoreUserSettings.Value, Is.False);
                Assert.That(second.Scale, Is.EqualTo(Vector2.One));
                Assert.That(second.ChildrenOfType<GameplayClockContainer>().Single().Alpha, Is.EqualTo(1));
                second.Pause();
                assertPracticePauseLayer(second);
            });
        }
        AddStep("verify every monitored frame", () =>
        {
            assertPracticeTransitions(monitor);
            Assert.That(brightest, Is.LessThanOrEqualTo(0.301f), "Completion, loading and count-in must retain the configured dim even with break lightening enabled.");
        });
        AddStep("return to settings", () => practice.AdjustRange());
        AddUntilStep("root restored", () => Stack.CurrentScreen == root);
        AddUntilStep("both players released", () => first.IsPracticeDisposed && second.IsPracticeDisposed);
        AddAssert("cancelled preparation cannot start an attempt", () => session.Attempts, () => Is.EqualTo(!exitDuringPreparation && countIn == 0 ? 2 : 1));
        AddStep("leave root", () => root.Exit());
    }

    [Test]
    public void TestPracticeLightensOnlyDuringChartBreaks()
    {
        BmsPracticeSession session = null!;
        PracticeIsolationScreen practice = null!;
        PracticeIsolationPlayer player = null!;
        BackgroundScreenBeatmap background = null!;
        BackgroundScreenBeatmap.DimmableBackground dimmable = null!;
        RealPracticeRoot root = null!;

        AddStep("prepare practice with a chart break", () =>
        {
            configurePracticeDim();
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 1100, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 29000, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 40000, Column = 1, Beatmap = beatmap },
            ];
            beatmap.Breaks = [new BreakPeriod(5000, 20000)];
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 1000, EndTime = 30000, SkipResults = true, CountIn = 5 }, []);
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root entered", () => Stack.CurrentScreen == root);
        AddStep("open practice", () => Stack.Push(practice = new PracticeIsolationScreen(session)));
        AddUntilStep("count-in ready", () => practice.Countdown.IsReady);
        AddStep("start and enter chart break", () =>
        {
            player = practice.Players.Single();
            player.ApplyToBackground(value => background = value);
            practice.SkipCountdown();
            player.ChildrenOfType<GameplayClockContainer>().Single().Seek(10000);
        });
        AddUntilStep("chart break active", () => background != null && player.IsBreakTime.Value && player.DimmableStoryboard.IsBreakTime.Value);
        AddStep("chart break still lightens background", () =>
        {
            dimmable = background.ChildrenOfType<BackgroundScreenBeatmap.DimmableBackground>().Single();
            dimmable.FinishTransforms(true);
            Assert.That(dimmable.Children.Single().Parent!.Colour.AverageColour.SRGB.R, Is.EqualTo(0.6f).Within(0.001));
            player.ChildrenOfType<GameplayClockContainer>().Single().Seek(22000);
        });
        AddUntilStep("chart break ended", () => !player.IsBreakTime.Value && !player.DimmableStoryboard.IsBreakTime.Value);
        AddStep("gameplay restores configured dim", () =>
        {
            dimmable.FinishTransforms(true);
            Assert.That(dimmable.Children.Single().Parent!.Colour.AverageColour.SRGB.R, Is.EqualTo(0.3f).Within(0.001));
            practice.AdjustRange();
        });
        AddUntilStep("root restored", () => Stack.CurrentScreen == root);
        AddStep("leave root", () => root.Exit());
    }

    private partial class DeferredRepeatPracticeScreen : PracticeIsolationScreen
    {
        private readonly BmsPracticeSession session;

        internal DeferredRepeatPracticeScreen(BmsPracticeSession session)
            : base(session)
        {
            this.session = session;
        }

        protected override BmsPracticePlayer CreatePlayer()
        {
            var player = new DeferredRepeatPracticePlayer(session, Players.Count > 0);
            Players.Add(player);
            return player;
        }
    }

    private partial class DeferredRepeatPracticePlayer(BmsPracticeSession session, bool deferPreparation) : PracticeIsolationPlayer(session)
    {
        protected override void StartGameplay()
        {
            if (!deferPreparation)
                base.StartGameplay();
        }

        internal void ContinuePreparation() => base.StartGameplay();
    }
}
