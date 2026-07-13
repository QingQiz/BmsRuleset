using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public enum BmsLongNoteEndpointKind
{
    Head,
    Tail,
}

public readonly record struct BmsLongNoteEndpointResult(
    BmsLongNote Source,
    BmsLongNoteEndpointKind Kind,
    double EventTime,
    double GameplayRate,
    HitResult Result)
{
    public double ExpectedTime => Kind == BmsLongNoteEndpointKind.Head ? Source.StartTime : Source.EndTime;

    public double TimeOffset => EventTime - ExpectedTime;
}
