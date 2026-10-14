using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsOperationSequence : BmsPlayerTestScene
{
    private BmsLongNoteMode mode;
    private bool scratch;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 1000,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsLongNote { StartTime = 3000, Duration = 1000, Column = scratch ? 0 : 1 },
                new BmsNote { StartTime = 3250, Column = 2 },
                new BmsLongNote { StartTime = 3300, Duration = 300, Column = 4 },
                new BmsLandmine { StartTime = 3500, Column = 3 },
                new BmsLandmine { StartTime = 3600, Column = 3 },
                new BmsNote { StartTime = 5000, Column = 5 },
                new BmsNote { StartTime = 30000, Column = 6 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void FrameworkAndSimulationAgreeAcrossUpdateSpans(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode longNoteMode,
        [Values(false, true)] bool scratchColumn,
        [Values(17, 1000, 5000)] double interval)
    {
        AddStep("load operation chart", () => { mode = longNoteMode; scratch = scratchColumn; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek before operations", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2798); });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2798) < .001);
        AddStep("compare timed input results and gauge", () => compare(interval));
    }

    private void compare(double interval)
    {
        var previousClock = Playfield.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2798, Rate = 1, IsRunning = true };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = clock;
        Player.HealthProcessor.Clock = clock;
        try
        {
            var health = (BmsHealthProcessor)Player.HealthProcessor;
            var source = new ScoreInfo { Mods = Player.ScoreProcessor.Mods.Value.ToArray() };
            var held = scratch ? BmsAction.Scratch : BmsAction.Key1;
            BmsReplayFrame[] frames =
            [
                new(2798), new(3000, held), new(3400, held, BmsAction.Key3), new(3500, held),
                new(3600, held, BmsAction.Key3), new(3601, held),
                scratch ? new(3990, BmsAction.ScratchReverse) : new(3990, held),
                scratch ? new(4000, BmsAction.ScratchReverse) : new(4000), new(4001), new(7000),
            ];
            var replay = new Replay { Frames = frames.Cast<ReplayFrame>().ToList(), HasReceivedAllFrames = true };
            var expected = BmsReplaySimulation.Run(Playfield.Beatmap, replay, source);
            var engine = (BmsJudgementEngine)typeof(BmsPlayfield).GetField("judgementEngine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Playfield)!;
            var pressed = new HashSet<BmsAction>();
            var cursor = 0;
            for (var time = 2798 + interval; manual.CurrentTime < 7000; time += interval)
            {
                var end = Math.Min(7000, time);
                while (cursor < frames.Length && frames[cursor].Time <= end)
                {
                    var frame = frames[cursor++];
                    var actions = frame.Actions.ToHashSet();
                    foreach (var action in pressed)
                        if (!actions.Contains(action)) queue(action, false, frame.Time);
                    foreach (var action in actions)
                        if (!pressed.Contains(action)) queue(action, true, frame.Time);
                    pressed = actions;
                }
                manual.CurrentTime = end;
                clock.ProcessFrame();
                Playfield.EndGameplayFrame();
                Playfield.UpdateSubTree();
            }
            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            Assert.That(JsonSerializer.Serialize(score.JudgementEvents), Is.EqualTo(JsonSerializer.Serialize(expected.Judgements)));
            Assert.That(JsonSerializer.Serialize(health.GaugeHistory), Is.EqualTo(JsonSerializer.Serialize(expected.Gauges)));
            var timings = score.JudgementEvents.SelectMany(item => item.TimingObservations).ToArray();
            Assert.That(timings.Single(item => item.Kind == BmsTimingObservationKind.LongNoteHead && item.ExpectedTime == 3000).ActualTime, Is.EqualTo(3000));
            Assert.That(score.JudgementEvents.Count(item => item.Source.Kind == BmsJudgementSourceKind.Landmine), Is.EqualTo(1));
            Assert.That(score.JudgementEvents.Single(item => item.Source.StartTime == 5000).Result, Is.EqualTo(HitResult.Meh));

            void queue(BmsAction action, bool down, double time)
            {
                var column = BmsKeyBindingConfiguration.ActionToColumn(action, Playfield.LayoutVariant)!.Value;
                engine.QueueInput(time, column, down, BmsKeyBindingConfiguration.IsReverseScratch(action));
            }
        }
        finally
        {
            Playfield.Clock = previousClock;
            Playfield.ProcessCustomClock = processClock;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }
}
