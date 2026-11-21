using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using osu.Framework.Timing;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

internal static class BmsReplaySimulation
{
    internal static (IReadOnlyList<BmsJudgementEvent> Judgements, IReadOnlyList<GameplayHealthHistoryEvent> Gauges) Run(
        BmsBeatmap beatmap, Replay replay, ScoreInfo source, CancellationToken cancellationToken = default,
        IReadOnlyList<BmsGaugeStateSnapshot>? courseGaugeStates = null, BmsGaugeProfileFamily? courseGaugeProfileFamily = null,
        Action<ScoreState>? onScore = null, bool captureHealthHistory = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frames = replay.Frames.OfType<BmsReplayFrame>().OrderBy(frame => frame.Time).ToArray();
        if (!replay.HasReceivedAllFrames || frames.Length == 0 || frames.Any(frame => !double.IsFinite(frame.Time)))
            throw new ArgumentException(@"A complete BMS input replay with finite timestamps is required.", nameof(replay));

        var mods = source.Mods.Select(mod => mod.DeepClone()).ToArray();

        var manualClock = new ManualClock();
        var clock = new FramedClock(manualClock);

        using var score = new BmsScoreProcessor();
        score.Clock = clock;

        using var health = new BmsHealthProcessor();
        health.Clock = clock;
        health.Mods = mods;

        score.Mods.Value = mods;
        score.ApplyBeatmap(beatmap);
        foreach (var mod in mods.OfType<IApplicableToScoreProcessor>())
            mod.ApplyToScoreProcessor(score);
        health.ApplyBeatmap(beatmap);

        if (courseGaugeStates != null && health.Model is BmsGaugeController courseGauge)
        {
            courseGauge.ConfigureGaugeContext(true, courseGaugeProfileFamily);
            courseGauge.SetGaugeTypes(courseGaugeStates.Select(state => state.GaugeType), replaceExisting: true,
                profileFamilyOverride: courseGaugeProfileFamily, initialStates: courseGaugeStates);
        }
        else if (mods.OfType<BmsModAutoGauge>().FirstOrDefault() is { } autoGauge)
            autoGauge.ApplyToHealthProcessor(health);
        else
        {
            foreach (var mod in mods.OfType<BmsModGauge>())
                mod.ApplyToHealthProcessor(health);
        }

        var gauges = new List<GameplayHealthHistoryEvent>();
        var columnCount = score.EngineChart.Chart.LaneCount;
        var output = new SimulationOutput(score, health, publishResult);
        var engine = new BmsJudgementEngine(
            score.EngineChart,
            Enumerable.Range(0, columnCount).Select(index => new BmsJudgementColumnDefinition(index, output)).ToArray(),
            compiled =>
            {
                var configuration = health.CreateEngineHealth(compiled);
                publishResult(0);
                return configuration;
            }, session =>
            {
                score.ApplyEngineState(session);
                health.ApplyEngineState(session);
                onScore?.Invoke(session.Score);
            }
        );

        advanceTo(beatmap, replay, frames, mods, engine, columnCount, frames[^1].Time, time =>
        {
            manualClock.CurrentTime = time;
            clock.ProcessFrame();
        }, cancellationToken, null);

        return (score.JudgementEvents, gauges);

        void publishResult(double time)
        {
            onScore?.Invoke(score.EngineScore);
            if (captureHealthHistory && health.CaptureHistory(time) is { } snapshot)
                gauges.Add(snapshot);
        }
    }

    internal static void AdvanceTo(BmsBeatmap beatmap, Replay replay, IReadOnlyList<Mod> mods, BmsJudgementEngine engine,
                                   int columnCount, double targetTime, Action<double> updateClock, CancellationToken cancellationToken = default,
                                   BmsReplayPosition? resume = null)
        => advanceTo(beatmap, replay, replay.Frames.OfType<BmsReplayFrame>().OrderBy(frame => frame.Time).ToArray(),
            mods, engine, columnCount, targetTime, updateClock, cancellationToken, resume);

