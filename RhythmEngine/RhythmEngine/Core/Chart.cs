using System.Collections.Frozen;
using System.Collections.Immutable;

namespace RhythmEngine.Core;

public sealed record ChartNote(int Id, int Lane, long StartTime, long EndTime, INoteRule Rule);

public sealed record ScoringOpportunity(int NoteId, PartId Part, double MaximumAccuracy, double Weight = 1);

public sealed class ScorePlan
{
    public ImmutableArray<ScoringOpportunity> Opportunities { get; }

    public double MaximumAccuracy { get; }

    public FrozenDictionary<(int NoteId, PartId Part), ScoringOpportunity> OpportunitiesByPart { get; }

    internal ScorePlan(ImmutableArray<ScoringOpportunity> opportunities)
    {
        Opportunities = opportunities;
        MaximumAccuracy = opportunities.Sum(o => o.MaximumAccuracy * o.Weight);
        OpportunitiesByPart = opportunities.ToFrozenDictionary(o => (o.NoteId, o.Part));
    }
}

public sealed class Chart
{
    public int LaneCount { get; }

    public ImmutableArray<ChartNote> Notes { get; }

    public ScorePlan ScorePlan { get; }

    internal ImmutableArray<ImmutableArray<int>> LaneNotes { get; }

    internal FrozenDictionary<int, int> NoteIndices { get; }

    public Chart(int laneCount, IEnumerable<ChartNote> notes, IEnumerable<ScoringOpportunity> scoringOpportunities)
    {
        if (laneCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(laneCount));

        LaneCount = laneCount;
        // Source order is authoritative for simultaneous objects, including during replay reconstruction.
        Notes = [.. notes];
        if (Notes.Select(n => n.Id).Distinct().Count() != Notes.Length
            || Notes.Any(n => n.Lane < 0 || n.Lane >= laneCount || n.EndTime < n.StartTime))
            throw new ArgumentException("Notes require unique IDs, valid lanes, rules and ordered endpoints.", nameof(notes));

        NoteIndices = Notes.Select((n, i) => (n.Id, Index: i)).ToFrozenDictionary(n => n.Id, n => n.Index);
        var opportunities = scoringOpportunities.ToImmutableArray();
        // Scoring policies cross the public chart boundary, so validate their note and part IDs at runtime.
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (opportunities.Any(o => !NoteIndices.ContainsKey(o.NoteId) || o.Part == null || !double.IsFinite(o.MaximumAccuracy) || o.MaximumAccuracy < 0 || !double.IsFinite(o.Weight) || o.Weight <= 0)
            || opportunities.Select(o => (o.NoteId, o.Part)).Distinct().Count() != opportunities.Length)
            throw new ArgumentException("Scoring opportunities require chart notes, unique part IDs and finite nonnegative potential.", nameof(scoringOpportunities));

        ScorePlan = new ScorePlan(opportunities);
        var laneNotes = Notes.Select((n, i) => (n.Lane, Index: i)).ToLookup(n => n.Lane, n => n.Index);
        LaneNotes = [.. Enumerable.Range(0, laneCount).Select(lane => laneNotes[lane].ToImmutableArray())];
    }
}
