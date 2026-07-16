using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public static class BmsJudgementEventProjection
{
    public static List<HitEvent> CreateScoringHitEvents(IEnumerable<BmsJudgementEvent> judgementEvents)
    {
        var events = judgementEvents.ToArray();

        var hitEvents = new List<HitEvent>(events.Length);
        HitObject? lastHitObject = null;

        foreach (var judgementEvent in events)
        {
            var observation = judgementEvent.TimingObservations[^1];
            var hitObject = createScoringHitObject(judgementEvent.Source, observation);
            var hitEvent = new HitEvent(
                observation.ActualTime - hitObject.GetEndTime(),
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
    {
        var observations = judgementEvents
            .SelectMany(judgementEvent => judgementEvent.TimingObservations.Select(observation => (judgementEvent.Source, Observation: observation)))
            .OrderBy(item => item.Observation.ActualTime)
            .ToArray();

        var hitEvents = new List<HitEvent>(observations.Length);
        HitObject? lastHitObject = null;

        foreach (var (source, observation) in observations)
        {
            var hitEvent = CreateTimingHitEvent(source, observation, lastHitObject);
            hitEvents.Add(hitEvent);
            lastHitObject = hitEvent.HitObject;
        }

        return hitEvents;
    }

    internal static HitEvent CreateTimingHitEvent(BmsJudgementSource source, BmsTimingObservation observation, HitObject? lastHitObject)
        => new(
            observation.TimeOffset,
            observation.GameplayRate,
            observation.Result,
            createObservationHitObject(source, observation),
            lastHitObject,
            null
        );

    private static HitObject createScoringHitObject(BmsJudgementSource source, BmsTimingObservation observation)
    {
        if (source.Kind == BmsJudgementSourceKind.LongNote
            && observation.Kind == BmsTimingObservationKind.LongNoteHead)
            return createNote(source, observation.ExpectedTime);

        return createHitObject(source);
    }

    private static HitObject createObservationHitObject(BmsJudgementSource source, BmsTimingObservation observation)
    {
        if (source.Kind == BmsJudgementSourceKind.LongNote
            && observation.Kind is BmsTimingObservationKind.LongNoteHead or BmsTimingObservationKind.LongNoteTail)
            return createNote(source, observation.ExpectedTime);

        return createHitObject(source);
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

    private static BmsNote createNote(BmsJudgementSource source, double startTime) => new()
    {
        StartTime = startTime,
        Column = source.Column,
    };
}
