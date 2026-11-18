using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Mania;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Common;

/// <summary>Press rules can be ordered independently of lane memory updates and playback.</summary>
public static class KeySoundPolicies
{
    public static readonly PartId STABLE_TAIL = new("stable-tail");

    public static KeySoundPolicy Beatoraja(Chart chart, IEnumerable<KeySoundMarker>? invisible = null)
        => KeySoundPolicy.FirstOf(JudgedPress(includeEmpty: true), keepBmsHoldVoice(chart), PassedOrFirst(chart, invisible))
            .WithTransitionSounds(muteBmsHeadOnEarlyRelease, changeSustainHeadGain, playBmsTail);

    private static KeySound? muteBmsHeadOnEarlyRelease(NoteTransition transition)
    {
        var before = part(transition.Before, NoteParts.TAIL);
        var tail = part(transition.After, NoteParts.TAIL);
        if (before.Resolved || !tail.Resolved || tail.Offset is not < 0 || transition.Input is { Pressed: true }
            || part(transition.Before, NoteParts.HEAD).Offset == null || part(transition.After, NoteParts.BODY).Reason == HoldReasons.HOST_END)
            return null;

        var result = transition.Judgements.FirstOrDefault(j => j.Event.Part == NoteParts.TAIL)?.Event.Result;
        if (tail.Reason == HoldReasons.DROP || (tail.Reason == HoldReasons.RELEASE && (result == BmsResults.BAD || result == BmsResults.POOR)))
            return new KeySound(transition.After.Id, NoteParts.HEAD, KeySoundAction.Mute);

        return null;
    }

    private static KeySound? changeSustainHeadGain(NoteTransition transition)
    {
        var tail = part(transition.After, NoteParts.TAIL);
        if (!part(transition.Before, NoteParts.TAIL).Resolved
            || (tail.Result != BmsResults.BAD && tail.Result != BmsResults.POOR)
            || part(transition.After, NoteParts.HEAD).Offset == null
            || part(transition.After, NoteParts.BODY).Resolved)
            return null;

        var active = part(transition.After, NoteParts.BODY).Active;
        if (active != part(transition.Before, NoteParts.BODY).Active)
            return new KeySound(transition.After.Id, NoteParts.HEAD, active ? KeySoundAction.Unmute : KeySoundAction.Mute);

        return null;
    }

    private static KeySound? playBmsTail(NoteTransition transition)
    {
        var tail = part(transition.After, NoteParts.TAIL);
        return !part(transition.Before, NoteParts.TAIL).Resolved && tail.Resolved && part(transition.After, NoteParts.BODY).Reason != HoldReasons.HOST_END
               && (tail.Reason == HoldReasons.RELEASE || tail.Reason == HoldReasons.DROP || tail.Reason == HoldReasons.AUTOMATIC)
            ? new KeySound(transition.After.Id, NoteParts.TAIL)
            : null;
    }

    private static KeySound? stopDroppedHead(NoteTransition transition)
    {
        if (transition.Input is not { Pressed: false } || !part(transition.Before, NoteParts.BODY).Active)
            return null;

        var tail = part(transition.After, NoteParts.TAIL);
        // R2 retains a wider BAD window than LR2. Its early BAD is outside LR2's successful release window.
        return tail.Reason == HoldReasons.DROP || (tail.Offset < 0 && tail.Result == BmsResults.BAD)
                                               || part(transition.After, NoteParts.BODY).Reason == HoldReasons.DROP
            ? new KeySound(transition.After.Id, NoteParts.HEAD, KeySoundAction.Stop)
            : null;
    }

    private static Func<NoteTransition, KeySound?> playHitTail(PartId source) => transition =>
        transition.Judgements.Any(j => j.Event.Part == NoteParts.TAIL && j.Event.Result != ManiaResults.MISS)
            ? new KeySound(transition.After.Id, source)
            : null;

    private static NotePartState part(NoteSnapshot note, PartId id) => part(note.Parts, id);

    private static NotePartState part(ImmutableArray<NotePartState> parts, PartId id)
    {
        foreach (var state in parts)
        {
            if (state.Part == id)
                return state;
        }

        return default;
    }

