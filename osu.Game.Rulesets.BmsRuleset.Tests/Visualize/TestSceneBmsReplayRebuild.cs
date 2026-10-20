using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Screens.Play.HUD;
using osu.Game.Screens.Play.HUD.JudgementCounter;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsReplayRebuild : BmsPlayerTestScene
{
    private BmsLayoutVariant layout;
    private bool scratchReplay;
    private bool disableFailure;
    private bool streaming;
    private bool emptyReplay;

    [SetUp]
    public void ResetReplayMode() => streaming = emptyReplay = false;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => streaming
        ? new StreamingPlayer(BmsTestSkins.CreateSkinSource(BmsTestSkins.SkinKind.Argon, this), createReplayFrames())
        : disableFailure
        ? new FailurePolicyPlayer(BmsTestSkins.CreateSkinSource(BmsTestSkins.SkinKind.Argon, this), _ => createReplayFrames())
        : CreateBmsPlayer(_ => createReplayFrames());

    private IList<ReplayFrame> createReplayFrames() => emptyReplay ? [] : scratchReplay ?
    [
        new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Scratch),
        new BmsReplayFrame(1400, BmsAction.ScratchReverse), new BmsReplayFrame(1500),
        new BmsReplayFrame(1600, BmsAction.Scratch), new BmsReplayFrame(2100, BmsAction.ScratchReverse),
        new BmsReplayFrame(3000), new BmsReplayFrame(3000, BmsAction.Scratch), new BmsReplayFrame(3010),
        new BmsReplayFrame(4000),
    ] :
    [
        new BmsReplayFrame(0),
        new BmsReplayFrame(600, BmsAction.Key1), new BmsReplayFrame(610),
        new BmsReplayFrame(1000, BmsAction.Key1),
        new BmsReplayFrame(1400, BmsAction.Key1, BmsAction.Key2), new BmsReplayFrame(1410, BmsAction.Key1),
        new BmsReplayFrame(1500), new BmsReplayFrame(1600, BmsAction.Key1),
        new BmsReplayFrame(2100, BmsAction.Key1, BmsAction.Key2), new BmsReplayFrame(2110, BmsAction.Key1),
        new BmsReplayFrame(3000), new BmsReplayFrame(3000, BmsAction.Key1), new BmsReplayFrame(3010),
        new BmsReplayFrame(4000),
    ];

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = layout,
            TotalColumns = layout == BmsLayoutVariant.Pms9K ? 9 : 8,
            Rank = 2,
            Total = 100,
            HitObjects =
            {
                new BmsLongNote { StartTime = 1000, Duration = 2000, Column = 1 },
                new BmsNote { StartTime = 1400, Column = 2 },
                new BmsLandmine { StartTime = 1800, Column = 1, LandmineDamagePercent = 1 },
                new BmsNote { StartTime = 2100, Column = 2 },
                new BmsNote { StartTime = 3000, Column = 1 },
                new BmsNote { StartTime = 20000, Column = 1 },
            },
        };
        if (scratchReplay)
            foreach (var note in beatmap.HitObjects.Where(note => note.Column == 1 && note.StartTime < 20000))
                note.Column = 0;
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void RebuildIsSilentAndContinuesWithTheSameRuntime(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values(BmsLayoutVariant.Bme7K, BmsLayoutVariant.Pms9K)] BmsLayoutVariant variant)
    {
        var snapshots = new Dictionary<double, string>();
        var rebuilds = 0;
        var judgements = 0;
        var nonConsuming = 0;
        AddStep("load replay", () =>
        {
            layout = variant;
            scratchReplay = false;
            disableFailure = false;
            Mod noteMode = mode switch
            {
                BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
                _ => new BmsModLongNote(),
            };
            LoadPlayer([noteMode, new BmsModAutoGauge()]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("observe notifications", () =>
        {
            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            score.NewJudgement += _ => judgements++;
            score.NonConsumingJudgementRegistered += _ => nonConsuming++;
            ((BmsDrawableRuleset)Player.DrawableRuleset).GameplayEvents.ReplaySeeked += () => rebuilds++;
        });

        double[] times = [-500, 0, 600, 1000, 1500, 1550, 1600, 1800, 2100, 2999, 3000, 3010, 4100];
        foreach (var time in times)
        {
            seek(time);
            AddStep($"capture {time}", () => snapshots[time] = snapshot());
        }

        foreach (var target in times.Reverse().Skip(1))
        {
            AddStep("reset notification counts", () => rebuilds = judgements = nonConsuming = 0);
            seek(target);
            AddAssert($"rebuild to {target} matches uninterrupted play", () => snapshot(), () => Is.EqualTo(snapshots[target]));
            AddAssert("one reconstruction without historical notifications", () => rebuilds == 1 && judgements == 0 && nonConsuming == 0);
            AddAssert("native HUD counters match rebuilt statistics",
                () => Player.HUDOverlay.ChildrenOfType<JudgementCountController>().Single().Counters.Select(count => count.ResultCount.Value).ToArray(),
                () => Is.EqualTo(Player.HUDOverlay.ChildrenOfType<JudgementCountController>().Single().Counters
                    .Select(count => count.Types.Sum(type => Player.ScoreProcessor.Statistics.GetValueOrDefault(type))).ToArray()));
            AddAssert("accuracy counters show the target without rolling", () => Player.HUDOverlay.ChildrenOfType<GameplayAccuracyCounter>()
                .All(counter => counter.DisplayedCount == counter.Current.Value));
            seek(4100);
            AddAssert("continued replay matches uninterrupted play", () => snapshot(), () => Is.EqualTo(snapshots[4100]));
        }
    }

    [Test]
    public void ScratchReversalKeepsTheSameResultsAfterRebuild(
        [Values(BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values] bool doubleTime)
    {
        AddStep("load scratch replay", () =>
        {
            layout = BmsLayoutVariant.Bme7K;
            scratchReplay = true;
            disableFailure = false;
            Mod noteMode = mode == BmsLongNoteMode.ChargeNote ? new BmsModChargeNote() : new BmsModHellChargeNote();
            LoadPlayer(doubleTime ? [noteMode, new BmsModDoubleTime()] : [noteMode]);
        });
        verifyRebuilds([0, 1000, 1400, 1500, 1600, 2100, 2999, 3000, 3010, 4100]);
    }

    [Test]
    public void RebuildRespectsThePlayersFailurePolicy()
    {
        AddStep("load replay with failure disabled", () =>
        {
            layout = BmsLayoutVariant.Bme7K;
            scratchReplay = false;
            disableFailure = true;
            LoadPlayer([new BmsModHazardGauge()]);
        });
        verifyRebuilds([500, 600, 1000, 4100]);
        AddAssert("depleted gauge retains the failure override", () =>
            ((BmsHealthProcessor)Player.HealthProcessor).HasEverFailed && !Player.HealthProcessor.HasFailed);
    }

    [Test]
    public void LargeSeeksReachTheTargetInOneUpdate(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values] bool scratch, [Values] bool doubleTime)
    {
        var snapshots = new Dictionary<double, string>();
        var seeks = 0;
        var judgements = 0;
        AddStep("load replay for direct seeks", () =>
        {
            layout = BmsLayoutVariant.Bme7K;
            scratchReplay = scratch;
            disableFailure = false;
            Mod noteMode = mode switch
            {
                BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
                _ => new BmsModLongNote(),
            };
            LoadPlayer(doubleTime ? [noteMode, new BmsModAutoGauge(), new BmsModDoubleTime()] : [noteMode, new BmsModAutoGauge()]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        double[] times = [-500, 0, 1000, 1400, 1500, 1550, 1600, 1800, 2100, 2999, 3000, 3010, 4000, 4100];
        foreach (var time in times)
        {
            AddStep($"advance normally to {time}", () => SeekReplayInFrames(time));
            AddStep("capture uninterrupted state", () => snapshots[time] = snapshot());
        }
        AddStep("observe seek notifications", () =>
        {
            ((BmsDrawableRuleset)Player.DrawableRuleset).GameplayEvents.ReplaySeeked += () => seeks++;
            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            score.NewJudgement += _ => judgements++;
            score.NonConsumingJudgementRegistered += _ => judgements++;
        });
        double[] targets = [1000, 3000, 1500, 4000, 0, 1800, 1550, 4100, -500, 2999, 1600, 3010];
        foreach (var time in targets)
        {
            AddStep($"jump directly to {time}", () =>
            {
                seeks = judgements = 0;
                Player.GameplayClockContainer.Stop();
                Player.GameplayClockContainer.Seek(time);
                Player.DrawableRuleset.UpdateSubTree();
                Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(time).Within(.001));
                Assert.That(snapshot(), Is.EqualTo(snapshots[time]));
                Assert.That(seeks, Is.EqualTo(1));
                Assert.That(judgements, Is.Zero);
            });
        }
        AddStep("continue normally after jumping", () => SeekReplayInFrames(4100));
        AddAssert("continued playback matches uninterrupted state", () => snapshot(), () => Is.EqualTo(snapshots[4100]));
    }

    [TestCase(1000)]
    [TestCase(4100)]
    public void RunningPlaybackAlsoSeeksInOneUpdate(double target)
    {
        var snapshots = new Dictionary<double, string>();
        AddStep("load running scratch replay", () =>
        {
            layout = BmsLayoutVariant.Bme7K;
            scratchReplay = true;
            disableFailure = false;
            LoadPlayer([new BmsModHellChargeNote(), new BmsModAutoGauge(), new BmsModDoubleTime()]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        foreach (var time in new[] { 1000d, 4100 })
        {
            AddStep("capture normal playback", () =>
            {
                SeekReplayInFrames(time);
                snapshots[time] = snapshot();
            });
        }
        AddStep("resume away from target", () =>
        {
            SeekReplayInFrames(target == 1000 ? 4100 : 500);
            Player.GameplayClockContainer.Start();
        });
        AddUntilStep("source clock running", () => Player.GameplayClockContainer.IsRunning);
        AddStep("seek without pausing", () =>
        {
            Player.GameplayClockContainer.Seek(target);
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(target).Within(.001));
            Assert.That(Player.GameplayClockContainer.IsPaused.Value, Is.False);
            Assert.That(snapshot(), Is.EqualTo(snapshots[target]));
            Player.GameplayClockContainer.Stop();
        });
    }

    [Test]
    public void EmptyReplaySeeksKeepScrollControlsAvailable()
    {
        AddStep("load empty replay", () =>
        {
            layout = BmsLayoutVariant.Bme7K;
            scratchReplay = disableFailure = false;
            emptyReplay = true;
            LoadPlayer([new BmsModAutoGauge()]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        foreach (var time in new[] { 4100d, 1000 })
        {
            AddStep($"seek empty replay to {time}", () =>
            {
                Player.GameplayClockContainer.Stop();
                Player.GameplayClockContainer.Seek(time);
                Player.DrawableRuleset.UpdateSubTree();
                Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(time));
                Playfield.ScrollController.SetConfiguredScrollSpeed(8);
                Playfield.OnPressed(new KeyBindingPressEvent<BmsAction>(new InputState(), BmsAction.IncreaseScrollSpeed));
                Assert.That(Playfield.ScrollSpeed, Is.GreaterThan(8));
            });
        }
    }

    [Test]
    public void StreamingSeeksMatchCompleteReplay(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode,
        [Values] bool scratch)
    {
        var snapshots = new Dictionary<double, string>();
        loadReplay(false);
        foreach (var time in new[] { 1000d, 1500, 3000, 4100 })
        {
            AddStep("capture complete replay", () =>
            {
                SeekReplayInFrames(time);
                snapshots[time] = snapshot();
            });
        }

        loadReplay(true);
        AddStep("seek beyond available frames", () =>
        {
            jump(4100, 1500);
            Assert.That(snapshot(), Is.EqualTo(snapshots[1500]));
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(1500));
            Assert.That(snapshot(), Is.EqualTo(snapshots[1500]));
        });
        AddStep("receive through first equal-time frame", () =>
        {
            var replay = ((StreamingPlayer)Player).Stream;
            foreach (var frame in createReplayFrames().Skip(replay.Frames.Count))
            {
                replay.Frames.Add(frame);
                if (frame.Time == 3000) break;
            }
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(3000));
            Assert.That(((BmsColumn)Playfield.Stage.Columns[scratch ? 0 : 1]).Judgements.PressedDirections, Is.Zero);
        });
        AddStep("receive another frame at the same timestamp", () =>
        {
            var replay = ((StreamingPlayer)Player).Stream;
            replay.Frames.Add(createReplayFrames()[replay.Frames.Count]);
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(3000));
            Assert.That(snapshot(), Is.EqualTo(snapshots[3000]));
        });
        AddStep("rewind and seek forward while streaming", () =>
        {
            jump(1000, 1000);
            Assert.That(snapshot(), Is.EqualTo(snapshots[1000]));
            jump(4100, 3000);
            Assert.That(snapshot(), Is.EqualTo(snapshots[3000]));
        });
        AddStep("finish receiving and reach the original target", () =>
        {
            var replay = ((StreamingPlayer)Player).Stream;
            replay.Frames.AddRange(createReplayFrames().Skip(replay.Frames.Count));
            replay.HasReceivedAllFrames = true;
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(4100));
            Assert.That(snapshot(), Is.EqualTo(snapshots[4100]));
        });

        void jump(double target, double expected)
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(target);
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(expected));
        }

        void loadReplay(bool stream)
        {
            AddStep("load replay", () =>
            {
                layout = BmsLayoutVariant.Bme7K;
                scratchReplay = scratch;
                disableFailure = false;
                streaming = stream;
                Mod noteMode = mode switch
                {
                    BmsLongNoteMode.ChargeNote => new BmsModChargeNote(),
                    BmsLongNoteMode.HellChargeNote => new BmsModHellChargeNote(),
                    _ => new BmsModLongNote(),
                };
                LoadPlayer([noteMode, new BmsModAutoGauge()]);
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        }
    }

    private void verifyRebuilds(double[] times)
    {
        var snapshots = new Dictionary<double, string>();
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        foreach (var time in times)
        {
            seek(time);
            AddStep($"capture {time}", () => snapshots[time] = snapshot());
        }
        foreach (var time in times.Reverse().Skip(1))
        {
            seek(time);
            AddAssert($"rebuild to {time} matches uninterrupted play", () => snapshot(), () => Is.EqualTo(snapshots[time]));
            seek(times[^1]);
            AddAssert("continued replay matches uninterrupted play", () => snapshot(), () => Is.EqualTo(snapshots[times[^1]]));
        }
    }

    private string snapshot()
    {
        var score = (BmsScoreProcessor)Player.ScoreProcessor;
        var health = (BmsHealthProcessor)Player.HealthProcessor;
        return JsonSerializer.Serialize(new
        {
            Statistics = score.Statistics.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key).ToArray(),
            Combo = score.Combo.Value,
            HighestCombo = score.HighestCombo.Value,
            TotalScore = score.TotalScore.Value,
            TotalScoreWithoutMods = score.TotalScoreWithoutMods.Value,
            Accuracy = score.Accuracy.Value,
            Rank = score.Rank.Value,
            score.ScoringJudgementEventCount,
            score.JudgementEvents,
            Gauges = health.GaugeHistory.ToArray(),
            health.CurrentGaugeStates,
            health.HasFailed,
            health.HasEverFailed,
            Columns = Playfield.Stage.Columns.Cast<BmsColumn>().Select(column => new
            {
                column.Judgements.PressedDirections,
                Holds = Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().Where(note => note.Column == column.Index)
                    .Select(column.Judgements.GetLongNoteState).ToArray(),
            }).ToArray(),
        });
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }

    private sealed partial class FailurePolicyPlayer(ISkinSource skin, Func<BmsBeatmap, IList<ReplayFrame>> replay)
        : BmsTestSkins.SkinnedTestPlayer(skin, replay)
    {
        protected override bool CheckModsAllowFailure() => false;
    }

    private sealed partial class StreamingPlayer(ISkinSource skin, IList<ReplayFrame> frames)
        : BmsTestSkins.SkinnedTestPlayer(skin, null)
    {
        internal Replay Stream { get; } = new() { HasReceivedAllFrames = false, Frames = [.. frames.TakeWhile(frame => frame.Time <= 1500)] };

        protected override void PrepareReplay() => DrawableRuleset.SetReplayScore(new Score { Replay = Stream });
    }
}
