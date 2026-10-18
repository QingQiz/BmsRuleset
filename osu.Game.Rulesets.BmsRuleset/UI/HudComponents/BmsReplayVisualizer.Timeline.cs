using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsReplayVisualizer
{
    private Replay? indexedReplay;
    private CancellationTokenSource? replayIndexCancellation;
    private Task<InputLane[]>? replayIndexTask;

    private void updateReplayInputs()
    {
        var replay = drawableRuleset?.ReplayScore?.Replay;
        if (!ReferenceEquals(replay, indexedReplay))
        {
            replayIndexCancellation?.Cancel();
            replayIndexCancellation?.Dispose();
            replayIndexCancellation = null;
            replayIndexTask = null;
            indexedReplay = replay;
            replayInputs = null;
            if (replay != null)
            {
                replayIndexCancellation = new CancellationTokenSource();
                var token = replayIndexCancellation.Token;
                var frames = replay.Frames.ToArray();
                var replayLayout = layout;
                var replayColumns = columnCount;
                // Frames may be appended by a replay source. Snapshot their references on
                // the update thread, and keep indexing work independent of drawable state.
                replayIndexTask = Task.Run(() => BuildReplayInputs(frames, replayLayout, replayColumns, token), token);
            }
        }

        if (replayIndexTask?.IsCompletedSuccessfully == true)
        {
            replayInputs = replayIndexTask.Result;
            replayIndexTask = null;
        }
        else if (replayIndexTask?.IsFaulted == true)
            replayIndexTask.GetAwaiter().GetResult();
    }

    internal static InputLane[] BuildReplayInputs(IReadOnlyList<ReplayFrame> frames, BmsLayoutVariant layout, int columnCount,
                                                  CancellationToken cancellationToken = default)
    {
        var history = new InputHistory();
        var released = new List<BmsAction>();
        for (var i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frames[i] is not BmsReplayFrame frame)
                continue;

            released.Clear();
            foreach (var action in history.HeldActions)
                if (!frame.Actions.Contains(action))
                    released.Add(action);
            foreach (var action in released)
                history.Release(action, frame.Time);

            foreach (var action in frame.Actions)
                if (BmsKeyBindingConfiguration.ActionToColumn(action, layout) is { } column && column < columnCount)
                    history.Press(action, column, frame.Time);
        }

        var strokesByColumn = new List<InputStroke>[columnCount];
        for (var column = 0; column < columnCount; column++)
            strokesByColumn[column] = [];
        for (var i = 0; i < history.Strokes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stroke = history.Strokes[i];
            strokesByColumn[stroke.Column].Add(stroke);
        }

        var lanes = new InputLane[columnCount];
        for (var column = 0; column < columnCount; column++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lanes[column] = new InputLane(strokesByColumn[column]);
        }
        return lanes;
    }

    internal interface ITimelineItem
    {
        double StartTime { get; }
        double EndTime { get; }
    }

    internal readonly record struct TimelineNote(BmsHitObject Source) : ITimelineItem
    {
        public int Column => Source.Column;

        public double StartTime => Source.StartTime;

        public double EndTime => Source is BmsLongNote longNote ? longNote.EndTime : Source.StartTime;
    }

    internal sealed class NoteLane(IEnumerable<BmsHitObject> hitObjects)
        : TimelineLane<TimelineNote>(hitObjects.Select(note => new TimelineNote(note)));

    internal class TimelineLane<T> where T : struct, ITimelineItem
    {
        private readonly T[] items;
        private readonly double[] maximumEndTimes;

        protected TimelineLane(IEnumerable<T> source)
        {
            items = source.OrderBy(item => item.StartTime).ToArray();
            maximumEndTimes = new double[items.Length];
            var maximum = double.NegativeInfinity;
            for (var i = 0; i < items.Length; i++)
                maximumEndTimes[i] = maximum = Math.Max(maximum, items[i].EndTime);
        }

        // Prefix maxima keep long notes visible even when their heads have scrolled away.
        public VisibleRange<T> Visible(double firstTime, double lastTime)
            => new(items, lowerBound(maximumEndTimes, firstTime), firstTime, lastTime);
    }

    internal readonly struct VisibleRange<T>(T[] items, int start, double firstTime, double lastTime) where T : struct, ITimelineItem
    {
        public Enumerator GetEnumerator() => new(items, start, firstTime, lastTime);

        public struct Enumerator(T[] items, int start, double firstTime, double lastTime)
        {
            private int index = start - 1;

            public T Current => items[index];

            public bool MoveNext()
            {
                while (++index < items.Length && items[index].StartTime <= lastTime)
                    if (items[index].EndTime >= firstTime)
                        return true;
                return false;
            }
        }
    }

    internal readonly record struct InputStroke(BmsAction Action, int Column, double StartTime, double EndTime = double.PositiveInfinity) : ITimelineItem;

    internal sealed class InputHistory
    {
        // Keep the maximum configurable window so changing TimeRange reveals recent input.
        private const double history_duration = 10000;

        private readonly List<InputStroke> strokes = [];
        private readonly Dictionary<BmsAction, int> held = [];
        private double previousTime = double.NegativeInfinity;
        private double nextExpiry = double.PositiveInfinity;

        public long Version { get; private set; }

        public IReadOnlyList<InputStroke> Strokes => strokes;

        public Dictionary<BmsAction, int>.KeyCollection HeldActions => held.Keys;

        public void Press(BmsAction action, int column, double time)
        {
            if (held.ContainsKey(action))
                return;

            held.Add(action, strokes.Count);
            strokes.Add(new InputStroke(action, column, time));
            Version++;
        }

        public void Release(BmsAction action, double time)
        {
            if (!held.Remove(action, out var index))
                return;

            var stroke = strokes[index];
            var end = Math.Max(stroke.StartTime, time);
            strokes[index] = stroke with { EndTime = end };
            nextExpiry = Math.Min(nextExpiry, end + history_duration);
            Version++;
        }

        public void Advance(double time)
        {
            var rewind = time < previousTime;
            if (rewind || time > nextExpiry)
            {
                held.Clear();
                nextExpiry = double.PositiveInfinity;
                var retained = 0;
                for (var i = 0; i < strokes.Count; i++)
                {
                    var stroke = strokes[i];
                    if (stroke.EndTime < time - history_duration || (rewind && stroke.StartTime > time))
                        continue;

                    if (rewind && stroke.EndTime > time)
                        stroke = stroke with { EndTime = double.PositiveInfinity };
                    if (double.IsPositiveInfinity(stroke.EndTime))
                        held[stroke.Action] = retained;
                    else
                        nextExpiry = Math.Min(nextExpiry, stroke.EndTime + history_duration);
                    strokes[retained++] = stroke;
                }

                strokes.RemoveRange(retained, strokes.Count - retained);
                Version++;
            }

            previousTime = time;
        }

        public void Clear()
        {
            strokes.Clear();
            held.Clear();
            previousTime = double.NegativeInfinity;
            nextExpiry = double.PositiveInfinity;
            Version++;
        }
    }

    internal sealed class InputLane(IEnumerable<InputStroke> source) : TimelineLane<InputStroke>(source);

    private static int lowerBound(double[] values, double target)
    {
        var lower = 0;
        var upper = values.Length;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (values[middle] < target)
                lower = middle + 1;
            else
                upper = middle;
        }

        return lower;
    }

    internal sealed class JudgementCursor
    {
        private readonly Dictionary<(int Column, double Time), BmsTimingObservation> observations = [];
        private readonly Dictionary<(int Column, double Time, bool Release), InputJudgement> inputObservations = [];
        private readonly Dictionary<(int Column, double Time, bool Release), InputJudgement> longNoteInputs = [];
        private int processedCount;
        private BmsJudgementEvent? lastEvent;

        public void Update(IReadOnlyList<BmsJudgementEvent>? events)
        {
            longNoteInputs.Clear();
            var count = events?.Count ?? 0;
            if (count < processedCount || (processedCount > 0 && !ReferenceEquals(events![processedCount - 1], lastEvent)))
            {
                observations.Clear();
                inputObservations.Clear();
                processedCount = 0;
            }

            for (; processedCount < count; processedCount++)
            {
                var judgement = events![processedCount];
                if (judgement.Source.Kind == BmsJudgementSourceKind.Landmine)
                    continue;

                for (var i = 0; i < judgement.TimingObservations.Count; i++)
                {
                    var observation = judgement.TimingObservations[i];
                    var column = judgement.Source.Column;
                    var scoring = judgement.Source.IsScoring;
                    if (scoring)
                    {
                        // Penalty suppression still leaves a missed note to display.
                        if (judgement.SuppressPenalty)
                            observations.TryAdd((column, observation.ExpectedTime), observation);
                        else
                            observations[(column, observation.ExpectedTime)] = observation;
                    }

                    if (judgement.SuppressPenalty)
                        continue;

                    var key = (column, observation.ActualTime, observation.Kind == BmsTimingObservationKind.LongNoteTail);
                    if (!inputObservations.TryGetValue(key, out var previous) || scoring || !previous.IsScoring)
                    {
                        var result = scoring && observation.Result == HitResult.Miss ? HitResult.Meh : observation.Result;
                        inputObservations[key] = new InputJudgement(result, scoring,
                            observation.Kind == BmsTimingObservationKind.LongNoteHead ? judgement.Source.EndTime : null, observation);
                    }
                }
            }

            lastEvent = count > 0 ? events![count - 1] : null;
        }

        public BmsTimingObservation? At(int column, double time, double now) =>
            observations.TryGetValue((column, time), out var observation) && observation.ActualTime <= now ? observation : null;

        public void ObserveLongNote(int column, BmsTimingObservation? head, BmsTimingObservation? tail, double endTime)
        {
            var result = NoteResult(head, tail);
            if (result is not { } value)
                return;

            // A normal LN head has no score event until completion; use its current state
            // for the matching press, and let a failed tail recolour the entire hold.
            if (head is { } press)
                longNoteInputs[(column, press.ActualTime, false)] = new InputJudgement(value, true, endTime, press);
            if (tail is { } release)
                longNoteInputs[(column, release.ActualTime, true)] = new InputJudgement(value, true, null, release);
        }

        public HitResult? ForInput(InputStroke stroke, double now)
        {
            var press = inputAt(stroke.Column, stroke.StartTime, false, now);
            // Charge scratch tails are judged on reversal presses.
            if (stroke.Action is BmsAction.Scratch or BmsAction.ScratchReverse or BmsAction.P2Scratch or BmsAction.P2ScratchReverse)
                press = worseResult(press, inputAt(stroke.Column, stroke.StartTime, true, now));
            return worseResult(press, inputAt(stroke.Column, stroke.EndTime, true, now));
        }

        public BmsTimingObservation? TimingForInput(InputStroke stroke, bool release, double now)
        {
            var time = release ? stroke.EndTime : stroke.StartTime;
            var judgement = findInput(stroke.Column, time, release, now);
            if (!release && stroke.Action is BmsAction.Scratch or BmsAction.ScratchReverse or BmsAction.P2Scratch or BmsAction.P2ScratchReverse)
                judgement ??= findInput(stroke.Column, time, true, now);
            return judgement is { IsScoring: true } value ? value.Timing : null;
        }

        private InputJudgement? findInput(int column, double time, bool release, double now)
        {
            if (time > now)
                return null;

            var key = (column, time, release);
            if (!longNoteInputs.TryGetValue(key, out var judgement) && !inputObservations.TryGetValue(key, out judgement))
                return null;

            return judgement;
        }

        private HitResult? inputAt(int column, double time, bool release, double now)
        {
            if (findInput(column, time, release, now) is not { } judgement)
                return null;

            return judgement.EndTime is { } end
                ? worseResult(judgement.Result, scoringResult(At(column, end, now)?.Result))
                : judgement.Result;
        }

        private readonly record struct InputJudgement(HitResult Result, bool IsScoring, double? EndTime, BmsTimingObservation Timing);
    }
}