    private static void advanceTo(BmsBeatmap beatmap, Replay replay, BmsReplayFrame[] frames, IReadOnlyList<Mod> mods, BmsJudgementEngine engine,
                                  int columnCount, double targetTime, Action<double> updateClock, CancellationToken cancellationToken,
                                  BmsReplayPosition? resume)
    {
        var algorithm = BmsReplayFrame.GetAlgorithm(replay);
        var rates = mods.OfType<IApplicableToRate>().ToArray();
        var useManiaCadence = mods.Any(mod => mod is BmsModManiaProfile { UseLazer.Value: true });
        var pressed = new HashSet<BmsAction>();
        var nextInput = 0;
        var firstFrameTime = frames.Length > 0 ? frames[0].Time : 0;
        var start = useManiaCadence
            ? Math.Min(firstFrameTime, beatmap.HitObjects.FirstOrDefault()?.StartTime - 2000 ?? 0)
            : Math.Min(0, Math.Min(firstFrameTime, engine.EarliestInputTime));
        var time = resume?.Time ?? Math.Min(targetTime, start);
        var cadence = useManiaCadence ? new BmsManiaReplayCadence(start) : (BmsManiaReplayCadence?)null;
        var idleTime = cadence?.NextIdleTimeAfter(resume?.Time ?? double.NegativeInfinity) ?? double.PositiveInfinity;

        if (resume?.LastFrame is { } lastFrame)
        {
            nextInput = Array.IndexOf(frames, lastFrame) + 1;
            pressed.UnionWith(lastFrame.Actions);
        }

        var previousTime = time;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = frameAt(time);

            var firstInput = nextInput;
            while (nextInput < frames.Length && frames[nextInput].Time == time)
            {
                if (useManiaCadence && nextInput != firstInput)
                    break;

                var input = frames[nextInput++];
                if (pressed.SetEquals(input.Actions))
                    continue;

                var actions = input.Actions.ToHashSet();

                foreach (var action in pressed)
                {
                    if (!actions.Contains(action))
                        queue(action, false, input.Time);
                }

                foreach (var action in actions)
                {
                    if (!pressed.Contains(action))
                        queue(action, true, input.Time);
                }

                pressed = actions;
            }

            if (!useManiaCadence || time == idleTime || nextInput > firstInput)
                engine.ProcessFrame(frame, time - previousTime, algorithm, frameAt);

            if (time == idleTime)
                idleTime = cadence!.Value.NextIdleTimeAfter(time);
            // Aborted attempts also settle held tails at their recorded endpoint.
            if (replay.HasReceivedAllFrames && nextInput > firstInput && nextInput == frames.Length)
                engine.CompleteLongNotesAtEnd(frame, frameAt);

            if (time == targetTime && (nextInput == frames.Length || frames[nextInput].Time > time))
            {
                updateClock(targetTime);
                return;
            }

            previousTime = time;
            var nextTime = nextInput < frames.Length ? frames[nextInput].Time : double.PositiveInfinity;
            time = Math.Min(Math.Min(nextTime, idleTime), targetTime);
        }

        BmsFrameContext frameAt(double eventTime)
        {
            updateClock(eventTime);
            return new BmsFrameContext(eventTime, rates.Aggregate(1.0, (value, mod) => mod.ApplyToRate(eventTime, value)));
        }

        void queue(BmsAction action, bool down, double inputTime)
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(action, beatmap.LayoutVariant) is { } index && index < columnCount)
                engine.QueueInput(inputTime, index, down, BmsKeyBindingConfiguration.IsReverseScratch(action));
        }
    }

    internal sealed class SimulationOutput(BmsScoreProcessor score, BmsHealthProcessor health, Action<double> recordGauge) : IBmsJudgementOutput
    {
        public void ApplyMainResult(BmsHitObject note, HitResult type, in BmsFrameContext frame, bool suppressPenalty)
            => apply(new BmsJudgementResult(note, note.CreateJudgement()) { Type = type, SuppressPenalty = suppressPenalty }, frame);

        public void ApplyMainResult(BmsLongNote note, HitResult type, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame, bool suppressPenalty)
            => apply(new BmsLongNoteJudgementResult(note, note.CreateJudgement(), endpoints) { Type = type, SuppressPenalty = suppressPenalty }, frame);

        private void apply(BmsJudgementResult result, in BmsFrameContext frame)
        {
            result.SetTiming(frame.SimulationTime, frame.GameplayRate);
            result.EngineResult = frame.EngineResult;
            score.ApplyResult(result);
            health.ApplyResult(result);
            recordGauge(result is BmsLongNoteJudgementResult longNoteResult
                ? longNoteResult.EndpointResults[^1].ApplicationTime ?? result.TimeAbsolute
                : result.EventTime);
        }

        public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty)
        {
            health.ApplyResult(score.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty, frame.EngineResult!));
            // PMS releases apply after their grace period while retaining the key-up judgement time.
            recordGauge(endpoint.ApplicationTime ?? endpoint.EventTime);
        }

        public void ApplyBodyTick(in BmsFrameContext frame)
        {
            score.ApplyEngineScore(frame.EngineResult!.Score);
            health.ApplyEngineTick(frame.EngineResult);
            recordGauge(frame.SimulationTime);
        }

        public void ApplyNonConsumingResult(BmsHitObject note, HitResult result, in BmsFrameContext frame)
        {
            var engineResult = frame.EngineResult!;
            score.RegisterNonConsumingJudgement(result, frame.SimulationTime, note.StartTime, note.Column, engineResult);
            health.ApplyEngineTick(engineResult);
            recordGauge(frame.SimulationTime);
        }

        public void PublishState(BmsLongNote note, NoteSnapshot state, in BmsFrameContext frame)
        {
        }

        public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame)
        {
        }

        public void PlayKeySound(BmsHitObject note, KeySound sound)
        {
        }

        public void PlayLandmineSample(BmsLandmine note)
        {
        }
    }
}
