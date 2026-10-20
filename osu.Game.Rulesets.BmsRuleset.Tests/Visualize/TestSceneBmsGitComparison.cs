#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

// Keep this scene and its observer portable to the pre-refactor checkout: only host inputs and outputs are shared.
[HeadlessTest, NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsGitComparison : BmsPlayerTestScene
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private BmsLongNoteMode mode;
    private BmsLayoutVariant layout;
    private BmsJudgementAlgorithm? algorithm;
    private bool replay;
    private BmsTestSkins.SkinKind skin;
    private BmsInputManager inputManager = null!;
    private static FrameStabilityContainer? budgetHost;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(replay ? _ => frames() : null, skin, algorithm);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout, TotalColumns = layout == BmsLayoutVariant.Bme7KDouble ? 16 : layout == BmsLayoutVariant.Pms9K ? 9 : 8,
            Rank = 2, Total = 24, LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 0, SampleKey = 11, TailSampleKey = 12 },
                new BmsNote { StartTime = 3000, Column = 1, SampleKey = 13 },
                new BmsLongNote { StartTime = 3002, Duration = 1000, Column = 1, SampleKey = 14, TailSampleKey = 15 },
                new BmsNote { StartTime = 3030, Column = 1, SampleKey = 16 },
                new BmsLandmine { StartTime = 3201, Column = 1, LandmineDamagePercent = 3 },
                new BmsLandmine { StartTime = 3202, Column = 0, LandmineDamagePercent = 3 },
                new BmsNote { StartTime = 3400, Column = 1, SampleKey = 18 },
                new BmsNote { StartTime = 3400, Column = 1, SampleKey = 19 },
                new BmsLongNote { StartTime = 3600, Duration = 500, Column = 2, SampleKey = 20, TailSampleKey = 21 },
                new BmsNote { StartTime = 4200, Column = 2, SampleKey = 22 },
                new BmsNote { StartTime = 12000, Column = 3 },
            ],
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 3100, Column = 1, SampleKey = 17 }],
        };
        if (layout == BmsLayoutVariant.Bme7KDouble)
            beatmap.HitObjects.Add(new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 15, SampleKey = 23, TailSampleKey = 24 });
        if (!autoScratchMine)
            beatmap.HitObjects.RemoveAll(n => n is BmsLandmine && n.Column == 0);
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(n => n.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private IList<ReplayFrame> frames()
    {
        var first = BmsKeyBindingConfiguration.ActionForColumn(layout, 0)!.Value;
        var second = BmsKeyBindingConfiguration.ActionForColumn(layout, 1)!.Value;
        var third = BmsKeyBindingConfiguration.ActionForColumn(layout, 2)!.Value;
        var reverse = BmsKeyBindingConfiguration.ReverseScratchAction(first) ?? first;
        IList<ReplayFrame> result =
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(3000, first, second), new BmsReplayFrame(3001, first), new BmsReplayFrame(3002, first, second),
            new BmsReplayFrame(3201, first), new BmsReplayFrame(3202, first, second), new BmsReplayFrame(3350, first),
            new BmsReplayFrame(3400, first, second), new BmsReplayFrame(3400, first), new BmsReplayFrame(3400, first, second),
            new BmsReplayFrame(3401, first), new BmsReplayFrame(3990, first, reverse), new BmsReplayFrame(3991, reverse),
            new BmsReplayFrame(4002), new BmsReplayFrame(4200, third), new BmsReplayFrame(4201), new BmsReplayFrame(13000),
        ];
        if (layout == BmsLayoutVariant.Bme7KDouble)
        {
            foreach (var frame in result.Cast<BmsReplayFrame>().Where(f => f.Time >= 3000 && f.Time < 4002))
                frame.Actions.Add(frame.Time < 3990 ? BmsAction.P2Scratch : BmsAction.P2ScratchReverse);
        }

        return result;
    }

    [Test]
    public void CaptureKeyboard(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)]
        BmsLongNoteMode noteMode,
        [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Bme7K2P)]
        BmsLayoutVariant variant,
        [Values(false, true)] bool deferred)
    {
        load(noteMode, variant, false, BmsJudgementAlgorithm.Combo, BmsTestSkins.SkinKind.Argon);
        AddStep("record real keyboard inputs and lifecycle", () => withClock((advance, keyboard) =>
        {
            using var observer = new BmsGitComparisonRecorder(Playfield, Player.ScoreProcessor, Player.HealthProcessor);
            var bindings = BmsKeyBindingConfiguration.GetDefaultKeyBindings((int)layout);
            var previousKeys = new HashSet<Key>();
            if (deferred) Playfield.BeginGameplayFrame();
            foreach (var frame in frames().Cast<BmsReplayFrame>().Where(f => f.Time >= 3000 && f.Time <= 4201))
            {
                advance(frame.Time);
                var keys = frame.Actions.Select(a => Enum.Parse<Key>(bindings.First(b => b.Action.Equals(a)).KeyCombination.Keys.Single().ToString())).ToHashSet();
                keyboard.Inputs.Add(new KeyboardKeyInput(previousKeys.Except(keys).Select(k => new ButtonInputEntry<Key>(k, false))
                    .Concat(keys.Except(previousKeys).Select(k => new ButtonInputEntry<Key>(k, true)))));
                previousKeys = keys;
                inputManager.UpdateSubTree();
                observer.Mark("input");
                if (frame.Time == 3202)
                {
                    Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().First().ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
                    observer.Mark("reapply");
                }
            }

            if (deferred) Playfield.EndGameplayFrame();
            foreach (var time in new[] { 4500d, 6000, 2998, 3000, 3202 })
            {
                advance(time);
                inputManager.UpdateSubTree();
                observer.Mark("lifecycle");
            }

            observer.Save($"keyboard-{mode}-{layout}-{deferred}");
        }));
    }

    [Test]
    public void CaptureReplay(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)]
        BmsLongNoteMode noteMode,
        [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Bme7K2P)]
        BmsLayoutVariant variant,
        [Values(null, BmsJudgementAlgorithm.Combo, BmsJudgementAlgorithm.Duration, BmsJudgementAlgorithm.Lowest, BmsJudgementAlgorithm.Score)]
        BmsJudgementAlgorithm? selection)
    {
        load(noteMode, variant, true, selection, BmsTestSkins.SkinKind.Argon);
        AddStep("record replay catchup and rewind", () => captureReplay($"replay-{mode}-{layout}-{selection?.ToString() ?? "legacy"}"));
    }

    [TestCase(BmsTestSkins.SkinKind.Classic)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void CaptureSkin(BmsTestSkins.SkinKind kind)
    {
        load(BmsLongNoteMode.HellChargeNote, BmsLayoutVariant.Bme7K, true, BmsJudgementAlgorithm.Combo, kind);
        AddStep("record skinned replay", () => captureReplay("skin-" + kind));
    }

    private void captureReplay(string scenario)
    {
        budgetHost = Player.DrawableRuleset.ChildrenOfType<FrameStabilityContainer>().Single();
        using var budget = new ScopedMethodProbe(typeof(FrameStabilityContainer).GetMethod("updateClock", instance)!,
            typeof(TestSceneBmsGitComparison).GetMethod(nameof(fixWallClockBudget), BindingFlags.Static | BindingFlags.NonPublic)!);
        using var observer = new BmsGitComparisonRecorder(Playfield, Player.ScoreProcessor, Player.HealthProcessor);
        foreach (var time in new[] { 3000d, 3002, 3202, 3401, 3991, 4202, 6000, 2998, 3202, 4202 })
        {
            SeekReplayInFrames(time);
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(time).Within(.001));
            if (time == 2998)
            {
                Assert.That(Player.ScoreProcessor.Statistics.Values, Is.All.Zero);
                Assert.That(CurrentResults, Is.Empty);
            }
            observer.Mark("seek");
        }

        observer.Save(scenario, rebuiltReplay: true);
        budgetHost = null;
    }

    private static void fixWallClockBudget(FrameStabilityContainer __instance)
    {
        // Machine load may split catchup across outer frames; the real stability clock still chooses all simulation steps.
        if (ReferenceEquals(__instance, budgetHost))
            ((Stopwatch)typeof(FrameStabilityContainer).GetField("stopwatch", instance)!.GetValue(__instance)!).Reset();
    }

    private void load(BmsLongNoteMode noteMode, BmsLayoutVariant variant, bool useReplay, BmsJudgementAlgorithm? selection, BmsTestSkins.SkinKind kind)
    {
        AddStep("load comparison chart", () =>
        {
            mode = noteMode;
            layout = variant;
            replay = useReplay;
            algorithm = selection;
            skin = kind;
            autoScratchMine = true;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("freeze host clock", () =>
        {
            inputManager = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
            typeof(GameplayClockContainer).GetMethod("StopGameplayClock", instance)!.Invoke(Player.GameplayClockContainer, null);
            Player.GameplayClockContainer.Seek(2998);
        });
        AddUntilStep("simulation at start", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2998) < .001);
    }

    private void withClock(Action<Action<double>, KeyboardDevice> action)
    {
        var previous = Playfield.Clock;
        var inputClock = inputManager.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var process = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2998, IsRunning = true, Rate = 1 };
        var framed = new FramedClock(manual);
        framed.ProcessFrame();
        Playfield.Clock = framed;
        Playfield.ProcessCustomClock = false;
        inputManager.Clock = framed;
        inputManager.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = framed;
        Player.HealthProcessor.Clock = framed;
        var keyboard = new KeyboardDevice();
        inputManager.UseParentInput = false;
        typeof(CustomInputManager).GetMethod("AddHandler", instance)!.Invoke(inputManager, [keyboard]);
        try
        {
            Playfield.EndGameplayFrame();
            inputManager.UpdateSubTree();
            action(time =>
            {
                manual.CurrentTime = time;
                framed.ProcessFrame();
            }, keyboard);
        }
        finally
        {
            Playfield.EndGameplayFrame();
            Playfield.Clock = previous;
            Playfield.ProcessCustomClock = process;
            inputManager.Clock = inputClock;
            inputManager.ProcessCustomClock = true;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }

    private sealed class KeyboardDevice : InputHandler
    {
        internal List<IInput> Inputs { get; } = [];

        public override bool IsActive => true;

        public override void CollectPendingInputs(List<IInput> inputs)
        {
            inputs.AddRange(Inputs);
            Inputs.Clear();
        }
    }
}
