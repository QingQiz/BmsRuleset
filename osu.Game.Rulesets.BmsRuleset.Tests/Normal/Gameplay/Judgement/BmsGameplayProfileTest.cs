using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsGameplayProfileTest
{
    [Test]
    public void TestSelectedProfileAutoplayProducesMaximumScore()
    {
        using var play = new Play(new BmsLongNote { StartTime = 1000, Duration = 500, Column = 0, Kind = KeyNoteKind.ReleaseHold },
            new BmsNote { StartTime = 1500, Column = 1 },
            new BmsLongNote { StartTime = 2000, Duration = 500, Column = 1, Kind = KeyNoteKind.SustainHold });
        var replay = new BmsModAutoplay().CreateReplayData(play.Beatmap, play.Score.Mods.Value).Replay;
        replay.Frames.Add(new BmsReplayFrame(3000));
        var states = new List<ScoreState>();
        var result = BmsReplaySimulation.Run(play.Beatmap, replay, new ScoreInfo { Mods = play.Score.Mods.Value.ToArray() }, onScore: states.Add);
        Assert.That(replay.Frames.OfType<BmsReplayFrame>().Any(frame => frame.Actions.Contains(BmsAction.ScratchReverse)), Is.True);
        Assert.That(states[^1].Score, Is.EqualTo(play.Score.EngineChart.MaximumScore));
        Assert.That(states[^1].JudgedOpportunities, Is.EqualTo(5));
        Assert.That(states[^1].MaximumCombo, Is.EqualTo(5));
        Assert.That(result.Judgements.All(judgement => judgement.Result == HitResult.Perfect), Is.True);
    }

    [TestCase(15, 210, 15, 210)]
    public void SelectedProfilePublishesFrameworkHeadAndTailWindows(double headPerfect, double headMiss, double tailPerfect, double tailMiss)
    {
        var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 1000, Kind = KeyNoteKind.ReleaseHold };
        using var play = new Play(note);
        var compiledWindows = note.HitWindows;
        note.ApplyDefaults(play.Beatmap.ControlPointInfo, play.Beatmap.Difficulty);
        Assert.That(note.HitWindows, Is.SameAs(compiledWindows));
        Assert.That(note.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(headPerfect));
        Assert.That(note.HitWindows.WindowFor(HitResult.Miss), Is.EqualTo(headMiss));
        var results = new List<BmsJudgementResult>();
        play.Score.NewJudgement += result => results.Add((BmsJudgementResult)result);
        play.Press(1000);
        play.Advance(2100);
        var presentation = play.Score.EngineChart.Profile.LongNotePresentation(note, play.Engine.GetColumn(1).GetNote(note),
            new BmsLongNotePresentationContext(2100, true, play.Score.EngineChart.TimingFor(note)));
        Assert.That(presentation.KeepVisibleAfterHit, Is.True);
        Assert.That(presentation.LifetimeEnd, Is.GreaterThan(2000 + tailMiss));
        play.Release(2150);
        Assert.That(results, Has.Count.EqualTo(2));
        var tail = results[1];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tail.HitObject.StartTime, Is.EqualTo(2000));
            Assert.That(((BmsHitObject)tail.HitObject).Column, Is.EqualTo(1));
            Assert.That(((BmsHitObject)tail.HitObject).Beatmap, Is.SameAs(play.Beatmap));
            Assert.That(tail.HitObject.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(tailPerfect));
            Assert.That(tail.HitObject.HitWindows.WindowFor(HitResult.Miss), Is.EqualTo(tailMiss));
            Assert.That(tail.TimeOffset, Is.EqualTo(150));
            Assert.That(tail.EventTime, Is.EqualTo(2150));
        }
    }

    [TestCase(1, "bms-ruleset-v1")]
    public void TestBmsProfileOwnsGameplayRules(int scoringOpportunities, string profileId)
    {
        using var play = new Play(new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1, Kind = KeyNoteKind.Hold });
        Assert.That(play.Score.EngineChart.Configuration.ProfileId, Is.EqualTo(profileId));
        Assert.That(play.Score.EngineChart.Chart.ScorePlan.Opportunities, Has.Length.EqualTo(scoringOpportunities));
        play.Press(1000);
        play.Release(2000);
        play.Advance(3000);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(play.Score.TotalScore.Value, Is.EqualTo(1_000_000));
            Assert.That(play.Score.Accuracy.Value, Is.EqualTo(1));
            Assert.That(play.Score.Combo.Value, Is.EqualTo(scoringOpportunities));
            Assert.That(play.Score.ScoringJudgementEventCount, Is.EqualTo(scoringOpportunities));
            Assert.That(play.Health.CurrentHealth.Layers.IsEmpty, Is.False);
            Assert.That(play.Score.Outcome, Is.EqualTo(SessionOutcome.Cleared));
            Assert.That(play.Health.HasPassedAtEnd(), Is.True);
        }
    }

    [TestCase(SessionOutcome.Failed)]
    public void TestClearUsesSelectedHealthRules(SessionOutcome outcome)
    {
        using var play = new Play(new BmsNote { StartTime = 1000, Column = 1 });
        play.Advance(2000);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(play.Score.ScoringJudgementEventCount, Is.EqualTo(1));
            Assert.That(play.Score.TotalScore.Value, Is.Zero);
            Assert.That(play.Score.Accuracy.Value, Is.Zero);
            Assert.That(play.Score.Outcome, Is.EqualTo(outcome));
            Assert.That(play.Health.HasPassedAtEnd(), Is.EqualTo(outcome == SessionOutcome.Cleared));
            Assert.That(play.Score.Statistics.GetValueOrDefault(HitResult.Meh), Is.EqualTo(1));
        }
    }

    [Test]
    public void TestLiveStoredAndReplayScoresUseSelectedProfile()
    {
        using var play = new Play(new BmsNote { StartTime = 1000, Column = 1 },
            new BmsNote { StartTime = 2000, Column = 1 }, new BmsNote { StartTime = 3000, Column = 1 });
        play.Press(1000);
        var first = play.Score.EngineScore.Score;
        play.Release(1100);
        play.Press(2060);
        var second = play.Score.EngineScore.Score;
        play.Release(2100);
        play.Advance(4000);
        var source = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = play.Score.Mods.Value.ToArray() };
        play.Score.PopulateScore(source);
        var archived = BmsTestReplayArchive.RoundTrip(new Score
        {
            ScoreInfo = source,
            Replay =
            {
                Frames =
                [
                    new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1100),
                    new BmsReplayFrame(2060, BmsAction.Key1), new BmsReplayFrame(2100), new BmsReplayFrame(4000),
                ],
            },
        });
        var states = new List<ScoreState>();
        BmsReplaySimulation.Run(play.Beatmap, archived.Replay, archived.ScoreInfo, onScore: states.Add);
        var profile = GameplayProfile.FromMods(archived.ScoreInfo.Mods);
        var value = profile.StoredScore(archived.ScoreInfo)!.Value;
        var aggregate = BmsScoreAggregation.CreateScore([source, source], play.Beatmap.BeatmapInfo, source.Mods, true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(play.Score.EngineScore.Score));
            Assert.That(states[^1].Score, Is.EqualTo(value));
            Assert.That(BmsScoreGraph.ScoreAtProgress(value, states, 1, 3), Is.EqualTo(first));
            Assert.That(BmsScoreGraph.ScoreAtProgress(value, states, 2, 3), Is.EqualTo(second));
            Assert.That(BmsScoreGraph.ScoreAtProgress(value, states, 0, 3), Is.Zero);
            Assert.That(profile.StoredScore(aggregate), Is.EqualTo(2 * value));
            Assert.That(profile.StoredMaximumScore(aggregate), Is.EqualTo(12));
            Assert.That(profile.JudgementLabel(HitResult.Miss).ToString(), Is.EqualTo("E-POOR"));
            Assert.That(profile.RankLabel(ScoreRank.S).ToString(), Is.EqualTo("AAA"));
        }
    }

    private sealed class Play : IDisposable
    {
        internal BmsScoreProcessor Score { get; } = new();
        internal BmsHealthProcessor Health { get; } = new();
        internal BmsJudgementEngine Engine { get; }
        internal BmsBeatmap Beatmap { get; }
        private readonly ManualClock clock = new();
        private readonly FramedClock frameClock;

        internal Play(params BmsHitObject[] notes)
            : this([], notes)
        {
        }

        internal Play(Mod[] mods, params BmsHitObject[] notes)
        {
            var beatmap = Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
            beatmap.HitObjects.AddRange(notes);
            foreach (var note in notes)
                note.Beatmap = beatmap;
            foreach (var mod in mods.OfType<IApplicableAfterBeatmapConversion>())
                mod.ApplyToBeatmap(beatmap);
            frameClock = new FramedClock(clock);
            Score.Clock = Health.Clock = frameClock;
            Score.Mods.Value = mods;
            Health.Mods = mods;
            Score.ApplyBeatmap(beatmap);
            Health.ApplyBeatmap(beatmap);
            foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
                mod.ApplyToHealthProcessor(Health);
            var output = new BmsReplaySimulation.SimulationOutput(Score, Health, _ => { });
            Engine = new BmsJudgementEngine(Score.EngineChart,
                Enumerable.Range(0, 8).Select(column => new BmsJudgementColumnDefinition(column, output)).ToArray(), Health.CreateEngineHealth,
                session =>
                {
                    Score.ApplyEngineState(session);
                    Health.ApplyEngineState(session);
                });
        }

        internal void Press(double time)
        {
            Engine.QueueInput(time, 1, true, false);
            Advance(time);
        }

        internal void Release(double time)
        {
            Engine.QueueInput(time, 1, false, false);
            Advance(time);
        }

        internal void Advance(double time)
        {
            clock.CurrentTime = time;
            frameClock.ProcessFrame();
            Engine.ProcessFrame(new BmsFrameContext(time, 1), 0);
        }

        public void Dispose()
        {
            Score.Dispose();
            Health.Dispose();
        }
    }
}
