using System.Collections.Immutable;

namespace RhythmEngine.Core;

public sealed record ChartNote(int Id, int Lane, long StartTime, long EndTime, INoteRule Rule);

public sealed record ScoringOpportunity(int NoteId, string Part, long Time, double MaximumAccuracy, double Weight = 1);

public sealed class ScorePlan
{
    public ImmutableArray<ScoringOpportunity> Opportunities { get; }

    public double MaximumAccuracy { get; }

    internal ScorePlan(ImmutableArray<ScoringOpportunity> opportunities)
    {
        Opportunities = opportunities;
        MaximumAccuracy = opportunities.Sum(o => o.MaximumAccuracy * o.Weight);
    }
}

public sealed class Chart
{
    public int LaneCount { get; }

    public ImmutableArray<ChartNote> Notes { get; }

    public ScorePlan ScorePlan { get; }

    public Chart(int laneCount, IEnumerable<ChartNote> notes)
    {
        if (laneCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(laneCount));

        LaneCount = laneCount;
        // Source order is authoritative for simultaneous objects, including during replay reconstruction.
        Notes = [..notes];
        if (Notes.Select(n => n.Id).Distinct().Count() != Notes.Length
            || Notes.Any(n => n.Lane < 0 || n.Lane >= laneCount || n.EndTime < n.StartTime))
            throw new ArgumentException("Notes require unique IDs, valid lanes, rules and ordered endpoints.", nameof(notes));

        var opportunities = Notes.SelectMany(n => n.Rule.GetScoringOpportunities(n)).ToImmutableArray();
        if (opportunities.Any(o => string.IsNullOrWhiteSpace(o.Part) || !double.IsFinite(o.MaximumAccuracy)
                                                                     || o.MaximumAccuracy < 0 || !double.IsFinite(o.Weight) || o.Weight <= 0)
            || opportunities.Select(o => (o.NoteId, o.Part)).Distinct().Count() != opportunities.Length)
            throw new ArgumentException("Scoring opportunities require unique part IDs and finite nonnegative potential.", nameof(notes));

        ScorePlan = new ScorePlan(opportunities);
    }
}
