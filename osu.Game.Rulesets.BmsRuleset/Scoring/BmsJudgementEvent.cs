using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public enum BmsTimingObservationKind
{
    Note,
    LongNoteHead,
    LongNoteTail,
}

public readonly record struct BmsTimingObservation(
    BmsTimingObservationKind Kind,
    double ExpectedTime,
    double ActualTime,
    double? GameplayRate,
    HitResult Result)
{
    public double TimeOffset => ActualTime - ExpectedTime;
}

public enum BmsJudgementSourceKind
{
    Note,
    LongNote,
    Landmine,
    EmptyPoor,
}

public readonly record struct BmsJudgementSource(
    double StartTime,
    int Column,
    BmsJudgementSourceKind Kind,
    double Duration = 0,
    double LandmineDamagePercent = 0)
{
    public double EndTime => StartTime + Duration;

    public bool IsScoring => Kind is BmsJudgementSourceKind.Note or BmsJudgementSourceKind.LongNote;

    public static BmsJudgementSource From(HitObject hitObject)
    {
        ArgumentNullException.ThrowIfNull(hitObject);

        return new BmsJudgementSource(
            hitObject.StartTime,
            hitObject is BmsHitObject bms ? bms.Column : 0,
            hitObject switch
            {
                BmsLandmine => BmsJudgementSourceKind.Landmine,
                BmsLongNote => BmsJudgementSourceKind.LongNote,
                BmsHitObject => BmsJudgementSourceKind.Note,
                _ => BmsJudgementSourceKind.EmptyPoor,
            },
            hitObject is BmsLongNote longNote ? longNote.Duration : 0,
            hitObject is BmsLandmine landmine ? landmine.LandmineDamagePercent : 0);
    }
}

public sealed record BmsJudgementEvent
{
    public BmsJudgementSource Source { get; }

    public HitResult Result { get; }

    public IReadOnlyList<BmsTimingObservation> TimingObservations { get; }

    public BmsJudgementEvent(
        BmsJudgementSource source,
        HitResult result,
        IEnumerable<BmsTimingObservation> timingObservations)
    {
        ArgumentNullException.ThrowIfNull(timingObservations);

        var observations = timingObservations.ToArray();
        if (observations.Length == 0)
            throw new ArgumentException(@"At least one timing observation is required.", nameof(timingObservations));

        Source = source;
        Result = result;
        TimingObservations = Array.AsReadOnly(observations);
    }
}
