#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
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
public partial class TestSceneBmsLandmineKeyboardInput : BmsPlayerTestScene
{
    private BmsInputManager inputManager = null!;
    private readonly List<Action> assertions = [];

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(null);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 240,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 2000, Column = 1 },
                new BmsLandmine { StartTime = 3201, Column = 2, LandmineDamagePercent = 10 },
                new BmsNote { StartTime = 10000, Column = 3 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FinalKeyboardHeldStateControlsMineBeforeBody(bool releaseAtMine)
    {
        AddStep("load without replay", () => { assertions.Clear(); LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("attach normal keyboard device", () =>
        {
            // Freeze only the time source: Stop() would pause non-replay input processing.
            typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(Player.GameplayClockContainer, null);
            Assert.That(Player.GameplayClockContainer.IsPaused.Value, Is.False);
            inputManager = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
            assertNoReplay();
            var keyboard = new ScheduledKeyboard(() => Player.DrawableRuleset.FrameStableClock.CurrentTime,
                [(3000, new KeyboardKeyInput(releaseAtMine ? [new(Key.Z, true), new(Key.S, true)] : [new(Key.Z, true)])),
                    (3201, new KeyboardKeyInput([new(Key.S, !releaseAtMine)])),
                    (3202, new KeyboardKeyInput([new(Key.S, releaseAtMine)]))]);
            inputManager.UseParentInput = false;
            typeof(CustomInputManager).GetMethod("AddHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(inputManager, [keyboard]);
        });

        // Java 9cddf911 JudgeManager 219-338 reads final physical keys, then applies
        // all mines before bodies. NORMAL TOTAL240 / 3 gives head +80%, body +40%.
        checkpoint(3000, false, [1]);
        checkpoint(3200, false, []);
        checkpoint(3201, !releaseAtMine, releaseAtMine ? [1] : [.9, 1]);
        checkpoint(3202, !releaseAtMine, []);
        AddStep("assert keyboard mine timeline", () => Assert.Multiple(() =>
        {
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

    private void checkpoint(double time, bool expectedMine, double[] expectedFrameHealth)
    {
        AddStep("seek " + time, () => Player.GameplayClockContainer.Seek(time));
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
        AddStep("capture " + time, () =>
        {
            assertNoReplay();
            var health = Player.HealthProcessor.Health.Value;
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.ToArray();
            var frameHealth = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.Where(e => e.Time == time)
                .Select(e => e.States.Single(s => s.GaugeType == BmsGaugeType.Normal).Health).ToArray();
            TestContext.WriteLine($"keyboard {time}: HP={health:R}; frame=[{string.Join(",", frameHealth)}]; replay=null");
            assertions.Add(() =>
            {
                Assert.That(health, Is.EqualTo(1).Within(1e-8), "final keyboard health at " + time);
                Assert.That(frameHealth, Is.EqualTo(expectedFrameHealth).Within(1e-8), "ordered keyboard gauge history at " + time);
                Assert.That(events.Select(e => e.Result), Is.EqualTo(expectedMine
                    ? new[] { HitResult.Perfect, HitResult.Meh } : [HitResult.Perfect]), "no extra endpoints at " + time);
                Assert.That(events.Select(e => e.Source.Kind), Is.EqualTo(expectedMine
                    ? new[] { BmsJudgementSourceKind.LongNote, BmsJudgementSourceKind.Landmine } : [BmsJudgementSourceKind.LongNote]));
                Assert.That(events.Select(e => e.Source.Column), Is.EqualTo(expectedMine ? new[] { 1, 2 } : [1]));
                Assert.That(events.All(e => !e.SuppressPenalty && e.TimingObservations.Count == 1), Is.True);
                Assert.That(events.Select(e => e.TimingObservations.Single().ActualTime), Is.EqualTo(expectedMine ? new[] { 3000d, 3201 } : [3000d]));
                Assert.That(events.Select(e => e.TimingObservations.Single().TimeOffset), Is.All.EqualTo(0));
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
}
