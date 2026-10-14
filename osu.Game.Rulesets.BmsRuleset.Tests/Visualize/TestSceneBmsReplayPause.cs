#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsReplayPause : BmsPlayerTestScene
{
    private BmsLongNoteMode mode;
    private bool sameTimeMine;
    private bool recordOnly;
    private Score? courseReplay;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(recordOnly ? null : _ => courseReplay?.Replay.Frames ?? (sameTimeMine ? mineInputs() : inputs()),
            replayAlgorithm: BmsJudgementAlgorithm.Combo,
            replayMods: courseReplay?.ScoreInfo.Mods);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 1000,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1 },
                new BmsNote { StartTime = 5000, Column = 2 },
                new BmsNote { StartTime = 30000, Column = 3 },
            ],
        };
        if (sameTimeMine)
            beatmap.HitObjects = [new BmsNote { StartTime = 3000, Column = 1 }, new BmsLandmine { StartTime = 3000, Column = 1 }, new BmsNote { StartTime = 30000, Column = 3 }];
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void PlaybackAndSimulationAgreeAfterRecordedPause(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode noteMode)
    {
        AddStep("load paused replay", () => { mode = noteMode; sameTimeMine = false; recordOnly = false; courseReplay = null; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek through pause", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(5500);
        });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 5500) < .001);
        AddStep("compare replay and reconstructed results", () =>
        {
            var source = new ScoreInfo { Mods = Player.ScoreProcessor.Mods.Value.ToArray() };
            source.Pauses.Add(3500);
            var replay = new osu.Game.Replays.Replay { Frames = [.. inputs()] };
            var expected = BmsReplaySimulation.Run(Playfield.Beatmap, replay, source);
            Assert.That(JsonSerializer.Serialize(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents), Is.EqualTo(JsonSerializer.Serialize(expected.Judgements)));
            Assert.That(JsonSerializer.Serialize(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory), Is.EqualTo(JsonSerializer.Serialize(expected.Gauges)));
        });
    }

    [Test]
    public void SameTimestampReleaseAvoidsMineInRealPlayback()
    {
        AddStep("load same-time mine replay", () => { mode = BmsLongNoteMode.LongNote; sameTimeMine = true; recordOnly = false; courseReplay = null; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek past simultaneous operations", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(5500); });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 5500) < .001);
        AddStep("compare final state mine decision", () =>
        {
            var source = new ScoreInfo { Mods = Player.ScoreProcessor.Mods.Value.ToArray() };
            var expected = BmsReplaySimulation.Run(Playfield.Beatmap, new osu.Game.Replays.Replay { Frames = [.. mineInputs()] }, source);
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            Assert.That(JsonSerializer.Serialize(events), Is.EqualTo(JsonSerializer.Serialize(expected.Judgements)));
            Assert.That(events.Any(item => item.Source.Kind == BmsJudgementSourceKind.Landmine), Is.False);
        });
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void PlaybackAndSimulationSettleTheHeldTerminalFrame(BmsLongNoteMode noteMode)
    {
        AddStep("load aborted hold replay", () =>
        {
            mode = noteMode;
            sameTimeMine = false;
            recordOnly = false;
            courseReplay = new Score
            {
                Replay = new osu.Game.Replays.Replay { Frames = [new BmsReplayFrame(3000, BmsAction.Key1), new BmsReplayFrame(3500.375, BmsAction.Key1)] },
            };
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek to attempt end", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(3500.375); });
        AddUntilStep("clock reached terminal frame", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 3500.375) < .001);
        AddStep("compare terminal tail and gauge", () =>
        {
            var expected = BmsReplaySimulation.Run(Playfield.Beatmap, courseReplay!.Replay, courseReplay.ScoreInfo);
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents;
            Assert.That(events.SelectMany(item => item.TimingObservations).Count(item => item.Kind == BmsTimingObservationKind.LongNoteTail), Is.EqualTo(1));
            Assert.That(JsonSerializer.Serialize(events), Is.EqualTo(JsonSerializer.Serialize(expected.Judgements)));
            Assert.That(JsonSerializer.Serialize(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory), Is.EqualTo(JsonSerializer.Serialize(expected.Gauges)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CourseReplayRestoresClonedInitialGaugeBeforePlayback(bool autoGauge)
    {
        AddStep("load saved course replay", () =>
        {
            mode = BmsLongNoteMode.HellChargeNote;
            sameTimeMine = false;
            recordOnly = false;
            var score = new Score
            {
                Replay = new osu.Game.Replays.Replay { Frames = [.. inputs()] },
                ScoreInfo = new ScoreInfo
                {
                    Ruleset = new BmsRuleset().RulesetInfo,
                    Mods = autoGauge ?
                    [
                        new BmsModAutoGauge
                        {
                            ClassHealth = { Value = 0.4 },
                            ExClassHealth = { Value = 0.3 },
                            GaugeProfileFamily = { Value = BmsGaugeProfileFamily.Lr2 },
                        },
                        new BmsModClassGauge(),
                    ] :
                    [
                        new BmsModClassGauge
                        {
                            GaugeProfileFamily = { Value = BmsGaugeProfileFamily.Lr2 },
                            InitialHealth = { Value = 0.4 },
                        },
                    ],
                },
            };
            courseReplay = BmsTestReplayArchive.RoundTrip(score.DeepClone());
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("assert restored course health", () =>
        {
            var health = (BmsHealthProcessor)Player.HealthProcessor;
            Assert.Multiple(() =>
            {
                Assert.That(health.IsCourseGaugeMode, Is.True);
                Assert.That(health.ConfiguredProfileFamily, Is.EqualTo(BmsGaugeProfileFamily.Lr2));
                Assert.That(health.CurrentGaugeStates, Is.EqualTo(autoGauge ? new[]
                {
                    new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.3, false),
                    new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.4, false),
                } : [new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.4, false)]));
            });
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(5500);
        });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 5500) < .001);
        AddStep("compare saved course result", () =>
        {
            var expected = BmsReplaySimulation.Run(Playfield.Beatmap, courseReplay!.Replay, courseReplay.ScoreInfo);
            Assert.That(JsonSerializer.Serialize(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents), Is.EqualTo(JsonSerializer.Serialize(expected.Judgements)));
            Assert.That(JsonSerializer.Serialize(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory), Is.EqualTo(JsonSerializer.Serialize(expected.Gauges)));
        });
        AddStep("seek back to dan opening", () => Player.GameplayClockContainer.Seek(0));
        AddUntilStep("rewind completed", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime) < .001);
        AddStep("carried health survives rewind", () =>
        {
            var health = (BmsHealthProcessor)Player.HealthProcessor;
            Assert.Multiple(() =>
            {
                Assert.That(health.Health.Value, Is.EqualTo(autoGauge ? 0.3 : 0.4));
                Assert.That(health.CurrentGaugeStates.Select(state => state.Health), Is.EqualTo(autoGauge ? new[] { 0.3, 0.4 } : [0.4]));
                Assert.That(health.CurrentGaugeStates.All(state => !state.Failed), Is.True);
            });
            Player.GameplayClockContainer.Seek(5500);
        });
        AddUntilStep("forward replay completed", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 5500) < .001);
        AddStep("replaying produces the same gauge history", () =>
        {
            var expected = BmsReplaySimulation.Run(Playfield.Beatmap, courseReplay!.Replay, courseReplay.ScoreInfo);
            Assert.That(JsonSerializer.Serialize(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory), Is.EqualTo(JsonSerializer.Serialize(expected.Gauges)));
        });
    }

    [Test]
    public void LiveRecorderKeepsOnlyOperationsAndOneTerminalFrame()
    {
        AddStep("load recording scene", () => { mode = BmsLongNoteMode.LongNote; sameTimeMine = false; recordOnly = true; courseReplay = null; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("attach live recorder", () => Player.DrawableRuleset.SetRecordTarget(Player.Score));
        AddUntilStep("recorder loaded", () => ((BmsInputManager)((global::osu.Game.Rulesets.BmsRuleset.UI.Gameplay.BmsDrawableRuleset)Player.DrawableRuleset).KeyBindingInputManager).Recorder?.IsLoaded == true);
        AddStep("record a long idle", () =>
        {
            Player.GameplayClockContainer.Stop();
            var input = (BmsInputManager)((global::osu.Game.Rulesets.BmsRuleset.UI.Gameplay.BmsDrawableRuleset)Player.DrawableRuleset).KeyBindingInputManager;
            var recorder = input.Recorder!;
            var manual = new ManualClock { CurrentTime = 1000, Rate = 1 };
            var clock = new FramedClock(manual);
            clock.ProcessFrame();
            recorder.Clock = clock;
            recorder.RecordFrame(false);
            input.KeyBindingContainer.TriggerPressed(BmsAction.Key1);
            manual.CurrentTime = 2500;
            clock.ProcessFrame();
            for (var i = 0; i < 20; i++)
                recorder.RecordFrame(false);

            var frames = Player.Score.Replay.Frames.Cast<BmsReplayFrame>().ToArray();
            Assert.That(frames, Has.Length.EqualTo(3));
            Assert.That(frames[^1].Time, Is.EqualTo(2500));
            Assert.That(frames[^1].Actions, Is.EqualTo(new[] { BmsAction.Key1 }));
        });
    }

    private static IList<ReplayFrame> mineInputs() => [new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Key1), new BmsReplayFrame(3000), new BmsReplayFrame(5500)];

    private static IList<ReplayFrame> inputs() =>
    [
        new BmsReplayFrame(0),
        new BmsReplayFrame(3000, BmsAction.Key1),
        new BmsReplayFrame(3500, BmsAction.Key1),
        new BmsReplayFrame(4000),
        new BmsReplayFrame(5000, BmsAction.Key2),
        new BmsReplayFrame(5010),
        new BmsReplayFrame(5500),
    ];
}
