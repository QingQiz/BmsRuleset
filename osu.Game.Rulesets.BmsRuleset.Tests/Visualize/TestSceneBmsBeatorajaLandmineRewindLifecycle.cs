#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsBeatorajaLandmineRewindLifecycle : BmsPlayerTestScene
{
    private readonly List<Action> assertions = [];
    private BmsReplayFrame[] replayFrames = [];

    // Sparse replay frames must preserve logical mine and body deadlines during catch-up.
    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ => replayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 12,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 2000, Column = 1 },
                new BmsLandmine { StartTime = 3201, Column = 2, LandmineDamagePercent = 10 },
                new BmsNote { StartTime = 10000, Column = 3 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false, 3200)]
    [TestCase(true, 3200)]
    [TestCase(false, 3205)]
    [TestCase(true, 3205)]
    public void MineApplicationCanReplayAcrossLifetime(bool returnToPool, double rewindTime)
    {
        AddStep("load mine lifecycle scenario", () =>
        {
            replayFrames = [new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Key1, BmsAction.Key2),
                new BmsReplayFrame(5000), new BmsReplayFrame(11000)];
            assertions.Clear();
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);

        // NORMAL starts at 20%; TOTAL12 / 3 gives head +4%, then body +2%.
        // The body deadline precedes the held mine's 10% deduction: 24 -> 26 -> 16.
        checkpoint("before", 3200, .24, false, []);
        checkpoint("first", 3210, .16, true, [.26, .16]);
        if (returnToPool)
        {
            seek(3500);
            AddAssert("mine returned to pool", () => !Playfield.AllColumnAliveObjects().Any(d => d.HitObject is BmsLandmine));
        }
        else
            AddAssert("mine remains resident", () => Playfield.AllColumnAliveObjects().Any(d => d.HitObject is BmsLandmine));

        // Rewinding before the logical crossing undoes it; later targets retain it,
        // independently of the outer frame that first displayed the result.
        checkpoint("rewound", rewindTime, rewindTime < 3201 ? .24 : .16, rewindTime >= 3201, []);
        checkpoint("replayed", 3210, .16, true, [.26, .16]);
        AddStep("assert captured lifecycle", () => Assert.Multiple(() =>
        {
            foreach (var assertion in assertions)
                assertion();
        }));
    }

    [TestCase(false, false, 3200)]
    [TestCase(true, true, 3200)]
    [TestCase(false, false, 3205)]
    [TestCase(false, false, 3210)]
    [TestCase(false, false, 3225)]
    [TestCase(false, true, 3225)]
    [TestCase(true, false, 3210)]
    [TestCase(true, false, 3225)]
    [TestCase(true, true, 3225)]
    public void HandledBoundaryAndPoolPreserveCrossing(bool held, bool returnToPool, double rewindTime)
    {
        AddStep("load handled mine boundary scenario", () =>
        {
            replayFrames = [new BmsReplayFrame(0), held
                ? new BmsReplayFrame(3000, BmsAction.Key1, BmsAction.Key2)
                : new BmsReplayFrame(3000, BmsAction.Key1),
                new BmsReplayFrame(3220, BmsAction.Key1, BmsAction.Key2),
                new BmsReplayFrame(5000), new BmsReplayFrame(11000)];
            assertions.Clear();
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);

        // NORMAL initial 20% + TOTAL12/3 head 4% + half-GREAT body 2% is 26%.
        // Only a mine held at the crossing deducts 10%; a later press cannot add it.
        var passedHealth = held ? .16 : .26;
        double[] firstFrame = held ? [.26, .16] : [.26];
        checkpoint("before", 3200, .24, false, []);
        checkpoint("first", 3210, passedHealth, held, firstFrame);
        checkpoint("later press", 3240, passedHealth, held, []);
        if (returnToPool)
        {
            seek(3500);
            AddAssert("mine returned to pool", () => !Playfield.AllColumnAliveObjects().Any(d => d.HitObject is BmsLandmine));
        }
        else
            AddAssert("mine remains resident", () => Playfield.AllColumnAliveObjects().Any(d => d.HitObject is BmsLandmine));

        // Avoided mines retain their logical crossing across rewinds and pooling too.
        checkpoint("rewound", rewindTime, rewindTime < 3201 ? .24 : passedHealth,
            rewindTime >= 3201 && held, rewindTime == 3210 ? firstFrame : []);
        if (rewindTime < 3201)
            checkpoint("re-cross", 3210, passedHealth, held, firstFrame);
        checkpoint("continued", 3240, passedHealth, held, []);
        AddStep("assert captured crossing boundaries", () => Assert.Multiple(() =>
        {
            foreach (var assertion in assertions)
                assertion();
        }));
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private void checkpoint(string phase, double time, double expectedHealth, bool expectedMine, double[] expectedFrameHealth)
    {
        seek(time);
        AddStep("capture " + phase, () =>
        {
            var label = phase + " at " + time;
            var health = Player.HealthProcessor.Health.Value;
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.ToArray();
            var mines = events.Count(e => e.Source.Kind == BmsJudgementSourceKind.Landmine);
            var observations = events.SelectMany(e => e.TimingObservations).ToArray();
            var history = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory.ToArray();
            var interval = history.Where(e => e.Time > (time == 3210 ? 3200 : 3201) && e.Time <= time).ToArray();
            var frameHealth = interval
                .Select(e => e.States.Single(s => s.GaugeType == BmsGaugeType.Normal).Health).ToArray();
            HitResult[] expectedResults = expectedMine ? [HitResult.Perfect, HitResult.Meh] : [HitResult.Perfect];
            BmsJudgementSourceKind[] expectedKinds = expectedMine
                ? [BmsJudgementSourceKind.LongNote, BmsJudgementSourceKind.Landmine] : [BmsJudgementSourceKind.LongNote];
            int[] expectedColumns = expectedMine ? [1, 2] : [1];
            double[] expectedSourceTimes = expectedMine ? [3000, 3201] : [3000];
            double[] expectedActualTimes = expectedMine ? [3000, 3201] : [3000];
            double[] expectedOffsets = expectedMine ? [0, 0] : [0];
            double[] expectedDamage = expectedMine ? [0, 10] : [0];
            BmsTimingObservationKind[] expectedTimingKinds = expectedMine
                ? [BmsTimingObservationKind.LongNoteHead, BmsTimingObservationKind.Note] : [BmsTimingObservationKind.LongNoteHead];
            TestContext.WriteLine($"{label}: health={health:R}; mines={mines}; frame=[{string.Join(",", frameHealth)}]; "
                                  + string.Join("; ", events.Select(e => $"{e.Result}/{e.Source.Kind}/col{e.Source.Column}/start{e.Source.StartTime}: "
                                      + string.Join(",", e.TimingObservations.Select(o => $"{o.Kind}/expected{o.ExpectedTime}/actual{o.ActualTime}/offset{o.TimeOffset}")))));

            // Capture every checkpoint before asserting so a first failure cannot hide rewind evidence.
            assertions.Add(() =>
            {
                Assert.That(health, Is.EqualTo(expectedHealth).Within(1e-8), "health " + label);
                Assert.That(mines, Is.EqualTo(expectedMine ? 1 : 0), "mine event count " + label);
                Assert.That(frameHealth, Is.EqualTo(expectedFrameHealth).Within(1e-8), "ordered NORMAL gauge history " + label);
                if (time == 3210)
                    Assert.That(interval.Select(e => e.Time), Is.EqualTo(expectedMine
                        ? new[] { Math.BitIncrement(3200), 3201 } : [Math.BitIncrement(3200)]));
                Assert.That(history.Any(e => e.Time > time), Is.False, "no future gauge history " + label);
                Assert.That(events.Select(e => e.Result), Is.EqualTo(expectedResults), "results, without extra endpoints " + label);
                Assert.That(events.Select(e => e.Source.Kind), Is.EqualTo(expectedKinds), "source kinds " + label);
                Assert.That(events.Select(e => e.Source.Column), Is.EqualTo(expectedColumns), "source columns " + label);
                Assert.That(events.Select(e => e.Source.StartTime), Is.EqualTo(expectedSourceTimes), "source times " + label);
                Assert.That(events.Select(e => e.Source.LandmineDamagePercent), Is.EqualTo(expectedDamage), "mine damage " + label);
                Assert.That(events.All(e => !e.SuppressPenalty && e.TimingObservations.Count == 1), Is.True, "one unsuppressed observation per event " + label);
                Assert.That(observations.Select(o => o.Kind), Is.EqualTo(expectedTimingKinds), "timing kinds " + label);
                Assert.That(observations.Select(o => o.Result), Is.EqualTo(expectedResults), "timing results " + label);
                Assert.That(observations.Select(o => o.ExpectedTime), Is.EqualTo(expectedSourceTimes), "expected times " + label);
                Assert.That(observations.Select(o => o.ActualTime), Is.EqualTo(expectedActualTimes), "actual times " + label);
                Assert.That(observations.Select(o => o.TimeOffset), Is.EqualTo(expectedOffsets), "time offsets " + label);
            });
        });
    }
}
