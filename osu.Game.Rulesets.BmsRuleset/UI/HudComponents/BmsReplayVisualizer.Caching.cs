using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsReplayVisualizer
{
    private const double cache_duration = 250;
    private CachedLane<TimelineNote>[]? cachedNotes;
    private CachedLane<InputStroke>[]? cachedInputs;
    private InputLane[]? cachedInputSource;
    private readonly SortedList<long, LiveChunk> cachedLiveInputs = [];
    private readonly List<long> expiredLiveChunks = [];
    private readonly Dictionary<BmsHitObject, LongNoteObservation> cachedLongNotes = [];
    private readonly PriorityQueue<CacheChange, double> pendingChanges = new();
    private readonly TimelineView timeline = new();
    private readonly TimelineBlock judgementBlock = new();
    private Vector2 cachedSize;
    private float cachedRange;
    private bool cachedConnections;
    private double cachedTime = double.NegativeInfinity;
    private int cachedEventCount;
    private BmsJudgementEvent? cachedLastEvent;

    private static long cacheBucket(double time) => (long)Math.Floor(time / cache_duration);

    private void prepareCachedGeometry(GeometryState geometry)
    {
        cachedNotes ??= notes.Select(lane => new CachedLane<TimelineNote>(lane.Items)).ToArray();
        if (!ReferenceEquals(cachedInputSource, replayInputs))
        {
            cachedInputSource = replayInputs;
            cachedInputs = replayInputs?.Select(lane => new CachedLane<InputStroke>(lane.Items)).ToArray();
        }

        if (cachedConnections != geometry.ShowNotes)
        {
            cachedConnections = geometry.ShowNotes;
            invalidateCachedInputs();
        }

        if (cachedSize != geometry.Size || cachedRange != geometry.Range)
        {
            cachedSize = geometry.Size;
            cachedRange = geometry.Range;
            invalidateCachedNotes();
            invalidateCachedInputs();
            judgementBlock.Rectangles.Clear();
            judgementBlock.Rectangles.Add(new TimelineRectangle(0, DrawWidth, 0, 0,
                DrawHeight * judgement_line - 0.5f, DrawHeight * judgement_line + 0.5f, TimelineKind.Fixed, Colour4.White));
            judgementBlock.Version++;
        }

        var now = geometry.Time;
        var range = geometry.Range * 1000;
        var firstTime = now - range * (1 - judgement_line);
        var lastTime = now + range * judgement_line;
        var events = (scoreProcessor as BmsScoreProcessor)?.JudgementEvents;
        var count = events?.Count ?? 0;
        if (now < cachedTime || count < cachedEventCount ||
            (cachedEventCount > 0 && !ReferenceEquals(events![cachedEventCount - 1], cachedLastEvent)))
        {
            cachedEventCount = 0;
            pendingChanges.Clear();
            cachedLongNotes.Clear();
            invalidateCachedNotes();
            invalidateCachedInputs();
        }

        cachedTime = now;
        judgements.Update(events);
        for (; cachedEventCount < count; cachedEventCount++)
        {
            var judgement = events![cachedEventCount];
            if (judgement.Source.Kind == BmsJudgementSourceKind.Landmine)
                continue;

            for (var i = 0; i < judgement.TimingObservations.Count; i++)
            {
                var timing = judgement.TimingObservations[i];
                pendingChanges.Enqueue(new CacheChange(judgement.Source, timing.ActualTime), timing.ActualTime);
            }
        }

        cachedLastEvent = count > 0 ? events![count - 1] : null;
        // A seek can leave future score events in the ledger. Activate their cache
        // changes at the observed input time so future notes retain their colour.
        while (pendingChanges.TryPeek(out _, out var time) && time <= now)
        {
            var change = pendingChanges.Dequeue();
            if ((uint)change.Source.Column < (uint)cachedNotes.Length)
                cachedNotes[change.Source.Column].Invalidate(change.Source.StartTime);
            if (change.Source.Kind == BmsJudgementSourceKind.LongNote)
                invalidateCachedInputs();
            else
            {
                if (cachedInputs != null && (uint)change.Source.Column < (uint)cachedInputs.Length)
                    cachedInputs[change.Source.Column].Invalidate(change.ActualTime);
                if (cachedLiveInputs.TryGetValue(cacheBucket(change.ActualTime), out var live))
                    live.Block.Dirty = true;
            }
        }

        foreach (var lane in geometry.ShowNotes || geometry.ShowInputs ? longNotes : [])
        {
            foreach (var note in lane.Visible(firstTime, lastTime))
            {
                var head = observationFor(note, false, now);
                var tail = observationFor(note, true, now);
                var observation = new LongNoteObservation(head, tail);
                if (!cachedLongNotes.TryGetValue(note.Source, out var previous) || previous != observation)
                {
                    cachedLongNotes[note.Source] = observation;
                    cachedNotes[note.Column].Invalidate(note.StartTime);
                    invalidateCachedInputs();
                }

                if (geometry.ShowInputs)
                    judgements.ObserveLongNote(note.Column, head, tail, note.EndTime);
            }
        }

        timeline.Blocks.Clear();
        timeline.Time = now;
        timeline.Range = geometry.Range;
        timeline.Size = geometry.Size;
        var columnWidth = DrawWidth / columnCount;
        if (geometry.ShowNotes)
        {
            foreach (var lane in cachedNotes)
            {
                foreach (var entry in lane.Visible(firstTime, lastTime))
                {
                    var chunk = entry.Chunk;
                    if (chunk.Block.Dirty)
                    {
                        chunk.Block.Rectangles.Clear();
                        foreach (var note in chunk.Items)
                        {
                            var head = observationFor(note, false, now);
                            var tail = note.Source is BmsLongNote ? observationFor(note, true, now) : null;
                            chunk.Block.Rectangles.Add(new TimelineRectangle((displayColumn(note.Column) + 0.1f) * columnWidth, columnWidth * 0.8f,
                                note.EndTime, note.StartTime, 0, 0, note.Source is BmsLongNote ? TimelineKind.LongNote : TimelineKind.Note,
                                ColourFor(NoteResult(head, tail))));
                        }

                        chunk.Block.Commit();
                    }

                    timeline.Blocks.Add(chunk.Block);
                }
            }
        }

        if (geometry.ShowInputs)
        {
            if (cachedInputs != null)
            {
                foreach (var lane in cachedInputs)
                {
                    foreach (var entry in lane.Visible(firstTime, now))
                    {
                        var chunk = entry.Chunk;
                        if (chunk.Block.Dirty)
                        {
                            chunk.Block.Rectangles.Clear();
                            foreach (var stroke in chunk.Items)
                                cacheInput(chunk.Block, stroke, now, range, columnWidth, geometry.ShowNotes);
                            chunk.Block.Commit();
                        }

                        timeline.Blocks.Add(chunk.Block);
                    }
                }
            }
            else if (indexedReplay == null)
            {
                expiredLiveChunks.Clear();
                for (var i = 0; i < cachedLiveInputs.Count; i++)
                {
                    var bucket = cachedLiveInputs.Keys[i];
                    var chunk = cachedLiveInputs.Values[i];
                    var end = chunk.EndTime;
                    if (end < now - 10000)
                    {
                        expiredLiveChunks.Add(bucket);
                        continue;
                    }

                    if (bucket * cache_duration > now || end < firstTime)
                        continue;

                    if (chunk.Block.Dirty)
                    {
                        chunk.Block.Rectangles.Clear();
                        foreach (var stroke in chunk.Items.Values)
                            cacheInput(chunk.Block, stroke, now, range, columnWidth, geometry.ShowNotes);
                        chunk.Block.Commit();
                    }

                    timeline.Blocks.Add(chunk.Block);
                }

                foreach (var bucket in expiredLiveChunks)
                    cachedLiveInputs.Remove(bucket);
            }
        }

        timeline.Blocks.Add(judgementBlock);
        Plot.Timeline = timeline;
    }

    private void cacheInput(TimelineBlock block, InputStroke stroke, double now, double range, float columnWidth, bool connections)
    {
        var x = (displayColumn(stroke.Column) + 0.5f) * columnWidth;
        var display = judgements.DescribeInput(stroke, now);
        var colour = ColourFor(display.Result);
        var held = double.IsPositiveInfinity(stroke.EndTime);
        block.Rectangles.Add(new TimelineRectangle(x - columnWidth * 0.22f, columnWidth * 0.44f,
            held ? stroke.StartTime : stroke.EndTime, stroke.StartTime, -1.5f, 1.5f, held ? TimelineKind.HeldInput : TimelineKind.Input, colour));
        if (!connections)
            return;

        cacheConnection(block, x, display.Press, colour, range);
        if (display.Release != display.Press)
            cacheConnection(block, x, display.Release, colour, range);
    }

    private void cacheConnection(TimelineBlock block, float x, BmsTimingObservation? timing, Colour4 colour, double range)
    {
        if (timing is not { } observation)
            return;

        var offset = observation.Kind == BmsTimingObservationKind.Note ? 2.5f : 0;
        var reverse = (observation.ExpectedTime - observation.ActualTime) / range * DrawHeight < offset;
        block.Rectangles.Add(new TimelineRectangle(x - 0.75f, 1.5f,
            reverse ? observation.ActualTime : observation.ExpectedTime, reverse ? observation.ExpectedTime : observation.ActualTime,
            reverse ? 0 : offset, reverse ? offset : 0, reverse ? TimelineKind.ReverseConnection : TimelineKind.Connection, colour));
    }

    private void invalidateCachedNotes()
    {
        if (cachedNotes != null)
            foreach (var lane in cachedNotes)
                lane.InvalidateAll();
    }

    private void invalidateCachedInputs()
    {
        if (cachedInputs != null)
            foreach (var lane in cachedInputs)
                lane.InvalidateAll();
        for (var i = 0; i < cachedLiveInputs.Count; i++)
            cachedLiveInputs.Values[i].Block.Dirty = true;
    }

    private void cacheLiveInput(InputStroke stroke)
    {
        var bucket = cacheBucket(stroke.StartTime);
        if (!cachedLiveInputs.TryGetValue(bucket, out var chunk))
            cachedLiveInputs[bucket] = chunk = new LiveChunk();
        chunk.Items[(stroke.Action, stroke.StartTime)] = stroke;
        chunk.EndInvalid = true;
        chunk.Block.Dirty = true;
    }

    private void resetLiveCache()
    {
        cachedLiveInputs.Clear();
        foreach (var stroke in liveInputs.Strokes)
            cacheLiveInput(stroke);
    }

    // Explicit values keep the shader's vertex attribute contract stable when adding kinds.
    internal enum TimelineKind
    {
        Fixed = 0,
        Note = 1,
        LongNote = 2,
        Input = 3,
        HeldInput = 4,
        Connection = 5,
        ReverseConnection = 6,
    }

    internal readonly record struct TimelineRectangle(
        float X,
        float Width,
        double TopTime,
        double BottomTime,
        float TopOffset,
        float BottomOffset,
        TimelineKind Kind,
        Colour4 Colour);

    internal sealed class TimelineBlock
    {
        internal readonly List<TimelineRectangle> Rectangles = [];
        internal long Version;
        internal bool Dirty = true;

        internal void Commit()
        {
            Version++;
            Dirty = false;
        }
    }

    internal sealed class TimelineView
    {
        internal readonly List<TimelineBlock> Blocks = [];
        internal double Time;
        internal float Range;
        internal Vector2 Size;
    }

    private sealed class CachedChunk<T>(T[] items) where T : struct, ITimelineItem
    {
        internal readonly T[] Items = items;
        internal readonly TimelineBlock Block = new();
    }

    private readonly record struct CachedEntry<T>(CachedChunk<T> Chunk, double StartTime, double EndTime) : ITimelineItem where T : struct, ITimelineItem;

    private sealed class CachedLane<T> : TimelineLane<CachedEntry<T>> where T : struct, ITimelineItem
    {
        private readonly Dictionary<long, TimelineBlock> blocks;

        internal CachedLane(T[] items)
            : base(items.GroupBy(item => cacheBucket(item.StartTime)).Select(group =>
                new CachedEntry<T>(new CachedChunk<T>(group.ToArray()), group.First().StartTime, group.Max(item => item.EndTime))))
        {
            blocks = Items.ToDictionary(entry => cacheBucket(entry.StartTime), entry => entry.Chunk.Block);
        }

        internal void Invalidate(double time)
        {
            if (blocks.TryGetValue(cacheBucket(time), out var block))
                block.Dirty = true;
        }

        internal void InvalidateAll()
        {
            foreach (var block in blocks.Values)
                block.Dirty = true;
        }
    }

    private sealed class LiveChunk
    {
        internal readonly Dictionary<(BmsAction Action, double Time), InputStroke> Items = [];
        internal readonly TimelineBlock Block = new();
        internal bool EndInvalid = true;

        internal double EndTime
        {
            get
            {
                if (EndInvalid)
                {
                    field = double.NegativeInfinity;
                    foreach (var item in Items.Values)
                        field = Math.Max(field, item.EndTime);
                    EndInvalid = false;
                }

                return field;
            }
        }
    }

    private readonly record struct CacheChange(BmsJudgementSource Source, double ActualTime);

    private readonly record struct LongNoteObservation(BmsTimingObservation? Head, BmsTimingObservation? Tail);
}
