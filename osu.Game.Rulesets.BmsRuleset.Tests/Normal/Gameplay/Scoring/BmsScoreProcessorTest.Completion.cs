using NUnit.Framework;
using System.Linq;
using System;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring;

public partial class BmsScoreProcessorTest
{
    [Test]
    public void TestPracticeCompletionIncludesTrailingSilence()
    {
        var practice = BmsPracticeBeatmap.Create(createThreeNoteBeatmap(), new BmsPracticeSettings { StartTime = 0, EndTime = 2500 });
        using var play = new CompletionPlay(practice);
        foreach (var note in practice.HitObjects)
        {
            play.Input(note.StartTime, note.Column, true);
            play.Input(note.StartTime + 1, note.Column, false);
        }
        play.Advance(2499);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Advance(2500);
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestPracticeEndPreservesCompleteHoldAndSlowWindow(
        [Values(BmsLongNoteMode.LongNote, BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode)
    {
        var source = new BmsLongNote { Kind = mode.ToNoteKind(), StartTime = 1000, Duration = 1000, Column = 1 };
        var full = completionBeatmap(source);
        full.LockedLongNoteMode = mode;
        var practice = BmsPracticeBeatmap.Create(full, new BmsPracticeSettings { StartTime = 0, EndTime = 1500 });
        var hold = (BmsLongNote)practice.HitObjects.Single();
        using var play = new CompletionPlay(practice);
        play.Input(hold.StartTime, hold.Column, true);
        play.Advance(1500);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Input(hold.EndTime, hold.Column, false);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        var deadline = hold.EndTime + BmsEngineRules.GetTiming(hold, false).PassiveDeadline / 1000.0;
        play.Advance(deadline - 1);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Advance(deadline);
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestCompletionWaitsForFinalGaugeDamage([Values] bool autoGauge, [Values(1280, 1281, 2000)] double completionTime)
    {
        using var play = new CompletionPlay(completionBeatmap(new BmsNote { StartTime = 1000, Column = 1 }), autoGauge, .83);
        play.SetClock(completionTime);
        play.Processor.UpdateCompletion();
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Advance(completionTime + 1);
        Assert.Multiple(() =>
        {
            Assert.That(play.Processor.HasCompleted.Value, Is.True);
            Assert.That(play.Health.Health.Value, Is.LessThan(.8));
            Assert.That(play.Score.ScoreInfo.Passed, Is.EqualTo(autoGauge));
            Assert.That(play.Health.Gauge().WorstGaugeType, Is.EqualTo(autoGauge ? BmsGaugeType.AssistEasy : BmsGaugeType.Normal));
            Assert.That(play.Score.ScoreInfo.Rank == ScoreRank.F, Is.EqualTo(!autoGauge));
            Assert.That(play.Score.ScoreInfo.Mods.OfType<BmsModAssistEasyGauge>().Any(), Is.EqualTo(autoGauge));
            Assert.That(play.Health.Gauge().CurrentGaugeStates.Single(state => state.GaugeType == BmsGaugeType.Normal).Health, Is.EqualTo(.77).Within(1e-12));
        });
    }

    [Test]
    public void TestCompletionWaitsForChargeTail([Values(BmsLongNoteMode.ChargeNote, BmsLongNoteMode.HellChargeNote)] BmsLongNoteMode mode)
    {
        using var play = new CompletionPlay(completionBeatmap(new BmsLongNote { StartTime = 1000, Column = 1, Duration = 600, Kind = mode.ToNoteKind() }));
        play.Input(1000, 1, true);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Input(1600, 1, false);
        play.Advance(2000);
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestCompletionIgnoresEmptyPoorAndLandmineCounts([Values] bool detonateMine)
    {
        using var play = new CompletionPlay(completionBeatmap(new BmsLandmine { StartTime = 500, Column = 2 }, new BmsNote { StartTime = 1000, Column = 1 }));
        if (detonateMine)
            play.Input(500, 2, true);
        play.Input(600, 1, true);
        Assert.That(play.Processor.ScoringJudgementEventCount, Is.Zero);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Input(700, 1, false);
        play.Input(1000, 1, true);
        play.Advance(1500);
        Assert.That(play.Processor.ScoringJudgementEventCount, Is.EqualTo(1));
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestCompletionWithFinalLandmine([Values] bool autoGauge, [Values] bool detonateMine, [Values] bool includeNote)
    {
        var mine = new BmsLandmine { StartTime = 3000, Column = 2, LandmineDamagePercent = 10 };
        var beatmap = includeNote ? completionBeatmap(new BmsNote { StartTime = 1000, Column = 1 }, mine) : completionBeatmap(mine);
        using var play = new CompletionPlay(beatmap, autoGauge, includeNote ? .89 : .83);
        play.Advance(2999);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        if (detonateMine)
            play.Input(3000, 2, true);
        else
            play.Advance(3000);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        var deadline = mine.StartTime + BmsEngineRules.GetTiming(mine, false).PassiveDeadline / 1000.0;
        play.Advance(deadline - 1);
        Assert.That(play.Processor.HasCompleted.Value, Is.False);
        play.Advance(deadline);
        Assert.Multiple(() =>
        {
            Assert.That(play.Processor.ScoringJudgementEventCount, Is.EqualTo(includeNote ? 1 : 0));
            Assert.That(play.Processor.HasCompleted.Value, Is.True);
            Assert.That(play.Health.Health.Value, Is.EqualTo(detonateMine ? .73 : .83).Within(1e-12));
            Assert.That(play.Score.ScoreInfo.Passed, Is.EqualTo(!detonateMine || autoGauge));
            Assert.That(play.Health.Gauge().WorstGaugeType, Is.EqualTo(detonateMine && autoGauge ? BmsGaugeType.AssistEasy : BmsGaugeType.Normal));
        });
    }

    [Test]
    public void TestCompletionWithNoScoringNotes([Values] bool includeMine)
    {
        using var play = new CompletionPlay(includeMine ? completionBeatmap(new BmsLandmine { StartTime = 1000, Column = 1 }) : completionBeatmap());
        play.Advance(10000);
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
    }

    [Test]
    public void TestRewindDoesNotFinaliseScore()
    {
        using var play = new CompletionPlay(completionBeatmap(new BmsNote { StartTime = 1000, Column = 1 }));
        play.Input(1000, 1, true);
        play.Advance(2000);
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
        play.Engine.Reset();
        play.Processor.ResetReplayState();
        play.Health.ResetReplayState();
        play.Advance(0);
        Assert.Multiple(() =>
        {
            Assert.That(play.Processor.HasCompleted.Value, Is.False);
            Assert.That(play.Processor.Rank.Value, Is.Not.EqualTo(ScoreRank.F));
        });
        play.Input(1000, 1, true);
        play.Advance(2000);
        Assert.That(play.Processor.HasCompleted.Value, Is.True);
    }

    private static BmsBeatmap completionBeatmap(params BmsHitObject[] notes)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
        beatmap.HitObjects.AddRange(notes);
        foreach (var note in notes)
            note.Beatmap = beatmap;
        return beatmap;
    }

    private sealed class CompletionPlay : IDisposable
    {
        internal TestBmsScoreProcessor Processor { get; } = new();
        internal BmsHealthProcessor Health { get; } = new();
        internal Score Score { get; }
        internal BmsJudgementEngine Engine { get; }
        private readonly ManualClock clock = new();
        private readonly FramedClock framedClock;
        private readonly BmsGameplayCompletionController completion;

        internal CompletionPlay(BmsBeatmap beatmap, bool autoGauge = false, double? initialHealth = null, Mod[] selectedMods = null)
        {
            framedClock = new FramedClock(clock);
            Processor.Clock = Health.Clock = framedClock;
            Mod[] mods = selectedMods ?? (autoGauge ? [new BmsModAutoGauge()] : []);
            Processor.Mods.Value = mods;
            Health.Mods = mods;
            Processor.ApplyBeatmap(beatmap);
            Health.ApplyBeatmap(beatmap);
            foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
                mod.ApplyToHealthProcessor(Health);
            if (initialHealth is { } initial)
            {
                var types = autoGauge ? BmsModAutoGauge.AUTO_GAUGE_CHAIN : [BmsGaugeType.Normal];
                Health.Gauge().SetGaugeTypes(types, replaceExisting: true, initialStates: types.Select(type =>
                    new BmsGaugeStateSnapshot(type, type > BmsGaugeType.Normal ? 0 : initial, type > BmsGaugeType.Normal)).ToArray());
            }
            Score = new Score { ScoreInfo = { Mods = mods, Passed = true } };
            var gameplay = new GameplayState(beatmap, new BmsRuleset(), mods, Score, Processor, Health);
            completion = new BmsGameplayCompletionController(Processor, Health, gameplay, null, null);
            var output = new BmsReplaySimulation.SimulationOutput(Processor, Health, _ => { });
            Engine = new BmsJudgementEngine(Processor.EngineChart,
                Enumerable.Range(0, Processor.EngineChart.Chart.LaneCount).Select(column => new BmsJudgementColumnDefinition(column, output)).ToArray(), Health.CreateEngineHealth,
                session =>
                {
                    Processor.ApplyEngineState(session);
                    Health.ApplyEngineState(session);
                });
        }

        internal void SetClock(double time)
        {
            clock.CurrentTime = time;
            framedClock.ProcessFrame();
        }

        internal void Input(double time, int column, bool pressed)
        {
            Engine.QueueInput(time, column, pressed, false);
            Advance(time);
        }

        internal void Advance(double time)
        {
            SetClock(time);
            Engine.ProcessFrame(new BmsFrameContext(time, 1), 0);
            Processor.UpdateCompletion();
        }

        public void Dispose()
        {
            completion.Dispose();
            Processor.Dispose();
            Health.Dispose();
        }
    }
}
