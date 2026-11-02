using System.Linq;
using NUnit.Framework;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    private bool? previousPracticePauseHold;
    private double previousPracticeHoldDelay;

    [TearDown]
    public void RestorePracticePausePreference()
    {
        if (previousPracticePauseHold is not { } requireHold)
            return;

        config.SetValue(OsuSetting.AlwaysRequireHoldingForPause, requireHold);
        config.SetValue(OsuSetting.UIHoldActivationDelay, previousPracticeHoldDelay);
        previousPracticePauseHold = null;
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void TestPracticePauseUsesNormalHoldPreference(bool requireHold, bool duringCountIn)
    {
        BmsPracticeSession session = null!;
        PracticeIsolationScreen practice = null!;
        PracticeIsolationPlayer player = null!;
        RealPracticeRoot root = null!;

        AddStep("configure pause preference and practice", () =>
        {
            previousPracticePauseHold = config.Get<bool>(OsuSetting.AlwaysRequireHoldingForPause);
            previousPracticeHoldDelay = config.Get<double>(OsuSetting.UIHoldActivationDelay);
            config.SetValue(OsuSetting.AlwaysRequireHoldingForPause, requireHold);
            config.SetValue(OsuSetting.UIHoldActivationDelay, 1000.0);
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 11000, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 29000, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 40000, Column = 1, Beatmap = beatmap },
            ];
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 10000, EndTime = 30000, SkipResults = duringCountIn, CountIn = 5 }, []);
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root entered", () => Stack.CurrentScreen == root);
        AddStep("open practice", () => Stack.Push(practice = new PracticeIsolationScreen(session)));
        AddUntilStep("practice ready in selected phase", () => practice.Players.Count == 1
            && Stack.CurrentScreen == practice.Players[0]
            && (duringCountIn ? practice.Countdown.IsReady : practice.Players[0].HasAttemptStarted));
        AddStep("tap Escape", () =>
        {
            player = practice.Players[0];
            var pauseButton = player.ChildrenOfType<HUDOverlay>().Single().HoldToQuit.ChildrenOfType<HoldToConfirmContainer>().Single();
            Assert.That(pauseButton.IsDangerousAction, Is.False);
            InputManager.Key(Key.Escape);
        });
        AddAssert("tap follows normal pause preference", () => player.IsPauseMenuVisible, () => Is.EqualTo(!requireHold));

        if (requireHold)
        {
            AddWaitStep("released tap never pauses later", 3);
            AddAssert("pause menu still hidden", () => !player.IsPauseMenuVisible);
            AddStep("hold Escape", () => InputManager.PressKey(Key.Escape));
            AddUntilStep("holding opens pause menu", () => player.IsPauseMenuVisible);
            AddStep("release Escape", () => InputManager.ReleaseKey(Key.Escape));
        }

        AddAssert("pause preference was preserved", () => config.Get<bool>(OsuSetting.AlwaysRequireHoldingForPause), () => Is.EqualTo(requireHold));
        AddAssert("gameplay clock is paused", () => player.ChildrenOfType<GameplayClockContainer>().Single().IsPaused.Value);
        if (duringCountIn)
        {
            AddAssert("count-in is paused before first attempt", () => practice.Countdown.IsPaused && session.Attempts == 0);
            AddStep("continue count-in through pause menu", () => player.ChildrenOfType<PauseOverlay>().Single().Buttons.First().TriggerClick());
            AddUntilStep("count-in resumed", () => !practice.Countdown.IsPaused && !player.IsPauseMenuVisible);
            AddAssert("continuing count-in keeps gameplay stopped", () => !player.HasAttemptStarted
                && player.ChildrenOfType<GameplayClockContainer>().Single().IsPaused.Value);
        }
        AddStep("return to range settings", () => practice.AdjustRange());
        AddUntilStep("root restored", () => Stack.CurrentScreen == root);
        AddAssert("practice imported no scores", () => player.ImportCount, () => Is.Zero);
        AddStep("leave root", () => root.Exit());
    }

    [TestCase(0)]
    [TestCase(5)]
    public void TestPracticeReloadPreservesPauseAtTheNewOpening(int countIn)
    {
        BmsPracticeSession session = null!;
        PausedRepeatPracticeScreen practice = null!;
        PracticeIsolationPlayer reloaded = null!;
        RealPracticeRoot root = null!;

        AddStep("prepare a repeat paused during reload", () =>
        {
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
        AddStep("open practice", () => Stack.Push(practice = new PausedRepeatPracticeScreen(session)));
        if (countIn > 0)
        {
            AddUntilStep("first opening countdown ready", () => practice.Countdown.IsReady);
            AddStep("skip first countdown", () => practice.SkipCountdown());
        }
        AddUntilStep("first attempt started", () => session.Attempts == 1 && practice.Players[0].HasAttemptStarted);
        AddStep("finish first attempt", () => practice.Players[0].ChildrenOfType<GameplayClockContainer>().Single().Seek(2500));
        AddUntilStep("new playfield is current with its countdown paused", () => practice.Players.Count == 2
            && Stack.CurrentScreen == practice.Players[1] && practice.Countdown.IsReady);
        AddStep("verify pause survives the player replacement", () =>
        {
            reloaded = practice.Players[1];
            Assert.That(reloaded.IsPauseMenuVisible, Is.True);
            Assert.That(practice.Countdown.IsPaused, Is.True);
            Assert.That(practice.Countdown.RemainingMilliseconds, Is.EqualTo(countIn * 1000));
            Assert.That(reloaded.ChildrenOfType<GameplayClockContainer>().Single().CurrentTime, Is.LessThan(session.Settings.StartTime));
            Assert.That(reloaded.HasAttemptStarted, Is.False);
        });
        AddWaitStep("paused reload cannot start itself", 3);
        AddAssert("only the completed attempt has started", () => session.Attempts, () => Is.EqualTo(1));
        AddStep("continue from the new pause menu", () => reloaded.ChildrenOfType<PauseOverlay>().Single().Buttons.First().TriggerClick());
        if (countIn > 0)
        {
            AddAssert("continue starts the full opening countdown", () => !practice.Countdown.IsPaused
                && practice.Countdown.RemainingMilliseconds > 4500 && !reloaded.HasAttemptStarted);
            AddStep("skip resumed countdown", () => practice.SkipCountdown());
        }
        AddUntilStep("new attempt starts after explicit continue", () => session.Attempts == 2 && reloaded.HasAttemptStarted);
        AddStep("return to settings", () => practice.AdjustRange());
        AddUntilStep("root restored", () => Stack.CurrentScreen == root);
        AddStep("leave root", () => root.Exit());
    }

    private partial class PausedRepeatPracticeScreen(BmsPracticeSession session) : PracticeIsolationScreen(session)
    {
        protected override BmsPracticePlayer CreatePlayer()
        {
            var player = base.CreatePlayer();
            if (Players.Count == 1)
                player.AttemptCompleted += _ => player.Pause();
            return player;
        }
    }
}
