#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.StateChanges.Events;
using osu.Framework.Input.States;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.UI;
using static osu.Game.Input.Handlers.ReplayInputHandler;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsFramedReplayInputHandlerTest
{
    [Test]
    public void ActionTransitionsMatchFrameworkEvents()
    {
        BmsAction[][] states =
        [
            [],
            [BmsAction.Key1],
            [BmsAction.Key2],
            [BmsAction.Key1, BmsAction.Key2],
            [BmsAction.Key2, BmsAction.Key1],
            [BmsAction.Scratch, BmsAction.P2ScratchReverse, BmsAction.P2PmsKey9],
            [BmsAction.Key1, BmsAction.Key1, BmsAction.Key2, BmsAction.Key2],
            [BmsAction.Key2, BmsAction.Key2, BmsAction.Key3, BmsAction.Key3],
            Enum.GetValues<BmsAction>(),
        ];
        var frames = states.SelectMany(previous => states.SelectMany(current => new[] { previous, current }))
                           .Select((actions, i) => new BmsReplayFrame(i * 1000, actions)).ToArray();
        using var handler = new BmsFramedReplayInputHandler(new osu.Game.Replays.Replay { Frames = [.. frames] });
        var state = new RulesetInputManagerInputState<BmsAction>(new InputState());
        var expectedState = new RulesetInputManagerInputState<BmsAction>(new InputState());
        var actual = new InputReceiver();
        var expected = new InputReceiver();
        var inputs = new List<IInput>();
        foreach (var frame in frames)
        {
            Assert.That(handler.SetFrameFromTime(frame.Time), Is.EqualTo(frame.Time));
            inputs.Clear();
            handler.CollectPendingInputs(inputs);
            foreach (var input in inputs) input.Apply(state, actual);
            new ReplayState<BmsAction> { PressedActions = frame.Actions }.Apply(expectedState, expected);

            Assert.That(actual.Changes[^1].Released, Is.EqualTo(expected.Changes[^1].Released));
            Assert.That(actual.Changes[^1].Pressed, Is.EqualTo(expected.Changes[^1].Pressed));
            Assert.That(state.LastReplayState, Is.SameAs(inputs.OfType<ReplayState<BmsAction>>().Single()));
        }
        Assert.That(actual.States, Is.EqualTo(expected.States));
    }

    [Test]
    public void LiveFramesRemainAvailableUntilTheStreamEnds()
    {
        var replay = new osu.Game.Replays.Replay
        {
            HasReceivedAllFrames = false,
            Frames = [new BmsReplayFrame(1000, BmsAction.Key1)],
        };
        var stops = new List<double>();
        using var handler = new BmsFramedReplayInputHandler(replay, stops.Add);
        var state = new RulesetInputManagerInputState<BmsAction>(new InputState());
        var receiver = new InputReceiver();
        collect(1000);
        Assert.That(stops, Is.Empty);

        replay.Frames.Add(new BmsReplayFrame(1500));
        replay.HasReceivedAllFrames = true;
        collect(1500);

        Assert.That(stops, Is.EqualTo(new[] { 1500d }));
        Assert.That(receiver.States.Select(actions => actions.Length), Is.EqualTo(new[] { 1, 0 }));

        void collect(double time)
        {
            Assert.That(handler.SetFrameFromTime(time), Is.EqualTo(time));
            var inputs = new List<IInput>();
            handler.CollectPendingInputs(inputs);
            foreach (var input in inputs) input.Apply(state, receiver);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OnlyCompleteAttemptsSettleHoldsAtTheTerminalFrame(bool complete)
    {
        var replay = new osu.Game.Replays.Replay
        {
            HasReceivedAllFrames = complete,
            Frames = [new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1500, BmsAction.Key1)],
        };
        var stops = new List<double>();
        using var handler = new BmsFramedReplayInputHandler(replay, stops.Add);
        var state = new RulesetInputManagerInputState<BmsAction>(new InputState());
        var receiver = new InputReceiver();
        foreach (var time in new[] { 1000, 1500, 1500 })
        {
            handler.SetFrameFromTime(time);
            var inputs = new List<IInput>();
            handler.CollectPendingInputs(inputs);
            foreach (var input in inputs) input.Apply(state, receiver);
        }

        Assert.That(stops, Is.EqualTo(complete ? new[] { 1500d } : []));
        Assert.That(replay.Frames.OfType<BmsReplayFrame>().Last().Actions, Is.EqualTo(new[] { BmsAction.Key1 }));
    }

    [Test]
    public void SameTimestampReleaseAndRepressAreCollectedInOneBatch()
    {
        var replay = new osu.Game.Replays.Replay
        {
            Frames = [new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1000), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(2000)],
        };
        using var handler = new BmsFramedReplayInputHandler(replay);
        handler.SetFrameFromTime(2000);
        handler.CollectPendingInputs([]);
        Assert.That(handler.SetFrameFromTime(2000), Is.EqualTo(1000));
        var inputs = new List<IInput>();
        handler.CollectPendingInputs(inputs);
        Assert.That(inputs.OfType<ReplayState<BmsAction>>().Select(state => state.PressedActions.Count), Is.EqualTo(new[] { 1, 0, 1 }));
        Assert.That(handler.CurrentFrame!.Actions, Is.EqualTo(new[] { BmsAction.Key1 }));

        var state = new RulesetInputManagerInputState<BmsAction>(new InputState());
        var receiver = new InputReceiver();
        foreach (var input in inputs) input.Apply(state, receiver);
        Assert.That(receiver.Changes.Select(change => change.Pressed), Is.EqualTo(new BmsAction[][] { [BmsAction.Key1], [], [BmsAction.Key1] }));
        Assert.That(receiver.Changes.Select(change => change.Released), Is.EqualTo(new BmsAction[][] { [], [BmsAction.Key1], [] }));
    }

    [Test]
    public void BackwardSeekRestoresTheFinalInputAtTheDestination()
    {
        var replay = new osu.Game.Replays.Replay
        {
            Frames = [new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1000), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(2000)],
        };
        using var handler = new BmsFramedReplayInputHandler(replay);

        while (handler.SetFrameFromTime(2000) is { } time && time != 2000)
            handler.CollectPendingInputs([]);
        handler.CollectPendingInputs([]);
        Assert.That(handler.SetFrameFromTime(1000), Is.EqualTo(1000));
        handler.CollectPendingInputs([]);
        Assert.That(handler.SetFrameFromTime(500), Is.EqualTo(500));

        var inputs = new List<IInput>();
        handler.CollectPendingInputs(inputs);

        Assert.That(inputs.OfType<ReplayState<BmsAction>>().Select(state => state.PressedActions.Count), Is.EqualTo(new[] { 0 }));
        Assert.That(handler.CurrentFrame!.Actions, Is.Empty);
        Assert.That(handler.NextFrame!.Time, Is.EqualTo(1000));
    }

    [Test]
    public void RebuildUsesTheFinalTargetAndRestoresTheLastEqualTimeInput()
    {
        var replay = new osu.Game.Replays.Replay
        {
            HasReceivedAllFrames = true,
            Frames = [new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1000),
                new BmsReplayFrame(1000, BmsAction.Key2), new BmsReplayFrame(2000)],
        };
        using var handler = new BmsFramedReplayInputHandler(replay) { FrameAccuratePlayback = true };
        while (handler.SetFrameFromTime(2000) != 2000)
            handler.CollectPendingInputs([]);
        handler.CollectPendingInputs([]);

        var rebuilds = new List<double>();
        var inputApplied = false;
        handler.ConfigureSeeking(() => 1000, time => { rebuilds.Add(time); return true; }, () => inputApplied = true);
        Assert.That(handler.SetFrameFromTime(1983), Is.EqualTo(1000));
        var inputs = new List<IInput>();
        handler.CollectPendingInputs(inputs);
        Assert.That(rebuilds, Is.EqualTo(new[] { 1000d }));
        Assert.That(inputs.OfType<ReplayState<BmsAction>>().Single().PressedActions, Is.EqualTo(new[] { BmsAction.Key2 }));
        Assert.That(handler.NextFrame!.Time, Is.EqualTo(2000));
        Assert.That(handler.FrameAccuratePlayback, Is.True);

        foreach (var input in inputs)
            input.Apply(new RulesetInputManagerInputState<BmsAction>(new InputState()), new InputReceiver());
        Assert.That(inputApplied, Is.True);
    }

    [TestCase(16)]
    [TestCase(1000)]
    [TestCase(1500)]
    [TestCase(2500)]
    public void RequestedForwardSeekUsesOneFinalInput(double target)
    {
        var replay = new osu.Game.Replays.Replay
        {
            Frames = [new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1000),
                new BmsReplayFrame(1000, BmsAction.Key2), new BmsReplayFrame(2000)],
        };
        var stops = new List<double>();
        using var handler = new BmsFramedReplayInputHandler(replay, stops.Add) { FrameAccuratePlayback = true };
        handler.SetFrameFromTime(0);
        handler.CollectPendingInputs([]);
        var seeks = new List<double>();
        var applied = 0;
        handler.ConfigureSeeking(() => target, time => { seeks.Add(time); return true; }, () => applied++);
        handler.RequestSeek();

        Assert.That(handler.SetFrameFromTime(16), Is.EqualTo(target));
        var inputs = new List<IInput>();
        handler.CollectPendingInputs(inputs);
        Assert.That(inputs.OfType<ReplayState<BmsAction>>().Single().PressedActions,
            Is.EqualTo(target >= 1000 && target < 2000 ? new[] { BmsAction.Key2 } : Array.Empty<BmsAction>()));
        var state = new RulesetInputManagerInputState<BmsAction>(new InputState());
        foreach (var input in inputs) input.Apply(state, new InputReceiver());
        Assert.That(seeks, Is.EqualTo(new[] { target }));
        Assert.That(applied, Is.EqualTo(1));
        Assert.That(stops, Is.Empty, "The silent simulation already settles terminal holds.");
        Assert.That(handler.FrameAccuratePlayback, Is.True);

        Assert.That(handler.SetFrameFromTime(target), Is.EqualTo(target));
        handler.CollectPendingInputs([]);
        Assert.That(seeks, Has.Count.EqualTo(1), "A seek request is consumed once.");
    }

    [Test]
    public void OrdinaryPlaybackKeepsFrameStepping()
    {
        using var handler = new BmsFramedReplayInputHandler(new osu.Game.Replays.Replay
        {
            Frames = [new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(2000)],
        });
        handler.SetFrameFromTime(0);
        handler.CollectPendingInputs([]);
        var seeks = 0;
        handler.ConfigureSeeking(() => 2000, _ => { seeks++; return true; }, () => { });

        Assert.That(handler.SetFrameFromTime(16), Is.EqualTo(16));
        Assert.That(seeks, Is.Zero);
    }

    [Test]
    public void StreamingSeekWaitsForKnownInputAndResumesAsFramesArrive()
    {
        var replay = new osu.Game.Replays.Replay { HasReceivedAllFrames = false };
        using var handler = new BmsFramedReplayInputHandler(replay) { FrameAccuratePlayback = true };
        var seeks = new List<double>();
        var target = 2500d;
        handler.ConfigureSeeking(() => target, time => { seeks.Add(time); return true; }, () => { });
        handler.RequestSeek();
        Assert.That(handler.SetFrameFromTime(16), Is.Null);
        Assert.That(seeks, Is.Empty);

        replay.Frames.Add(new BmsReplayFrame(1000, BmsAction.Key1));
        Assert.That(handler.SetFrameFromTime(16), Is.EqualTo(1000));
        handler.CollectPendingInputs([]);
        Assert.That(handler.WaitingForFrame, Is.True);
        Assert.That(handler.SetFrameFromTime(1016), Is.Null);
        Assert.That(seeks, Has.Count.EqualTo(1));

        replay.Frames.Add(new BmsReplayFrame(1000, BmsAction.Key2));
        Assert.That(handler.SetFrameFromTime(1016), Is.EqualTo(1000));
        var inputs = new List<IInput>();
        handler.CollectPendingInputs(inputs);
        Assert.That(inputs.OfType<ReplayState<BmsAction>>().Single().PressedActions, Is.EqualTo(new[] { BmsAction.Key2 }));

        replay.Frames.Add(new BmsReplayFrame(2000));
        Assert.That(handler.SetFrameFromTime(1016), Is.EqualTo(2000));
        handler.CollectPendingInputs([]);
        target = 500;
        handler.RequestSeek();
        Assert.That(handler.SetFrameFromTime(1984), Is.EqualTo(500));
        handler.CollectPendingInputs([]);
        Assert.That(handler.CurrentFrame, Is.Null);

        target = 2500;
        replay.HasReceivedAllFrames = true;
        handler.RequestSeek();
        Assert.That(handler.SetFrameFromTime(516), Is.EqualTo(2500));
        Assert.That(seeks, Is.EqualTo(new[] { 1000d, 1000, 2000, 500, 2500 }));
    }

    [Test]
    public void OrdinaryStreamingWaitsEvenWhenTheReferenceClockIsClamped()
    {
        var replay = new osu.Game.Replays.Replay
        {
            HasReceivedAllFrames = false,
            Frames = [new BmsReplayFrame(1000, BmsAction.Key1)],
        };
        using var handler = new BmsFramedReplayInputHandler(replay);
        handler.ConfigureSeeking(() => 2000, _ => throw new InvalidOperationException("Ordinary playback must not seek."), () => { });
        Assert.That(handler.SetFrameFromTime(1000), Is.EqualTo(1000));
        handler.CollectPendingInputs([]);
        Assert.That(handler.SetFrameFromTime(1000), Is.Null);
        replay.Frames.Add(new BmsReplayFrame(1500));
        Assert.That(handler.SetFrameFromTime(1016), Is.EqualTo(1016));
        Assert.That(handler.WaitingForFrame, Is.False);
    }

    private sealed class InputReceiver : IInputStateChangeHandler
    {
        internal List<BmsAction[]> States { get; } = [];
        internal List<(BmsAction[] Released, BmsAction[] Pressed)> Changes { get; } = [];

        public void HandleInputStateChange(InputStateChangeEvent inputStateChange)
        {
            if (inputStateChange is ReplayStateChangeEvent<BmsAction> replay)
            {
                States.Add(((ReplayState<BmsAction>)replay.Input).PressedActions.ToArray());
                Changes.Add((replay.ReleasedActions, replay.PressedActions));
            }
        }
    }
}
