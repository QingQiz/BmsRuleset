#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using ReplayData = osu.Game.Replays.Replay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsReplaySimulationTest
{
    [Test]
    public void RebuildsInputTimingAndPassiveMisses()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 2, StartTime = 2000 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1010, BmsAction.Key1), new BmsReplayFrame(1020)));

        Assert.That(result.Judgements.Count(entry => entry.Result == HitResult.Perfect), Is.EqualTo(1));
        Assert.That(result.Judgements.Count(entry => entry.Result == HitResult.Meh), Is.EqualTo(1));
        Assert.That(result.HitEvents[0].TimeOffset, Is.EqualTo(10));
        Assert.That(result.Gauges, Has.Count.EqualTo(2));
    }

    [TestCase(BmsLongNoteMode.LongNote, 1)]
    [TestCase(BmsLongNoteMode.ChargeNote, 2)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 2)]
    public void RebuildsLongNoteEndpoints(BmsLongNoteMode mode, int count)
    {
        var beatmap = createBeatmap(mode);
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(2000)));

        Assert.That(result.Judgements.Count(entry => entry.Result == HitResult.Perfect), Is.EqualTo(count));
        Assert.That(result.HitEvents, Has.Count.EqualTo(2));
        Assert.That(result.HitEvents.Select(entry => entry.TimeOffset), Is.All.EqualTo(0));
    }

    [Test]
    public void HellChargeTicksFollowElapsedTimeAcrossCompaction()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.HellChargeNote);
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 });
        var replay = createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1250, BmsAction.Key1),
            new BmsReplayFrame(1500, BmsAction.Key1), new BmsReplayFrame(1750, BmsAction.Key1), new BmsReplayFrame(2000));
        var compact = BmsTestReplayArchive.RoundTrip(new Score { Replay = replay });
        var original = simulate(beatmap, replay);
        var restored = simulate(beatmap, compact.Replay);
        var gauges = original.Gauges;
        var restoredGauges = restored.Gauges;
        Assert.That(restoredGauges.Select(entry => entry.Time), Is.EqualTo(gauges.Select(entry => entry.Time)));
        Assert.That(restoredGauges.SelectMany(entry => entry.States), Is.EqualTo(gauges.SelectMany(entry => entry.States)));
        Assert.That(gauges.Select(entry => entry.Time), Is.EqualTo(new[]
        {
            1000d, Math.BitIncrement(1200), Math.BitIncrement(1400), Math.BitIncrement(1600), Math.BitIncrement(1800), 2000,
        }));
    }

    [Test]
    public void HellChargeGaugeDoesNotDependOnUnchangedReplayFrames()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.HellChargeNote);
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000 });
        var inputs = new[]
        {
            new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1100),
            new BmsReplayFrame(1200, BmsAction.Key1), new BmsReplayFrame(1300),
            new BmsReplayFrame(1400, BmsAction.Key1), new BmsReplayFrame(3000),
        };
        var dense = new List<BmsReplayFrame>();
        for (var i = 0; i < inputs.Length - 1; i++)
        {
            dense.Add(inputs[i]);
            for (var time = inputs[i].Time + 1; time < inputs[i + 1].Time; time++)
                dense.Add(new BmsReplayFrame(time, inputs[i].Actions.ToArray()));
        }
        dense.Add(inputs[^1]);
        var sparseResult = simulate(beatmap, createReplay(inputs));
        var denseResult = simulate(beatmap, createReplay(dense.ToArray()));

        Assert.That(sparseResult.Gauges.Select(entry => entry.Time), Is.EqualTo(denseResult.Gauges.Select(entry => entry.Time)));
        Assert.That(sparseResult.Gauges.SelectMany(entry => entry.States), Is.EqualTo(denseResult.Gauges.SelectMany(entry => entry.States)));
        Assert.That(sparseResult.Gauges.Select(entry => entry.Time), Is.EqualTo(new[]
        {
            1000d, 1100, Math.BitIncrement(1600), Math.BitIncrement(1800), Math.BitIncrement(2000),
            Math.BitIncrement(2200), Math.BitIncrement(2400), Math.BitIncrement(2600), Math.BitIncrement(2800),
        }));
    }

    [Test]
    public void PassiveMissesUseTheirDeadlineWithoutRecordedUpdates()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.HellChargeNote);
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 2, StartTime = 1000 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(0), new BmsReplayFrame(1000), new BmsReplayFrame(1500)));
        var deadline = Math.BitIncrement(1000 + BmsJudgementProfileProvider.GetTable(beatmap.LayoutVariant, 1, beatmap.Rank, false).SlowWindowFor(HitResult.Ok));
        Assert.That(result.Judgements.SelectMany(entry => entry.TimingObservations).Single(observation => observation.Kind == BmsTimingObservationKind.LongNoteHead).ActualTime,
            Is.EqualTo(deadline));
    }

    [Test]
    public void PreservesReleaseThenPressAtTheSameTime()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1100 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1100), new BmsReplayFrame(1100, BmsAction.Key1)));
        Assert.That(result.Judgements.Count(entry => entry.Result == HitResult.Perfect), Is.EqualTo(2));
    }

    [Test]
    public void DetectsHeldKeyLandmines()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1100, LandmineDamagePercent = 20 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1200)));
        Assert.That(result.Judgements.Count(entry => entry.Source.Kind == BmsJudgementSourceKind.Landmine), Is.EqualTo(1));
    }

    [TestCase(1100, 0)]
    [TestCase(1100.001, 1)]
    public void LandmineUsesTheStateAtItsTimestamp(double releaseTime, int hits)
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1100, LandmineDamagePercent = 20 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(releaseTime)));
        Assert.That(result.Judgements.Count(entry => entry.Source.Kind == BmsJudgementSourceKind.Landmine), Is.EqualTo(hits));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void JudgementsAndGaugeOrderingDoNotDependOnIdleFrames(BmsLongNoteMode mode)
    {
        var beatmap = createBeatmap(mode);
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000 });
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 2, StartTime = 1050, Duration = 1800 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 3, StartTime = 1600 });
        beatmap.HitObjects.Add(new BmsLandmine { Beatmap = beatmap, Column = 1, StartTime = 1500, LandmineDamagePercent = 20 });
        var inputs = new[]
        {
            new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1050, BmsAction.Key1, BmsAction.Key2),
            new BmsReplayFrame(1600, BmsAction.Key2), new BmsReplayFrame(1700, BmsAction.Key1, BmsAction.Key2),
            new BmsReplayFrame(2850, BmsAction.Key1), new BmsReplayFrame(3000),
        };
        var sparse = simulate(beatmap, createReplay(inputs));
        var dense = new List<BmsReplayFrame>();
        for (var i = 0; i < inputs.Length; i++)
        {
            dense.Add(inputs[i]);
            if (i + 1 == inputs.Length) continue;
            for (var time = inputs[i].Time + 17.25; time < inputs[i + 1].Time; time += 17.25)
                dense.Add(new BmsReplayFrame(time, inputs[i].Actions.ToArray()));
        }

        var sampled = simulate(beatmap, createReplay(dense.ToArray()));
        Assert.That(sampled.Judgements.Select(entry => (entry.Source, entry.Result, entry.SuppressPenalty)),
            Is.EqualTo(sparse.Judgements.Select(entry => (entry.Source, entry.Result, entry.SuppressPenalty))));
        Assert.That(sampled.Judgements.SelectMany(entry => entry.TimingObservations), Is.EqualTo(sparse.Judgements.SelectMany(entry => entry.TimingObservations)));
        Assert.That(sampled.Gauges.Select(entry => (entry.Time, entry.ActiveGaugeType)), Is.EqualTo(sparse.Gauges.Select(entry => (entry.Time, entry.ActiveGaugeType))));
        Assert.That(sampled.Gauges.SelectMany(entry => entry.States), Is.EqualTo(sparse.Gauges.SelectMany(entry => entry.States)));
    }

    [Test]
    public void ReopeningDetailsRecalculatesWithoutMutatingTheScore()
    {
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        beatmap.HitObjects.Add(note);
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        var source = new ScoreInfo { Passed = true, TotalScore = 123456, MaxCombo = 17 };
        source.Statistics[HitResult.Great] = 42;
        var replay = createReplay(new BmsReplayFrame(1010, BmsAction.Key1));
        var originalHitEvents = source.HitEvents;
        var first = BmsResultStatisticsData.Load(source, beatmap, replay, default)!;
        replay.Frames[0].Time = 1020;
        var second = BmsResultStatisticsData.Load(source, beatmap, replay, default)!;
        Assert.That(first.HitEvents.Single().TimeOffset, Is.EqualTo(10));
        Assert.That(second.HitEvents.Single().TimeOffset, Is.EqualTo(20));
        Assert.That(source.HitEvents, Is.SameAs(originalHitEvents).And.Empty);
        Assert.That(source.TotalScore, Is.EqualTo(123456));
        Assert.That(source.MaxCombo, Is.EqualTo(17));
        Assert.That(source.Statistics[HitResult.Great], Is.EqualTo(42));
    }

    [Test]
    public void RecalculatesTimingStatisticsWithoutMutatingSavedHitEvents()
    {
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        beatmap.HitObjects.Add(note);
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        var source = new ScoreInfo { HitEvents = [new HitEvent(42, 1, HitResult.Great, note, null, null)] };
        var saved = source.HitEvents;
        var replay = createReplay(new BmsReplayFrame(1010, BmsAction.Key1));
        var data = BmsResultStatisticsData.Load(source, beatmap, replay, default)!;
        Assert.That(source.HitEvents, Is.SameAs(saved));
        Assert.That(data.HitEvents.Single().TimeOffset, Is.EqualTo(10));
        Assert.That(data.Gauges, Is.Not.Empty);
    }

    [Test]
    public void CourseChartsConsumeSimulatedDataWithoutWritingStageScores()
    {
        var beatmap = createBeatmap();
        var note = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        beatmap.HitObjects.Add(note);
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        var source = new ScoreInfo { Passed = true };
        var stage = BmsResultStatisticsData.Load(source, beatmap, createReplay(new BmsReplayFrame(1010, BmsAction.Key1)), default)!;
        var unplayed = createBeatmap();
        unplayed.HitObjects.Add(new BmsNote { Beatmap = unplayed, Column = 2, StartTime = 3000 });
        var course = BmsResultStatisticsData.CreateCourse(
            [(beatmap, stage), (unplayed, BmsResultStatisticsData.Create(new ScoreInfo(), unplayed))]);

        Assert.That(course.HitEvents.Single().TimeOffset, Is.EqualTo(10));
        Assert.That(course.StageBoundaries.Single(), Is.EqualTo(1010f / 4010).Within(0.001));
        Assert.That(course.Gauges.Single().Segments, Has.Count.EqualTo(1));
        Assert.That(course.Timeline.Notes.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(2));
        Assert.That(course.Timeline.Judgements.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(1));
        Assert.That(course.Scatter.Overall.Points, Has.Count.EqualTo(1));
        Assert.That(source.HitEvents, Is.Empty);
    }

    [Test]
    public void AutomaticLongNoteTailPrecedesALaterRelease()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(990, BmsAction.Key1), new BmsReplayFrame(2000.1)));
        Assert.That(result.HitEvents.Select(entry => entry.TimeOffset), Is.All.EqualTo(-10));
    }

    [Test]
    public void PauseMetadataDoesNotAlterInputJudgements()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 });
        var replay = createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(2000));
        var source = new ScoreInfo();
        source.Pauses.Add(1200);
        var paused = simulate(beatmap, replay, source);
        var uninterrupted = simulate(beatmap, replay, new ScoreInfo());
        Assert.That(JsonSerializer.Serialize(paused.Judgements), Is.EqualTo(JsonSerializer.Serialize(uninterrupted.Judgements)));
        Assert.That(JsonSerializer.Serialize(paused.Gauges), Is.EqualTo(JsonSerializer.Serialize(uninterrupted.Gauges)));
    }

    [Test]
    public void RestoresTheInitialCourseGaugeState()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        var source = new ScoreInfo
        {
            Passed = true,
            Mods =
            [
                new BmsModClassGauge
                {
                    GaugeProfileFamily = { Value = BmsGaugeProfileFamily.Lr2 },
                    InitialHealth = { Value = 0.4 },
                },
            ],
        };
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1)), source);
        var gauges = result.Gauges;
        Assert.That(gauges[0].States.Single().Health, Is.EqualTo(0.4));
        Assert.That(gauges[^1].States.Single().Health, Is.GreaterThan(0.4));
    }

    [TestCase(BmsJudgementAlgorithm.Combo, 1000, HitResult.Good)]
    [TestCase(BmsJudgementAlgorithm.Duration, 1100, HitResult.Perfect)]
    [TestCase(BmsJudgementAlgorithm.Lowest, 1000, HitResult.Good)]
    [TestCase(BmsJudgementAlgorithm.Score, 1100, HitResult.Perfect)]
    public void UsesTheArchivedAlgorithm(BmsJudgementAlgorithm algorithm, double expectedTime, HitResult expectedResult)
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1100 });
        var original = new Score
        {
            Replay = createReplay(new BmsReplayFrame(0) { Algorithm = algorithm }, new BmsReplayFrame(1100, BmsAction.Key1)),
        };
        var restored = BmsTestReplayArchive.RoundTrip(original.DeepClone());
        var result = simulate(beatmap, restored.Replay, restored.ScoreInfo);
        Assert.That(result.Judgements.Single(entry => entry.Source.StartTime == expectedTime).Result, Is.EqualTo(expectedResult));
    }

    [Test]
    public void RestoresSurvivingCourseAutoGaugesWithoutApplyingTheAttributedGauge()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        var source = new ScoreInfo
        {
            Mods =
            [
                new BmsModAutoGauge
                {
                    ClassHealth = { Value = 0.8 },
                    ExClassHealth = { Value = 0.4 },
                    GaugeProfileFamily = { Value = BmsGaugeProfileFamily.Lr2 },
                },
                new BmsModClassGauge(),
            ],
        };
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1)), source);
        Assert.That(result.Gauges[0].States, Is.EqualTo(new[]
        {
            new BmsGaugeStateSnapshot(BmsGaugeType.ExClass, 0.4, false),
            new BmsGaugeStateSnapshot(BmsGaugeType.Class, 0.8, false),
        }));
        Assert.That(result.Gauges[^1].States, Has.Count.EqualTo(2));
    }

    [Test]
    public void FailedAttemptStopsAtItsLastRecordedFrame()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 5000 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1200)), new ScoreInfo { Passed = false });
        Assert.That(result.HitEvents, Has.Count.EqualTo(1));
    }

    [TestCase(BmsLongNoteMode.LongNote, 1, HitResult.Ok)]
    [TestCase(BmsLongNoteMode.ChargeNote, 1, HitResult.Meh)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 1, HitResult.Meh)]
    [TestCase(BmsLongNoteMode.ChargeNote, 0, HitResult.Meh)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 0, HitResult.Meh)]
    public void AbortedHoldSettlesAtTheTerminalTimeWithoutPauseMetadata(BmsLongNoteMode mode, int column, HitResult tailResult)
    {
        var beatmap = createBeatmap(mode);
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = column, StartTime = 3000, Duration = 1000 });
        var action = column == 0 ? BmsAction.Scratch : BmsAction.Key1;
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(3000, action), new BmsReplayFrame(3500.375, action)), new ScoreInfo());
        var tail = result.Judgements.SelectMany(entry => entry.TimingObservations).Single(entry => entry.Kind == BmsTimingObservationKind.LongNoteTail);

        Assert.That(tail.ActualTime, Is.EqualTo(3500.375));
        Assert.That(tail.Result, Is.EqualTo(tailResult));
        Assert.That(result.Gauges[^1].Time, Is.EqualTo(3500.375));
    }

    [Test]
    public void SimultaneousHellChargeTicksKeepTheOrdinaryLaneBeforeScratch()
    {
        var beatmap = createBeatmap(BmsLongNoteMode.HellChargeNote);
        beatmap.Total = 1000;
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 0, StartTime = 1000, Duration = 3000 });
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 3000 });
        var source = new ScoreInfo { Mods = [new BmsModHardGauge { InitialHealth = { Value = .0459 } }] };
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1000, BmsAction.Scratch, BmsAction.Key1),
            new BmsReplayFrame(1100, BmsAction.Key1), new BmsReplayFrame(1401, BmsAction.Key1)), source);
        var beforeStop = result.Gauges.Where(entry => entry.Time < 1401).Last().States.Single();

        Assert.That(beforeStop.Failed, Is.False);
        Assert.That(beforeStop.Health, Is.EqualTo(.0001).Within(1e-12));
    }

    [Test]
    public void AttemptEndUsesTheTerminalBoundaryWithoutCompletingTheChart()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 2, StartTime = 2000 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 3, StartTime = 5000 });
        var replay = createReplay(new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(3000, BmsAction.Key1));
        var result = simulate(beatmap, replay, new ScoreInfo { Passed = false });
        Assert.That(result.Judgements.Select(entry => entry.Source.Column), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void UsesTheScoresRateAndWindowMods()
    {
        var beatmap = createBeatmap();
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
        var result = simulate(beatmap, createReplay(new BmsReplayFrame(1080, BmsAction.Key1)),
            new ScoreInfo { Passed = true, Ruleset = new BmsRuleset().RulesetInfo, Mods = [new BmsModDoubleTime(), new BmsModNoGood()] });
        Assert.That(result.HitEvents.Single().Result, Is.EqualTo(HitResult.Ok));
        Assert.That(result.HitEvents.Single().TimeOffset, Is.EqualTo(80));
        Assert.That(result.HitEvents.Single().GameplayRate, Is.EqualTo(1.5));
    }

    [Test]
    public void RejectsIncompleteReplaysAndSupportsCancellation()
    {
        var replay = createReplay(new BmsReplayFrame(0));
        replay.HasReceivedAllFrames = false;
        Assert.Throws<ArgumentException>(() => BmsReplaySimulation.Run(createBeatmap(), replay, new ScoreInfo()));
        Assert.Throws<OperationCanceledException>(() => BmsReplaySimulation.Run(createBeatmap(), replay, new ScoreInfo(), new CancellationToken(true)));
    }

    private static BmsBeatmap createBeatmap(BmsLongNoteMode mode = BmsLongNoteMode.LongNote) => new()
    {
        LayoutVariant = BmsLayoutVariant.Bme7K,
        TotalColumns = 8,
        Rank = 2,
        LockedLongNoteMode = mode,
    };

    private static (IReadOnlyList<BmsJudgementEvent> Judgements, IReadOnlyList<BmsGaugeHistoryEvent> Gauges, List<HitEvent> HitEvents)
        simulate(BmsBeatmap beatmap, ReplayData replay, ScoreInfo? source = null)
    {
        foreach (var note in beatmap.HitObjects)
            note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        if (source == null)
        {
            var last = (BmsReplayFrame)replay.Frames[^1];
            var endTime = beatmap.HitObjects.Select(note => note.GetEndTime() + 1000).DefaultIfEmpty(last.Time).Max();
            if (last.Time < endTime)
                replay.Frames.Add(new BmsReplayFrame(endTime, last.Actions.ToArray()));
        }
        var result = BmsReplaySimulation.Run(beatmap, replay, source ?? new ScoreInfo { Passed = true });
        return (result.Judgements, result.Gauges, BmsJudgementEventProjection.CreateTimingHitEvents(result.Judgements));
    }

    private static ReplayData createReplay(params BmsReplayFrame[] frames) => new()
    {
        HasReceivedAllFrames = true,
        Frames = frames.Cast<osu.Game.Rulesets.Replays.ReplayFrame>().ToList(),
    };
}
