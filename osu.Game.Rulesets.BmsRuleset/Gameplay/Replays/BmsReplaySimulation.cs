using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Timing;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

internal static class BmsReplaySimulation
{
    internal static (IReadOnlyList<BmsJudgementEvent> Judgements, IReadOnlyList<BmsGaugeHistoryEvent> Gauges) Run(
        BmsBeatmap beatmap, Replay replay, ScoreInfo source, CancellationToken cancellationToken = default,
        IReadOnlyList<BmsGaugeStateSnapshot>? courseGaugeStates = null, BmsGaugeProfileFamily? courseGaugeProfileFamily = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frames = replay.Frames.OfType<BmsReplayFrame>().OrderBy(frame => frame.Time).ToArray();
        if (!replay.HasReceivedAllFrames || frames.Length == 0 || frames.Any(frame => !double.IsFinite(frame.Time)))
            throw new ArgumentException(@"A complete BMS input replay with finite timestamps is required.", nameof(replay));

        var mods = source.Mods.Select(mod => mod.DeepClone()).ToArray();
        using var windows = BmsJudgementProfileProvider.UseWindowMods(mods.OfType<IApplicableToJudgementWindow>().ToArray());
        var manualClock = new ManualClock();
        var clock = new FramedClock(manualClock);
        using var score = new BmsScoreProcessor { Clock = clock };
        using var health = new BmsHealthProcessor { Clock = clock };
        score.Mods.Value = mods;
        score.ApplyBeatmap(beatmap);
        health.ApplyBeatmap(beatmap);
        var algorithm = BmsReplayFrame.GetAlgorithm(replay);
        if (courseGaugeStates != null)
        {
            health.ConfigureGaugeContext(true, courseGaugeProfileFamily);
            health.SetGaugeTypes(courseGaugeStates.Select(state => state.GaugeType), replaceExisting: true,
                profileFamilyOverride: courseGaugeProfileFamily, initialStates: courseGaugeStates);
        }
        else if (mods.OfType<BmsModAutoGauge>().FirstOrDefault() is { } autoGauge)
            autoGauge.ApplyToHealthProcessor(health);
        else
            foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
                mod.ApplyToHealthProcessor(health);

        var objects = beatmap.HitObjects.Where(note => note is BmsNote or BmsLongNote or BmsLandmine).OrderBy(note => note.StartTime).ToArray();
        var columnCount = Math.Max(beatmap.TotalColumns, objects.Select(note => note.Column + 1).DefaultIfEmpty(1).Max());
        var engine = new BmsJudgementEngine(Enumerable.Range(0, columnCount).Select(index =>
        {
            var notes = objects.Where(note => note.Column == index).ToArray();
            return new BmsJudgementColumnDefinition(notes, beatmap.LayoutVariant, index, new SimulationOutput(score, health));
        }).ToArray());

        var rates = mods.OfType<IApplicableToRate>().ToArray();
        var pressed = new HashSet<BmsAction>();
        var nextInput = 0;
        var endTime = frames[^1].Time;
        var time = Math.Min(0, Math.Min(frames[0].Time, objects.Select(note => note.StartTime - 500).DefaultIfEmpty(0).Min()));
        var previousTime = time;

        while (time <= endTime || nextInput < frames.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            manualClock.CurrentTime = time;
            clock.ProcessFrame();
            var rate = rates.Aggregate(1.0, (value, mod) => mod.ApplyToRate(time, value));
            var frame = new BmsFrameContext(time, rate, time < previousTime);
            while (nextInput < frames.Length && frames[nextInput].Time == time)
            {
                var input = frames[nextInput++];
                if (pressed.SetEquals(input.Actions))
                    continue;

                var actions = input.Actions.ToHashSet();
                foreach (var action in pressed)
                {
                    if (!actions.Contains(action)) queue(action, false, input.Time);
                }

                foreach (var action in actions)
                {
                    if (!pressed.Contains(action)) queue(action, true, input.Time);
                }

                pressed = actions;
            }

            engine.ProcessFrame(frame, time - previousTime, algorithm);
            // Aborted attempts also settle held tails at their recorded endpoint.
            if (nextInput == frames.Length && time == endTime)
                engine.CompleteLongNotesAtEnd(frame);
            previousTime = time;
            var nextTime = nextInput < frames.Length ? frames[nextInput].Time : double.PositiveInfinity;
            nextTime = Math.Min(nextTime, engine.NextEventTime);
            time = Math.Max(Math.BitIncrement(time), Math.Min(nextTime, time < endTime ? endTime : Math.BitIncrement(endTime)));
        }

        return (score.JudgementEvents, health.GaugeHistory);

        void queue(BmsAction action, bool down, double inputTime)
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(action, beatmap.LayoutVariant) is { } index && index < columnCount)
                engine.QueueInput(inputTime, index, down, BmsKeyBindingConfiguration.IsReverseScratch(action));
        }
    }

    private sealed class SimulationOutput(BmsScoreProcessor score, BmsHealthProcessor health) : IBmsJudgementOutput
    {
        private readonly Dictionary<BmsHitObject, BmsJudgementResult> results = new(ReferenceEqualityComparer.Instance);
        private Action<BmsHitObject, double> history = null!;
        private Action<BmsHitObject> checkpoint = null!;

        public BmsMainResultState GetMainResult(BmsHitObject note)
            => results.TryGetValue(note, out var result)
                ? new BmsMainResultState(result.Type, result.EventTime, result.EventTime - note.GetEndTime(), result.GameplayRate, result.SuppressPenalty)
                : new BmsMainResultState(HitResult.None, note.GetEndTime(), 0, null, false);

        public void SetResultCheckpoints(Action<BmsHitObject, double> recordHistory, Action<BmsHitObject> refreshIndexes)
        {
            history = recordHistory;
            checkpoint = refreshIndexes;
        }

        public void ApplyMainResult(BmsHitObject note, HitResult type, in BmsFrameContext frame, bool suppressPenalty)
            => apply(note, new BmsJudgementResult(note, note.CreateJudgement()) { Type = type, SuppressPenalty = suppressPenalty }, frame);

        public void ApplyMainResult(BmsLongNote note, HitResult type, IReadOnlyList<BmsLongNoteEndpointResult> endpoints, in BmsFrameContext frame, bool suppressPenalty)
            => apply(note, new BmsLongNoteJudgementResult(note, note.CreateJudgement(), endpoints) { Type = type, SuppressPenalty = suppressPenalty }, frame);

        private void apply(BmsHitObject note, BmsJudgementResult result, in BmsFrameContext frame)
        {
            result.SetTiming(frame.SimulationTime, frame.GameplayRate);
            results.Add(note, result);
            score.ApplyResult(result);
            health.ApplyResult(result);
            history(note, frame.SimulationTime);
            checkpoint(note);
        }

        public void ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint, in BmsFrameContext frame, bool suppressPenalty)
            => health.ApplySyntheticLongNoteEndpoint(score.ApplySyntheticLongNoteEndpoint(endpoint, suppressPenalty));

        public void ApplyBodyTick(BmsLongNote note, bool holding, double scale, in BmsFrameContext frame)
            => health.ApplyHellChargeTick(holding, scale, frame.SimulationTime);

        public void ApplyNonConsumingBad(BmsHitObject note, in BmsFrameContext frame)
            => nonConsuming(HitResult.Ok, note.StartTime, note.Column, frame);

        public void ApplyEmptyPoor(double expectedTime, int column, in BmsFrameContext frame)
            => nonConsuming(HitResult.Miss, expectedTime, column, frame);

        private void nonConsuming(HitResult type, double expectedTime, int column, in BmsFrameContext frame)
        {
            score.RegisterNonConsumingJudgement(type, frame.SimulationTime, expectedTime, column);
            health.RegisterNonConsumingJudgement(type, frame.SimulationTime);
        }

        public void PublishState(BmsLongNote note, in BmsLongNoteState state, in BmsFrameContext frame)
        {
        }

        public void PublishState(BmsLandmine note, in BmsLandmineState state, in BmsFrameContext frame)
        {
        }

        public void PlaySample(BmsHitObject note)
        {
        }

        public void PlayTailSample(BmsLongNote note)
        {
        }

        public void PlayKeySound()
        {
        }

        public void PlayLandmineSample(BmsLandmine note)
        {
        }
    }
}
