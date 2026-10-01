#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Input.StateChanges;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsAutoScratchInput : BmsPlayerTestScene
{
    private BmsLongNoteMode mode;
    private bool recordedInputs;
    private bool adjacentHold;
    private BmsInputManager input = null!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(recordedInputs ? _ => recordedFrames() : null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 240,
            LockedLongNoteMode = mode,
            HitObjects = [new BmsNote { StartTime = 3000, Column = 0 },
                new BmsLongNote { StartTime = 3200, Duration = 600, Column = 0 },
                new BmsNote { StartTime = 3400, Column = 1 },
                new BmsLandmine { StartTime = 4000, Column = 0, LandmineDamagePercent = 10 },
                new BmsNote { StartTime = 10000, Column = 2 }],
        };
        if (adjacentHold)
            beatmap.HitObjects.Insert(3, new BmsLongNote { StartTime = 3800, Duration = 100, Column = 0 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void GeneratedInputsUseFrameworkHoldingAndRecording(BmsLongNoteMode longNoteMode)
    {
        load(longNoteMode);
        seek(2999);
        AddStep("physical scratch pressed before generation", () => keyboard(Key.LShift, true));
        seek(3000);
        AddAssert("generated scratch hits through framework", () => observations().Single().ActualTime, () => Is.EqualTo(3000));
        seek(3200);
        AddAssert("ordinary scratch input is held", () => scratch.PressedDirections, () => Is.EqualTo(1));
        AddStep("physical scratch release cannot release generated hold", () => keyboard(Key.LShift, false));
        seek(3300);
        AddAssert("hold survives physical release", () => scratch.PressedDirections, () => Is.EqualTo(1));
        seek(3400);
        AddStep("ordinary key remains playable", () => keyboard(Key.Z, true));
        AddUntilStep("ordinary key judged", () => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Any(e => e.Source.Column == 1 && e.Result.IsHit()));
        AddStep("release ordinary key and hold both physical scratch directions", () =>
        {
            keyboard(Key.Z, false);
            keyboard(Key.LShift, true);
            keyboard(Key.LControl, true);
        });
        seek(3800);
        seek(3810);
        AddAssert("tail releases while physical keys remain held", () => scratch.PressedDirections, () => Is.Zero);
        seek(4000);
        AddStep("check results and replay", () =>
        {
            var actual = observations();
            Assert.That(actual.Select(o => o.ExpectedTime), Is.EqualTo(new[] { 3000d, 3200, 3800 }));
            Assert.That(actual.All(o => o.Result.IsHit()), Is.True);
            var frames = Player.Score.Replay.Frames.OfType<BmsReplayFrame>().ToArray();
            Assert.That(frames.Any(f => f.IncludesGeneratedInputs), Is.True);
            Assert.That(frames.Any(f => f.Time == 3000 && f.Actions.Contains(BmsAction.Scratch)), Is.True);
            Assert.That(frames.Any(f => f.Time >= 3810 && !f.Actions.Contains(BmsAction.Scratch) && !f.Actions.Contains(BmsAction.ScratchReverse)), Is.True);
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Any(e => e.Source.Kind == BmsJudgementSourceKind.Landmine && e.Result == HitResult.Meh), Is.False);
        });
    }

    [Test]
    public void MarkedReplayUsesRecordedActionTimes()
    {
        load(BmsLongNoteMode.ChargeNote, recorded: true);
        seek(3900);
        AddStep("recorded timing is authoritative", () =>
        {
            Assert.That(observations().Select(o => o.ActualTime), Is.EqualTo(new[] { 3017d, 3219, 3800 }));
            Assert.That(observations().All(o => o.Result.IsHit()), Is.True);
            Assert.That(scratch.PressedDirections, Is.Zero);
        });
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void AdjacentHoldsReceiveSeparateTailAndHeadInputs(BmsLongNoteMode longNoteMode)
    {
        load(longNoteMode, adjacent: true);
        seek(3000);
        seek(3200);
        seek(3800);
        seek(3900);
        AddStep("both holds judged through framework", () =>
        {
            Assert.That(observations().Select(o => o.ExpectedTime), Is.EqualTo(new[] { 3000d, 3200, 3800, 3800, 3900 }));
            Assert.That(observations().All(o => o.Result.IsHit()), Is.True);
        });
    }

    [Test]
    public void RewindRearmsInputAndResumeRewindPreservesHold()
    {
        load(BmsLongNoteMode.ChargeNote);
        seek(3300);
        AddAssert("holding before rewind", () => scratch.PressedDirections, () => Is.EqualTo(1));
        seek(2999);
        AddAssert("ordinary rewind releases input", () => scratch.PressedDirections, () => Is.Zero);
        seek(3000);
        seek(3200);
        seek(3300);
        AddAssert("head replayed and held", () => scratch.PressedDirections, () => Is.EqualTo(1));
        var events = 0;
        AddStep("start resume rewind", () => { events = observations().Length; Playfield.BeginResumeRewind(3300, 0); });
        seek(2999);
        AddAssert("resume rewind does not regenerate", () => observations().Length, () => Is.EqualTo(events));
        seek(3300);
        AddAssert("hold retained through resume", () => scratch.PressedDirections, () => Is.EqualTo(1));
        seek(3800);
        AddAssert("single successful tail after resume", () => observations().Count(o => o.Kind == BmsTimingObservationKind.LongNoteTail && o.Result.IsHit()), () => Is.EqualTo(1));
    }

    private Gameplay.Judgement.BmsColumnJudgementRuntime scratch => ((BmsColumn)Playfield.Stage.Columns[0]).Judgements;

    private BmsTimingObservation[] observations() => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
        .Where(e => e.Source.Column == 0 && e.Source.Kind != BmsJudgementSourceKind.Landmine).SelectMany(e => e.TimingObservations).ToArray();

    private static IList<ReplayFrame> recordedFrames() =>
    [
        new BmsReplayFrame(0) { IncludesGeneratedInputs = true }, new BmsReplayFrame(3017, BmsAction.Scratch), new BmsReplayFrame(3027),
        new BmsReplayFrame(3219, BmsAction.Scratch), new BmsReplayFrame(3800, BmsAction.ScratchReverse), new BmsReplayFrame(3810), new BmsReplayFrame(60000),
    ];

    private void load(BmsLongNoteMode longNoteMode, bool recorded = false, bool adjacent = false)
    {
        AddStep("load AS player", () => { mode = longNoteMode; recordedInputs = recorded; adjacentHold = adjacent; LoadPlayer([new BmsModAutoScratch()]); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("freeze time and observe production input", () =>
        {
            typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Player.GameplayClockContainer, null);
            input = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
            if (!recorded)
            {
                input.UseParentInput = false;
                // The visual test player intentionally skips recording setup for manual-input scenes.
                Player.DrawableRuleset.SetRecordTarget(Player.Score);
            }
        });
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () => Player.GameplayClockContainer.Seek(time));
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private void keyboard(Key key, bool pressed) => new KeyboardKeyInput(key, pressed).Apply(input.CurrentState, input);

}
