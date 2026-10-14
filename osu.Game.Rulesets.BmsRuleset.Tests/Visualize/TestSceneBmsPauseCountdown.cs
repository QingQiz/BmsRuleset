#nullable enable
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsPauseCountdown : BmsPlayerTestScene
{
    public enum PausedInput { Held, Released, Repressed }

    private BmsLongNoteMode mode;
    private BmsInputManager input = null!;
    private BmsLongNote note => (BmsLongNote)Playfield.Beatmap.HitObjects[0];
    private BmsColumn column => (BmsColumn)Playfield.Stage.Columns[1];
    private BmsScoreProcessor score => (BmsScoreProcessor)Player.ScoreProcessor;
    private BmsHealthProcessor health => (BmsHealthProcessor)Player.HealthProcessor;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => new(allowPause: true, showResults: false);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 240,
            LockedLongNoteMode = mode,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1 }, new BmsNote { StartTime = 30000, Column = 2 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void CountdownPreservesHoldsAndRecordedInputResults(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode longNoteMode,
        [Values] PausedInput pausedInput)
    {
        var pauseTime = 0d;
        var judgements = "";
        var gauges = "";
        load(longNoteMode);
        seek(3000);
        AddStep("press head through framework input", () => InputManager.PressKey(Key.Z));
        AddUntilStep("head held", () => column.Judgements.GetLongNoteState(note).IsHolding);
        seek(3500);
        AddStep("pause while holding", () =>
        {
            Assert.That(Player.Pause(), Is.True);
            pauseTime = Player.GameplayClockContainer.CurrentTime;
            judgements = JsonSerializer.Serialize(score.JudgementEvents);
            gauges = JsonSerializer.Serialize(health.GaugeHistory);
        });
        AddStep("change physical input while paused", () =>
        {
            if (pausedInput != PausedInput.Held) InputManager.ReleaseKey(Key.Z);
        });
        AddStep("repress while paused", () =>
        {
            if (pausedInput == PausedInput.Repressed) InputManager.PressKey(Key.Z);
        });
        AddStep("resume with mania countdown", () =>
        {
            Player.Resume();
            Assert.That(Player.IsResuming, Is.True);
            Assert.That(Player.DrawableRuleset.ResumeOverlay, Is.TypeOf<DelayedResumeOverlay>());
            Assert.That(Player.DrawableRuleset.ResumeOverlay!.State.Value, Is.EqualTo(Visibility.Visible));
        });
        AddWaitStep("countdown advances independently", 5);
        AddStep("gameplay and judgement remain frozen", () =>
        {
            Assert.That(Player.GameplayClockContainer.IsPaused.Value, Is.True);
            Assert.That(Player.GameplayClockContainer.CurrentTime, Is.EqualTo(pauseTime).Within(.001));
            Assert.That(column.Judgements.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(JsonSerializer.Serialize(score.JudgementEvents), Is.EqualTo(judgements));
            Assert.That(JsonSerializer.Serialize(health.GaugeHistory), Is.EqualTo(gauges));
        });
        AddUntilStep("countdown resumes gameplay", () => !Player.IsResuming && !Player.GameplayClockContainer.IsPaused.Value);
        AddUntilStep("physical state synchronised", () => column.IsPressed == (pausedInput != PausedInput.Released));
        AddAssert("continues forward from pause time", () => Player.DrawableRuleset.FrameStableClock.CurrentTime,
            () => Is.InRange(pauseTime, pauseTime + 250));
        seek(3990);
        AddStep("release tail", () => InputManager.ReleaseKey(Key.Z));
        seek(4100);
        AddStep("freeze final result", () => Player.GameplayClockContainer.Stop());
        AddWaitStep("finish the last gameplay frame", 2);
        AddStep("recorded operations reproduce live judgements and gauge", () =>
        {
            Assert.That(column.Judgements.GetLongNoteState(note).TailJudged, Is.True);
            var endpoints = score.JudgementEvents.SelectMany(item => item.TimingObservations).ToArray();
            Assert.That(endpoints.Count(item => item.Kind == BmsTimingObservationKind.LongNoteHead), Is.EqualTo(1));
            Assert.That(endpoints.Count(item => item.Kind == BmsTimingObservationKind.LongNoteTail), Is.EqualTo(1));
            var saved = BmsTestReplayArchive.RoundTrip(Player.Score.DeepClone());
            var result = BmsReplaySimulation.Run(Playfield.Beatmap, saved.Replay, saved.ScoreInfo);
            Assert.That(JsonSerializer.Serialize(result.Judgements), Is.EqualTo(JsonSerializer.Serialize(score.JudgementEvents)));
            Assert.That(JsonSerializer.Serialize(result.Gauges), Is.EqualTo(JsonSerializer.Serialize(health.GaugeHistory)));
            var times = Player.Score.Replay.Frames.Select(frame => frame.Time).ToArray();
            Assert.That(times, Is.Ordered);
        });
    }

    [Test]
    public void PausingDuringCountdownCancelsAndRestartsIt()
    {
        var pauseTime = 0d;
        load(BmsLongNoteMode.LongNote);
        seek(3500);
        AddStep("pause and start countdown", () =>
        {
            Assert.That(Player.Pause(), Is.True);
            pauseTime = Player.GameplayClockContainer.CurrentTime;
            Player.Resume();
        });
        AddAssert("countdown active", () => Player.IsResuming);
        AddStep("cancel countdown by pausing", () => Assert.That(Player.Pause(), Is.True));
        AddWaitStep("remain paused", 5);
        AddAssert("countdown cancelled", () => !Player.IsResuming && Player.GameplayClockContainer.IsPaused.Value
            && Player.DrawableRuleset.ResumeOverlay!.State.Value == Visibility.Hidden);
        AddAssert("time unchanged", () => Player.DrawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(pauseTime).Within(.001));
        AddStep("restart countdown", () => Player.Resume());
        AddUntilStep("countdown completes", () => !Player.IsResuming && !Player.GameplayClockContainer.IsPaused.Value);
        AddAssert("continues from pause point", () => Player.DrawableRuleset.FrameStableClock.CurrentTime, () => Is.InRange(pauseTime, pauseTime + 250));
    }

    private void load(BmsLongNoteMode longNoteMode)
    {
        AddStep("load pausable player", () =>
        {
            foreach (var key in InputManager.CurrentState.Keyboard.Keys.ToArray()) InputManager.ReleaseKey(key);
            mode = longNoteMode;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("prepare actual input and recorder", () =>
        {
            InputManager.MoveMouseTo(Player.DrawableRuleset.Playfield);
            input = (BmsInputManager)((BmsDrawableRuleset)Player.DrawableRuleset).KeyBindingInputManager;
            if (input.Recorder == null) Player.DrawableRuleset.SetRecordTarget(Player.Score);
        });
        AddUntilStep("recorder loaded", () => input.Recorder?.IsLoaded == true);
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () => Player.GameplayClockContainer.Seek(time));
        AddUntilStep("clock reached " + time, () => Player.DrawableRuleset.FrameStableClock.CurrentTime >= time);
    }
}
