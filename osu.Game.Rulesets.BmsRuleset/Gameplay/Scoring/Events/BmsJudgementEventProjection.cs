using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;

public static class BmsJudgementEventProjection
{
    public static List<HitEvent> CreateScoringHitEvents(IEnumerable<BmsJudgementEvent> judgementEvents)
        => CreateScoringHitEvents(judgementEvents, 0);

    internal static List<HitEvent> CreateScoringHitEvents(IEnumerable<BmsJudgementEvent> judgementEvents, double timeOrigin)
    {
        var events = judgementEvents.Where(e => !e.SuppressPenalty).ToArray();

        var hitEvents = new List<HitEvent>(events.Length);
        HitObject? lastHitObject = null;

        foreach (var judgementEvent in events)
        {
            var source = judgementEvent.Source with { StartTime = judgementEvent.Source.StartTime - timeOrigin };
            var observation = relativeObservation(judgementEvent.TimingObservations[^1], timeOrigin);
            var hitObject = createScoringHitObject(source, observation);

            // Empty inputs stay anchored to the press for timeline ordering, while their timing error is
            // measured against the upcoming note that caused the non-consuming miss row.
            var hitEvent = new HitEvent(
                judgementEvent.Source.Kind == BmsJudgementSourceKind.EmptyInput
                    ? observation.TimeOffset
                    : observation.ActualTime - hitObject.GetEndTime(),
                observation.GameplayRate,
                judgementEvent.Result,
                hitObject,
                lastHitObject,
                null);

            hitEvents.Add(hitEvent);
            lastHitObject = hitEvent.HitObject;
        }

        return hitEvents;
    }

    public static List<HitEvent> CreateTimingHitEvents(IEnumerable<BmsJudgementEvent> judgementEvents)
        => CreateTimingHitEvents(judgementEvents, 0);

    internal static List<HitEvent> CreateTimingHitEvents(IEnumerable<BmsJudgementEvent> judgementEvents, double timeOrigin)
    {
        var observations = judgementEvents
            .Where(e => !e.SuppressPenalty)
            .SelectMany(judgementEvent => judgementEvent.TimingObservations.Select(observation =>
                (Source: judgementEvent.Source with { StartTime = judgementEvent.Source.StartTime - timeOrigin },
                    Observation: relativeObservation(observation, timeOrigin))))
            .OrderBy(item => item.Observation.ActualTime)
            .ToArray();

        var hitEvents = new List<HitEvent>(observations.Length);
        HitObject? lastHitObject = null;

        foreach (var (source, observation) in observations)
        {
            var hitEvent = CreateTimingHitEvent(observation, CreateTimingHitObject(source, observation), lastHitObject);
            hitEvents.Add(hitEvent);
            lastHitObject = hitEvent.HitObject;
        }

        return hitEvents;
    }

    private static BmsTimingObservation relativeObservation(BmsTimingObservation observation, double timeOrigin) => observation with
    {
        ExpectedTime = observation.ExpectedTime - timeOrigin,
        ActualTime = observation.ActualTime - timeOrigin,
    };

    internal static HitEvent CreateTimingHitEvent(BmsTimingObservation observation, HitObject hitObject, HitObject? lastHitObject)
        => new(
            observation.TimeOffset,
            observation.GameplayRate,
            observation.Result,
            hitObject,
            lastHitObject,
            null
        );

    private static HitObject createScoringHitObject(BmsJudgementSource source, BmsTimingObservation observation)
    {
        if (source.Kind == BmsJudgementSourceKind.NonConsumingHit
            || (source.Kind == BmsJudgementSourceKind.LongNote && observation.Kind == BmsTimingObservationKind.LongNoteHead))
            return CreateTimingNote(source.Column, observation.ExpectedTime);

        return createHitObject(source);
    }

    internal static HitObject CreateTimingHitObject(BmsJudgementSource source, BmsTimingObservation observation)
    {
        // Independently scored endpoints have a note-shaped source, so the observation owns endpoint identity.
        return observation.Kind switch
        {
            BmsTimingObservationKind.LongNoteTail => CreateTimingNote(source.Column, observation.ExpectedTime, isTail: true),
            BmsTimingObservationKind.LongNoteHead => CreateTimingNote(source.Column, observation.ExpectedTime),
            _ when source.Kind == BmsJudgementSourceKind.NonConsumingHit => CreateTimingNote(source.Column, observation.ExpectedTime),
            _ => createHitObject(source),
        };
    }

    private static HitObject createHitObject(BmsJudgementSource source)
    {
        HitObject hitObject = source.Kind switch
        {
            BmsJudgementSourceKind.LongNote => new BmsLongNote { Duration = source.Duration },
            BmsJudgementSourceKind.Landmine => new BmsLandmine { LandmineDamagePercent = source.LandmineDamagePercent },
            BmsJudgementSourceKind.Note => new BmsNote(),
            _ => new HitObject(),
        };

        hitObject.StartTime = source.StartTime;

        if (hitObject is BmsHitObject bms)
            bms.Column = source.Column;

        return hitObject;
    }

    internal static BmsNote CreateTimingNote(int column, double time, bool isTail = false)
    {
        var note = isTail ? new BmsLongNoteTailHitObject() : new BmsNote();
        note.StartTime = time;
        note.Column = column;
        return note;
    }
}

internal sealed class BmsLongNoteTailHitObject : BmsNote;