    private static KeySoundRule keepBmsHoldVoice(Chart chart)
    {
        var lanes = chart.LaneNotesByTime.Select(indices => sustainRanges(chart, indices)).ToArray();
        return (context, remembered) =>
        {
            // A press during release grace or a passing HCN keeps the existing voice.
            foreach (var transition in context.Transitions)
            {
                if (transition.Input != null && part(transition.Before, NoteParts.BODY).Active)
                    return new KeySoundDecision(null, remembered);
            }

            var time = context.Input.Time;
            var ranges = lanes[context.Input.Lane];
            var low = 0;
            var high = ranges.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var range = ranges[middle];
                if (time < range.Start)
                    high = middle;
                else if (time >= range.End)
                    low = middle + 1;
                else
                    return new KeySoundDecision(null, remembered);
            }

            return null;
        };
    }

    private static ImmutableArray<(long Start, long End)> sustainRanges(Chart chart, ImmutableArray<int> indices)
    {
        var ranges = ImmutableArray.CreateBuilder<(long Start, long End)>();
        foreach (var index in indices)
        {
            var note = chart.Notes[index];
            if (note.Rule is not HoldRule { Configuration.Balance: not null } || note.StartTime == note.EndTime)
                continue;

            if (ranges.Count > 0 && note.StartTime <= ranges[^1].End)
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, note.EndTime));
            else
                ranges.Add((note.StartTime, note.EndTime));
        }

        return ranges.DrainToImmutable();
    }

    public static KeySoundRule PassedOrFirst(Chart chart, IEnumerable<KeySoundMarker>? invisible = null)
    {
        var markers = (invisible ?? []).ToLookup(marker => marker.Lane);
        var lanes = Enumerable.Range(0, chart.LaneCount).Select(lane => new PassedSoundLane(chart, lane, markers[lane])).ToArray();
        return (context, remembered) => lanes[context.Input.Lane].Select(context, remembered);
    }

    private sealed class PassedSoundLane
    {
        private readonly ImmutableArray<SoundEndpoint> endpoints;
        private readonly ChartNote? first;

        internal PassedSoundLane(Chart chart, int lane, IEnumerable<KeySoundMarker> markers)
        {
            var ordered = chart.LaneNotesByTime[lane];
            first = ordered.IsEmpty ? null : chart.Notes[ordered[0]];

            // The last source at a timestamp wins. Invisible markers precede visible endpoints at a tie.
            var sounds = markers.Select(marker => new SoundEndpoint(marker.Time, marker.SourceId)).ToList();
            foreach (var index in chart.LaneNotes[lane])
            {
                var note = chart.Notes[index];
                var hold = note.StartTime < note.EndTime;
                sounds.Add(new SoundEndpoint(note.StartTime, note.Id, hold ? index : -1, IsMine: note.Rule is MineRule));
                if (hold)
                {
                    // Ordinary LNs record their sole judgement on the head, so the tail remains a source.
                    var scoresHead = chart.ScorePlan.OpportunitiesByPart.ContainsKey((note.Id, NoteParts.HEAD));
                    sounds.Add(new SoundEndpoint(note.EndTime, note.Id, scoresHead ? index : -1, Tail: true));
                }
            }

            endpoints = [.. sounds.OrderBy(sound => sound.Time)];
        }

        internal KeySoundDecision? Select(KeySoundContext context, int? remembered)
        {
            var time = context.Input.Time;
            var low = 0;
            var high = endpoints.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (endpoints[middle].Time < time)
                    low = middle + 1;
                else
                    high = middle;
            }

            // Most presses resolve at the latest endpoint. Earlier endpoints matter only if that part was judged.
            var projectedIndex = -1;
            ImmutableArray<NotePartState> parts = [];
            for (var i = low - 1; i >= 0; i--)
            {
                var endpoint = endpoints[i];
                var partId = endpoint.Tail ? NoteParts.TAIL : NoteParts.HEAD;
                if (endpoint.NoteIndex >= 0)
                {
                    if (projectedIndex != endpoint.NoteIndex)
                    {
                        projectedIndex = endpoint.NoteIndex;
                        parts = context.GetParts(projectedIndex);
                    }

                    if (part(parts, partId).Resolved)
                        continue;
                }

                return new KeySoundDecision(endpoint.IsMine ? null : new KeySound(endpoint.SourceId, endpoint.Tail ? partId : null), remembered);
            }

            return first == null ? null : new KeySoundDecision(first.Rule is MineRule ? null : new KeySound(first.Id), remembered);
        }
    }

    private readonly record struct SoundEndpoint(long Time, int SourceId, int NoteIndex = -1, bool Tail = false, bool IsMine = false);

    public static KeySoundPolicy Lr2 { get; } = KeySoundPolicy.FirstOf(
            JudgedPress(includeEmpty: true, remember: (_, note) => note),
            HoldPress((_, note) => note),
            Recall).WithTransitionSounds(stopDroppedHead) with
        {
            // A timeout changes the cached WAV without playing it.
            Observe = (transition, remembered) => transition.Input == null && transition.Judgements.Any(j => j.Event.Result == BmsResults.POOR)
                ? transition.After.Id
                : remembered,
        };

    public static KeySoundPolicy ManiaStable { get; } = KeySoundPolicy.FirstOf(
        JudgedPress(remember: (_, _) => null),
        HoldPress((_, _) => null),
        FutureOrRemembered(STABLE_TAIL)).WithTransitionSounds(playHitTail(STABLE_TAIL));

    public static KeySoundRule FutureOrRemembered(PartId? holdPart = null) => (context, remembered) =>
    {
        var chart = context.Chart;
        ChartNote? next;
        if (remembered is { } cached)
        {
            next = chart.NoteIndices.TryGetValue(cached, out var index) ? chart.Notes[index] : null;
            if (next?.Lane != context.Input.Lane)
                return null;
        }
        else
        {
            var lane = chart.LaneNotesByTime[context.Input.Lane];
            var position = chart.FirstLaneNoteAfter(context.Input.Lane, context.Input.Time);
            next = position < lane.Length ? chart.Notes[lane[position]] : null;
        }

        return next == null ? null : new KeySoundDecision(new(next.Id, next.StartTime < next.EndTime ? holdPart : null), next.Id);
    };

    public static KeySoundPolicy ManiaLazer { get; } = KeySoundPolicy.FirstOf(
        JudgedPress(silentResult: ManiaResults.MISS),
        PendingOrRemembered(maniaEarlySelection, maniaPendingSample)).WithTransitionSounds(playHitTail(NoteParts.TAIL));

    public static KeySoundRule PendingOrRemembered(Func<ChartNote, long> earlySelection, Func<ChartNote, long, bool, KeySound?> sample)
        => (context, remembered) =>
        {
            var chart = context.Chart;
            var lane = chart.LaneNotesByTime[context.Input.Lane];
            if (lane.IsEmpty)
                return null;

            var time = context.Input.Time;
            var selected = remembered is { } cached && chart.NoteIndices.TryGetValue(cached, out var index)
                                                    && chart.Notes[index].Lane == context.Input.Lane
                ? index
                : -1;
            var completed = selected >= 0 && context.IsCompleted(selected);
            if (selected < 0 || completed)
            {
                var position = context.FirstIncomplete;
                while (position < lane.Length && context.IsCompleted(lane[position]))
                    position++;

                if (position == lane.Length)
                {
                    selected = lane[^1];
                    completed = true;
                }
                else
                {
                    var candidate = chart.Notes[lane[position]];
                    if (time >= candidate.StartTime - earlySelection(candidate))
                    {
                        selected = lane[position];
                        completed = false;
                    }
                    else if (selected < 0)
                    {
                        selected = lane[0];
                        completed = context.IsCompleted(selected);
                    }
                }
            }

            var note = chart.Notes[selected];
            return new KeySoundDecision(sample(note, time, completed), note.Id);
        };

    // A native hold parent has no Miss window. Its head can still be used as a child.
    private static long maniaEarlySelection(ChartNote note)
        => note.StartTime < note.EndTime ? 0 : 2 * ((INoteTiming)note.Rule).Timing.HeadWindows.Entries[^1].Late;

    // A hold's body is the first child left after its head passes and has no sample.
    private static KeySound? maniaPendingSample(ChartNote note, long time, bool completed)
        => !completed && note.StartTime <= time && time < note.EndTime ? null : new KeySound(note.Id);

    public static KeySoundRule JudgedPress(bool includeEmpty = false, ResultId? silentResult = null, Func<int?, int, int?>? remember = null)
        => (context, remembered) =>
        {
            foreach (var transition in context.Transitions)
            {
                if (transition.Input == null)
                    continue;

                foreach (var judgement in transition.Judgements)
                {
                    var hit = judgement.Event;
                    var part = hit.Part;
                    if (part == NoteParts.HEAD || part == NoteParts.TAP || part == NoteParts.NOTE || (includeEmpty && part == NoteParts.EMPTY))
                        return new KeySoundDecision(hit.Result == silentResult ? null : new KeySound(hit.NoteId),
                            remember == null ? remembered : remember(remembered, hit.NoteId));
                }
            }

            return null;
        };

    public static KeySoundRule HoldPress(Func<int?, int, int?>? remember = null) => (context, remembered) =>
    {
        foreach (var transition in context.Transitions)
        {
            if (transition.Input == null)
                continue;

            var wasHolding = transition.Before.Parts.Any(part => part.Part == NoteParts.BODY && part.Active);
            if (!wasHolding && transition.After.Parts.Any(part => part.Part == NoteParts.BODY && part.Active))
                return new KeySoundDecision(new(transition.After.Id), remember == null ? remembered : remember(remembered, transition.After.Id));
        }

        return null;
    };

    public static KeySoundDecision? Recall(KeySoundContext context, int? remembered)
        => remembered == null ? null : new KeySoundDecision(source(remembered), remembered);

    private static KeySound? source(int? id) => id is { } sourceId ? new KeySound(sourceId) : null;
}

public readonly record struct KeySoundMarker(int SourceId, int Lane, long Time);
