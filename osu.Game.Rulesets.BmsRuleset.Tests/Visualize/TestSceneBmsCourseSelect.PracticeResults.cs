#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Practice;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsCourseSelect
{
    internal Drawable PracticeResultCaptureTarget => (Drawable)Stack.CurrentScreen;
    internal bool KeepPracticeResultOpen { get; set; }
    internal bool KeepPracticeCountdownOpen { get; set; }

    private partial class PracticeTransitionMonitor(Func<IScreen> currentScreen) : Container
    {
        internal int Frames { get; private set; }
        internal int ResultFrames { get; private set; }
        internal int TransitionFrames { get; private set; }
        internal Action? ObserveFrame { get; set; }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            ObserveFrame?.Invoke();
            Frames++;
            if (currentScreen() is not BmsPracticePlayer player)
            {
                ResultFrames++;
                return;
            }
            var retainedPresentation = player.Parent?.ChildrenOfType<BmsPracticePlayer>()
                .Any(previous => previous != player && previous.IsAlive && previous.Alpha == 1) == true;
            if (player.IsLoaded && ((!retainedPresentation && player.Alpha != 1) || player.Scale != Vector2.One
                || player.ChildrenOfType<GameplayClockContainer>().Single().Alpha != 1))
                TransitionFrames++;
        }
    }

    private PracticeTransitionMonitor monitorPracticeTransitions()
    {
        var monitor = new PracticeTransitionMonitor(() => Stack.CurrentScreen) { AlwaysPresent = true };
        Add(monitor);
        return monitor;
    }

    private void assertPracticeTransitions(PracticeTransitionMonitor monitor)
    {
        Assert.That(monitor.Frames, Is.GreaterThan(0));
        Assert.That(monitor.ResultFrames, Is.Zero, "Automatic repeats must keep a Player current in every frame.");
        Assert.That(monitor.TransitionFrames, Is.Zero, "Automatic repeats must not fade or scale the Player or gameplay clock.");
        Remove(monitor, true);
    }

    [Test]
    public void TestPracticeZeroCountInRepeatsWithoutShowingResults()
    {
        RealPracticeScreen practice = null!;
        RealPracticeRoot root = null!;
        BmsPracticeSession session = null!;
        PracticeTransitionMonitor monitor = null!;
        AddStep("prepare boundary notes with zero count-in", () =>
        {
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 1000, Column = 1, Beatmap = beatmap },
                new BmsLongNote { StartTime = 1499, Duration = 200, Column = 1, Beatmap = beatmap },
                new BmsNote { StartTime = 3000, Column = 1, Beatmap = beatmap },
            ];
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 1000, EndTime = 1500, CountIn = 0, SkipResults = true }, []);
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root entered", () => Stack.CurrentScreen == root);
        AddStep("start automatic practice", () => Stack.Push(practice = new RealPracticeScreen(session)));
        AddUntilStep("first attempt starts automatically", () => session.Attempts == 1);
        AddStep("watch every repeat frame", () => monitor = monitorPracticeTransitions());
        AddAssert("zero count-in retains note entry time", () => practice.Players[0].NaturalClock.StartTime < 900);
        AddUntilStep("second attempt starts automatically", () => session.Attempts >= 2);
        AddStep("verify completed tail and no result overlay", () =>
        {
            Assert.That(practice.Results[0].Statistics.Timeline.Duration, Is.EqualTo(699));
            Assert.That(session.Settings.CountIn, Is.Zero);
            Assert.That(practice.SelectedScore.Value, Is.Null);
            Assert.That(practice.Players[1].NaturalClock.StartTime, Is.LessThan(900));
            Assert.That(practice.Players[1].ChildrenOfType<BmsResultOverview>().Any(drawable => drawable.IsPresent), Is.False);
            Assert.That(practice.Players.All(player => player.ImportCount == 0), Is.True);
            assertPracticeTransitions(monitor);
            practice.AdjustRange();
        });
        AddUntilStep("return cancels automatic practice", () => Stack.CurrentScreen == root);
        AddWaitStep("no delayed restart", 3);
        AddAssert("only two attempts started", () => session.Attempts, () => Is.EqualTo(2));
        AddStep("exit test root", () => root.Exit());
    }

    [Test]
    public void TestPracticeAggregateSelectsEveryCompletedAttemptAndReturnsToSummary()
    {
        BmsPracticeSession session = null!;
        RealPracticeScreen practice = null!;
        RealPracticeRoot root = null!;
        BmsPracticeResultsScreen results = null!;
        ScoreInfo aggregate = null!;
        BmsPracticeOverviewHeader header = null!;
        IReadOnlyList<HitEvent>? displayedHits = null;

        void clickAttempt(int index)
        {
            var card = results.ChildrenOfType<BmsPracticeAttemptCard>()
                .Single(card => card.Name == $"Practice attempt {session.AttemptResults[index].Attempt} result");
            InputManager.MoveMouseTo(card);
            InputManager.Click(MouseButton.Left);
        }

        AddStep("prepare two short continuous practice rounds", () =>
        {
            var beatmap = BmsTestBeatmaps.CreateBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { StartTime = 1100, Column = 1, Beatmap = beatmap },
                new BmsLongNote { StartTime = 1400, Duration = 300, Column = 2, Beatmap = beatmap },
                new BmsNote { StartTime = 3000, Column = 1, Beatmap = beatmap },
            ];
            var source = new TestWorkingBeatmap(beatmap, audioManager: Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source,
                new BmsPracticeSettings { StartTime = 1000, EndTime = 2000, CountIn = 5, SkipResults = true }, []);
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root entered", () => Stack.CurrentScreen == root);
        AddStep("open continuous practice", () => Stack.Push(practice = new RealPracticeScreen(session)));
        AddUntilStep("first opening count-in ready", () => practice.Countdown.IsReady);
        AddStep("start first round", () => practice.SkipCountdown());
        AddUntilStep("second round reloaded and waiting at its start", () => practice.Results.Count == 1
            && practice.Players.Count == 2 && Stack.CurrentScreen == practice.Players[1] && practice.Countdown.IsReady);
        AddStep("start second round", () => practice.SkipCountdown());
        AddUntilStep("both rounds complete and third opening count-in ready", () => practice.Results.Count == 2
            && practice.Players.Count == 3 && Stack.CurrentScreen == practice.Players[2] && practice.Countdown.IsReady);
        AddStep("quit from the new round's opening count-in", () =>
        {
            var player = practice.Players[2];
            Assert.That(player.HasAttemptStarted, Is.False);
            Assert.That(player.NaturalClock.CurrentTime, Is.LessThan(session.Settings.StartTime));
            Assert.That(player.Pause(), Is.True);
            player.ChildrenOfType<PauseOverlay>().Single().Buttons.Last().TriggerClick();
        });
        AddUntilStep("aggregate results ready", () => Stack.CurrentScreen is BmsPracticeResultsScreen screen
            && screen.SelectedScore.Value != null && TestSceneBmsResultScreenStatistics.ChartsFitViewport(screen));
        AddStep("observe displayed result data", () =>
        {
            results = (BmsPracticeResultsScreen)Stack.CurrentScreen;
            aggregate = results.SelectedScore.Value!;
            header = results.ChildrenOfType<BmsPracticeOverviewHeader>().Single();
            results.ChildrenOfType<BmsStatisticsPanel>().Single().StatisticsLoaded += hits => displayedHits = hits;
            Assert.That(session.AttemptResults, Has.Count.EqualTo(2));
            Assert.That(results.ChildrenOfType<BmsPracticeAttemptCard>().Count(), Is.EqualTo(2));
        });
        for (var index = 0; index < 2; index++)
        {
            var attemptIndex = index;
            AddStep($"click round {index + 1}", () => clickAttempt(attemptIndex));
            AddUntilStep($"round {index + 1} score and full statistics loaded", () =>
                results.SelectedScore.Value == session.AttemptResults[attemptIndex].Score
                && ReferenceEquals(displayedHits, session.AttemptResults[attemptIndex].Statistics.HitEvents));
            AddAssert($"round {index + 1} is highlighted and fits", () =>
                results.ChildrenOfType<BmsPracticeAttemptCard>().Single(card => card.IsSelected).Name
                == $"Practice attempt {session.AttemptResults[attemptIndex].Attempt} result"
                && results.ChildrenOfType<BmsPracticeOverviewHeader>().Single() == header
                && results.ChildrenOfType<BmsResultOverview>().Single().Score == session.AttemptResults[attemptIndex].Score
                && TestSceneBmsResultScreenStatistics.ChartsFitViewport(results));
            if (index == 0)
                AddStep("Escape returns to the aggregate", () => InputManager.Key(Key.Escape));
            else
                AddStep("summary heading returns to the aggregate", () => results.ChildrenOfType<OsuClickableContainer>()
                    .Single(container => container.Name == "Practice aggregate selector").TriggerClick());
            AddUntilStep("aggregate restored without leaving the screen", () => Stack.CurrentScreen == results
                && results.SelectedScore.Value == aggregate && results.ChildrenOfType<BmsPracticeAttemptCard>().All(card => !card.IsSelected));
        }
        AddStep("select the second round again", () => clickAttempt(1));
        AddUntilStep("second round selected again", () => results.SelectedScore.Value == session.AttemptResults[1].Score);
        AddStep("click selected round to return to aggregate", () => clickAttempt(1));
        AddUntilStep("selected card toggles back to aggregate", () => results.SelectedScore.Value == aggregate);
        AddStep("select a round before starting next practice", () => clickAttempt(0));
        AddUntilStep("first round selected before restart", () => results.SelectedScore.Value == session.AttemptResults[0].Score);
        AddStep("Enter starts another practice from round details", () => InputManager.Key(Key.Enter));
        AddUntilStep("next practice reloads and waits at its opening count-in", () => practice.Players.Count == 4
            && Stack.CurrentScreen == practice.Players[3] && practice.Countdown.IsReady);
        AddStep("return to settings cancels the unstarted round", () =>
        {
            Assert.That(session.Attempts, Is.EqualTo(2));
            Assert.That(session.AttemptResults, Has.Count.EqualTo(2));
            Assert.That(session.AttemptResults.All(attempt => attempt.Score.HitEvents.Count > 0), Is.True);
            practice.AdjustRange();
        });
        AddUntilStep("settings root restored with no active countdown", () => Stack.CurrentScreen == root && !practice.Countdown.IsReady);
        AddStep("leave root", () => root.Exit());
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void TestPracticeResultWaitsForNextAttempt(bool keypad, bool click)
    {
        BmsPracticeSession session = null!;
        RealPracticeScreen practice = null!;
        RealPracticeRoot root = null!;
        var expectedCount = 0;

        AddStep("prepare a real Destr0yer practice interval", () =>
        {
            var path = Path.Combine(realPracticeSongsRoot(), "Destr0yer (by 削除 feat. Nikki Simmons)", "7ANOTHER.bml");
            var decoded = BmsBeatmapDecoder.DecodeBytes(File.ReadAllBytes(path), randomValueSelector: _ => 1);
            var full = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
            {
                BranchRandomValueSelector = _ => 1,
            }.Convert();
            BmsTestBeatmaps.SetupBeatmapInfo(full, Ruleset.Value, bpm: full.BeatmapInfo.BPM,
                overallDifficulty: full.Difficulty.OverallDifficulty, drainRate: full.Difficulty.DrainRate);
            full.BeatmapInfo.DifficultyName = Path.GetFileNameWithoutExtension(path);
            full.Metadata.Source = Path.GetDirectoryName(path)!;
            var source = new BmsWorkingBeatmap(new TestWorkingBeatmap(full, audioManager: Audio), Audio);
            Beatmap.Value = source;
            session = new BmsPracticeSession(source, selectRealPracticeRange(full, true) with { SkipResults = false },
                [new BmsModHalfTime { SpeedChange = { Value = 0.75 } }]);
            expectedCount = BmsPracticeBeatmap.Create(full, session.Settings).CountScoringNotes();
            Stack.Push(root = new RealPracticeRoot());
        });
        AddUntilStep("root ready", () => Stack.CurrentScreen == root && root.IsLoaded);
        AddStep("open practice", () => Stack.Push(practice = new RealPracticeScreen(session)));
        AddUntilStep("first attempt starts without the stored count-in", () => session.Attempts == 1 && practice.Players[0].HasAttemptStarted);
        AddAssert("stored countdown is retained for automatic repeats", () => session.Settings.CountIn, () => Is.EqualTo(2));
        AddUntilStep("first interval reaches its midpoint", () => practice.Players[0].NaturalClock.CurrentTime
            >= session.Settings.StartTime + 3000);
        AddUntilStep("real interval finishes and its result opens", () => practice.Results.Count == 1 && Stack.CurrentScreen == practice);
        AddWaitStep("result waits for manual restart", 3);
        AddStep("verify completed statistics and the visible result panel", () =>
        {
            var result = practice.LastResult!;
            assertRealPracticeCompleted(practice.Players[0], result, expectedCount, true);
            Assert.That(session.Attempts, Is.EqualTo(1));
            Assert.That(practice.Players.Count, Is.EqualTo(1));
            Assert.That(practice.Countdown.IsReady, Is.False);
            Assert.That(Realm.Run(realm => realm.All<ScoreInfo>().Count()), Is.EqualTo(scoreCountAtTestStart));

            var text = practice.ChildrenOfType<OsuSpriteText>().Where(drawable => drawable.IsPresent)
                .Select(drawable => drawable.Text.ToString()).ToArray();
            Assert.That(practice, Is.InstanceOf<BmsResultsScreen>());
            Assert.That(practice.ChildrenOfType<BmsResultOverview>().Single().Score, Is.SameAs(result.Score));
            Assert.That(practice.ChildrenOfType<BmsResultStatisticsGrid>().Count(), Is.EqualTo(1));
            Assert.That(practice.ChildrenOfType<BmsResultComparisonButton>(), Is.Empty);
            Assert.That(result.Statistics.Gauges, Is.Not.Empty);
            Assert.That(result.Statistics.Timeline.Duration, Is.EqualTo(session.Settings.EndTime - session.Settings.StartTime));
            Assert.That(result.Statistics.Scatter.Overall.Duration, Is.EqualTo(result.Statistics.Timeline.Duration));
            Assert.That(result.Statistics.Timeline.Duration, Is.LessThan(8000));
            Assert.That(result.Statistics.Scatter.Overall.Points.All(point => point.Time >= 0 && point.Time < 8000), Is.True);
            Assert.That(practice.ChildrenOfType<BmsCourseResultButton>().Count(), Is.EqualTo(2));
            Assert.That(text, Does.Contain(BmsStrings.PracticeReturnToSettings.ToString()));
            Assert.That(text, Does.Contain(BmsStrings.PracticeNextAttempt.ToString()));
        });
        if (!KeepPracticeResultOpen)
        {
            AddStep("start next attempt with button or Enter", () =>
            {
                if (click)
                    practice.ChildrenOfType<BmsCourseResultButton>().Single(button => button.Name == "Start next practice attempt button").TriggerClick();
                else
                    InputManager.PressKey(keypad ? Key.KeypadEnter : Key.Enter);
            });
            AddUntilStep("fresh attempt starts directly", () => practice.Players.Count == 2 && practice.Players[1].HasAttemptStarted);
            AddStep("release Enter and inspect restart", () =>
            {
                InputManager.ReleaseKey(keypad ? Key.KeypadEnter : Key.Enter);
                Assert.That(session.Attempts, Is.EqualTo(2));
                Assert.That(practice.Countdown.IsReady, Is.False);
                Assert.That(practice.Countdown.RemainingMilliseconds, Is.Zero);
                Assert.That(practice.Players[1].PreparedJudgements, Is.Zero);
                Assert.That(practice.Players[1].NaturalClock.StartTime, Is.LessThan(session.Settings.StartTime - 100));
                Assert.That(practice.Players[1].ChildrenOfType<BmsResultOverview>().Any(drawable => drawable.IsPresent), Is.False);
            });
            AddUntilStep("second interval returns to results", () => practice.Results.Count == 2 && Stack.CurrentScreen == practice);
            AddStep("return to settings using the result control", () => practice.ChildrenOfType<BmsCourseResultButton>()
                .Single(button => button.Name == "Return to practice settings button").TriggerClick());
            AddUntilStep("root restored", () => Stack.CurrentScreen == root);
            AddAssert("return keeps countdown inactive", () => !practice.Countdown.IsReady && session.Attempts == 2);
            AddStep("leave the test root", () => root.Exit());
        }
    }
}
