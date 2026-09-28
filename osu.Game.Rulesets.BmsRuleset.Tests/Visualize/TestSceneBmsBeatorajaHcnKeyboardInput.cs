#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.StateChanges.Events;
using osu.Framework.Input.States;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsBeatorajaHcnKeyboardInput : BmsPlayerTestScene
{
    private bool scratch;
    private BmsInputManager inputManager = null!;
    private readonly List<Action> assertions = [];
    private readonly List<(double Time, Key Key, bool Pressed, bool ColumnHeld, bool ForwardHeld, bool ReverseHeld, bool Replay)> transitions = [];

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 12,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1101, Column = scratch ? 0 : 1 },
                new BmsNote { StartTime = 10000, Column = 1 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void KeyboardTailInputUsesCurrentFrameBodyStateWithoutReplay(bool reverseScratch)
    {
        AddStep("load without any replay", () =>
        {
            scratch = reverseScratch;
            assertions.Clear();
            transitions.Clear();
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("attach keyboard device to normal input manager", () =>
        {
            // Stop() also pauses simulation when no replay is attached. Freeze only its
            // time source so normal keyboard processing remains active at exact timestamps.
            typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Player.GameplayClockContainer, null);
            Assert.That(Player.GameplayClockContainer.IsPaused.Value, Is.False);
            inputManager = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
            assertNoReplay();
            var forward = scratch ? Key.LShift : Key.Z;
            var keyboard = new ScheduledKeyboard(() => Player.DrawableRuleset.FrameStableClock.CurrentTime,
                [(3000, new ObservedKeyboardInput([new(forward, true)], observe)),
                    (4001, new ObservedKeyboardInput(scratch
                        ? [new(forward, false), new(Key.LControl, true)]
                        : [new(forward, false)], observe))]);

            // Use the same ordinary KeyboardKeyInput polling route as a hardware handler.
            // AddHandler is protected; this test-only adapter avoids replacing the ruleset,
            // synthesising action events, or attaching a ReplayInputHandler to reach it.
            inputManager.UseParentInput = false;
            typeof(CustomInputManager).GetMethod("AddHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(inputManager, [keyboard]);
        });

        checkpoint(3000, .24, [HitResult.Perfect], [3000], [0], [.24]);
        checkpoint(3200, .24, [HitResult.Perfect], [3000], [0], []);
        checkpoint(3201, .26, [HitResult.Perfect], [3000], [0], [.26]);
        checkpoint(4000, .32, [HitResult.Perfect], [3000], [0], []);
        // NORMAL TOTAL12 / 3: PG/GREAT +4%, body +2%. Java reads all held keys before
        // body, then judges the -100ms GREAT tail. A reversal is still held; key-up is not.
        checkpoint(4001, reverseScratch ? .38 : .36, [HitResult.Perfect, HitResult.Great], [3000, 4001], [0, -100], reverseScratch ? [.34, .38] : [.36]);
        checkpoint(4002, reverseScratch ? .38 : .36, [HitResult.Perfect, HitResult.Great], [3000, 4001], [0, -100], []);
        checkpoint(4003, .38, [HitResult.Perfect, HitResult.Great], [3000, 4001], [0, -100], reverseScratch ? [] : [.38]);
        AddStep("assert keyboard transition phase and complete timeline", () => Assert.Multiple(() =>
        {
            assertNoReplay();
            Assert.That(transitions.All(t => !t.Replay), Is.True, "LastReplayState stayed null at every keyboard event");
            Assert.That(transitions.Select(t => (t.Time, t.Key, t.Pressed)), Is.EqualTo(scratch
                ? new[] { (3000d, Key.LShift, true), (4001d, Key.LShift, false), (4001d, Key.LControl, true) }
                : new[] { (3000d, Key.Z, true), (4001d, Key.Z, false) }));
            Assert.That(transitions[0].ForwardHeld, Is.True);
            Assert.That(transitions[1].ForwardHeld, Is.False);
            if (scratch)
            {
                Assert.That(transitions[2].ReverseHeld, Is.True);
                Assert.That(((BmsColumn)Playfield.Stage.Columns[0]).IsPressed, Is.True);
            }
            foreach (var assertion in assertions)
                assertion();
        }));
    }

    private void assertNoReplay()
    {
        Assert.That(Player.DrawableRuleset.HasReplayLoaded.Value, Is.False);
        Assert.That(inputManager.ReplayInputHandler, Is.Null);
        Assert.That(((RulesetInputManagerInputState<BmsAction>)inputManager.CurrentState).LastReplayState, Is.Null);
    }

    private void observe(InputState state, Key key, ButtonStateChangeKind kind)
    {
        var transition = (Player.DrawableRuleset.FrameStableClock.CurrentTime, key, kind == ButtonStateChangeKind.Pressed,
            ((BmsColumn)Playfield.Stage.Columns[scratch ? 0 : 1]).IsPressed,
            state.Keyboard.Keys.IsPressed(scratch ? Key.LShift : Key.Z), state.Keyboard.Keys.IsPressed(Key.LControl),
            ((RulesetInputManagerInputState<BmsAction>)state).LastReplayState != null);
        transitions.Add(transition);
        TestContext.WriteLine("keyboard before dispatch: " + transition);
    }

    private void checkpoint(double time, double expectedHealth, HitResult[] expectedResults, double[] expectedTimes,
                            double[] expectedOffsets, double[] expectedFrameHealth)
    {
        AddStep("seek " + time, () => Player.GameplayClockContainer.Seek(time));
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
        AddStep("capture keyboard frame " + time, () =>
        {
            assertNoReplay();
            var health = Player.HealthProcessor.Health.Value;
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.ToArray();
            var frameHealth = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Where(e => e.Time == time)
                .Select(e => e.States.Single(s => s.GaugeType == BmsGaugeType.Normal).Health).ToArray();
            TestContext.WriteLine($"{time}: HP={health:R}; frame=[{string.Join(",", frameHealth)}]; replay=null; "
                                  + string.Join("; ", events.Select(e => $"{e.Result}@{e.TimingObservations.Single().ActualTime}")));
            assertions.Add(() =>
            {
                Assert.That(health, Is.EqualTo(expectedHealth).Within(1e-8), "keyboard health at " + time);
                Assert.That(events.Select(e => e.Result), Is.EqualTo(expectedResults), "keyboard results at " + time);
                Assert.That(events.Select(e => e.Source.Column), Is.All.EqualTo(scratch ? 0 : 1));
                Assert.That(events.Select(e => e.TimingObservations.Single().ActualTime), Is.EqualTo(expectedTimes), "keyboard input times at " + time);
                Assert.That(events.Select(e => e.TimingObservations.Single().TimeOffset), Is.EqualTo(expectedOffsets), "keyboard offsets at " + time);
                Assert.That(frameHealth, Is.EqualTo(expectedFrameHealth).Within(1e-8), "keyboard NORMAL gauge history at " + time);
            });
        });
    }

    private sealed class ScheduledKeyboard(Func<double> getTime, (double Time, IInput Input)[] schedule) : InputHandler
    {
        private int next;

        public override bool IsActive => true;

        public override void CollectPendingInputs(List<IInput> inputs)
        {
            while (next < schedule.Length && getTime() >= schedule[next].Time)
                inputs.Add(schedule[next++].Input);
        }
    }

    private sealed class ObservedKeyboardInput(IEnumerable<ButtonInputEntry<Key>> entries,
                                              Action<InputState, Key, ButtonStateChangeKind> observe) : KeyboardKeyInput(entries)
    {
        protected override ButtonStateChangeEvent<Key> CreateEvent(InputState state, Key button, ButtonStateChangeKind kind)
        {
            // Observation only: ButtonInput.Apply has updated Keyboard.Keys but has not
            // yet dispatched the event through BmsInputManager and its key bindings.
            observe(state, button, kind);
            return base.CreateEvent(state, button, kind);
        }
    }
}
