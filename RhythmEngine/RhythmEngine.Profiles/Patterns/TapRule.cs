using System.Collections.Immutable;

namespace RhythmEngine.Profiles.Patterns;

public sealed record TapConfiguration(
    TimingWindows Windows,
    long LateDeadline,
    string MissResult,
    double MaximumAccuracy,
    bool OnRelease = false,
    ImmutableHashSet<string>? NonResolvingResults = null,
    TimingWindows? ResolvedWindows = null,
    Func<string, int>? CandidatePriority = null,
    Func<string, long, bool>? ShareInput = null,
    bool BlockResolvedUntilStart = false);

public sealed record TapState(NoteTimer? Timer, bool Done, bool Resolved) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}

public sealed class TapRule : INoteRule
{
    public ImmutableArray<NotePartState> GetParts(NoteContext context)
        => [new("tap", ((TapState)context.State).Resolved)];

    public TapConfiguration Configuration { get; }

    public TapRule(TapConfiguration configuration)
    {
        if (configuration.LateDeadline < 0 || configuration.MaximumAccuracy < 0 || !double.IsFinite(configuration.MaximumAccuracy))
            throw new ArgumentOutOfRangeException(nameof(configuration));

        Configuration = configuration;
    }

    public NoteState CreateState(ChartNote note) => new TapState(new NoteTimer(checked(note.StartTime + Configuration.LateDeadline + 1), TimerOrder.BeforeInput), false, false);

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        yield return new ScoringOpportunity(note.Id, "tap", note.StartTime, Configuration.MaximumAccuracy);
    }

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        if (input.Pressed == Configuration.OnRelease)
            return null;

        var state = (TapState)context.State;
        var offset = context.Time - context.Note.StartTime;
        if (state.Resolved && Configuration.BlockResolvedUntilStart && offset < 0)
            return new InputCandidate(noteIndex, context.Note.StartTime, offset, 0);
        var result = (state.Resolved ? Configuration.ResolvedWindows : Configuration.Windows)?.ResultFor(offset);
        return result == null
            ? null
            : new InputCandidate(noteIndex, context.Note.StartTime, offset, Configuration.CandidatePriority?.Invoke(result) ?? 1,
                Configuration.ShareInput?.Invoke(result, offset) == true);
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = (TapState)context.State;
        if (state.Resolved && Configuration.BlockResolvedUntilStart && context.Time < context.Note.StartTime)
            return NoteUpdate.Silent(state);
        var offset = context.Time - context.Note.StartTime;
        var result = (state.Resolved ? Configuration.ResolvedWindows : Configuration.Windows)!.ResultFor(offset)!;
        var nonResolving = state.Resolved || Configuration.NonResolvingResults?.Contains(result) == true;
        if (!nonResolving)
            state = resolvedState(context.Note, context.Time);

        return new NoteUpdate(state, [new NoteEmission(nonResolving ? "empty" : "tap", result, context.Note.StartTime, offset)]);
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (TapState)context.State;
        if (state.Resolved)
            return NoteUpdate.Silent(new TapState(null, true, true));

        return new NoteUpdate(resolvedState(context.Note, context.Time), [new NoteEmission("tap", Configuration.MissResult, context.Note.StartTime)]);
    }

    private TapState resolvedState(ChartNote note, long time)
    {
        var expiry = Configuration.ResolvedWindows?.Entries.Max(w => w.Late) is { } late ? checked(note.StartTime + late + 1) : time;
        if (Configuration.BlockResolvedUntilStart)
            expiry = Math.Max(expiry, note.StartTime);
        return expiry > time ? new TapState(new NoteTimer(expiry), false, true) : new TapState(null, true, true);
    }
}
