using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Patterns;

public sealed record TapConfiguration(
    TimingWindows Windows,
    long LateDeadline,
    ResultId MissResult,
    bool OnRelease = false,
    ImmutableHashSet<ResultId>? NonResolvingResults = null,
    TimingWindows? ResolvedWindows = null,
    Func<ResultId, int>? CandidatePriority = null,
    Func<ResultId, long, bool>? ShareInput = null,
    bool BlockResolvedUntilStart = false,
    bool SuppressRepeatedMistakes = false,
    ImmutableHashSet<ResultId>? RecoveringResults = null,
    TimerOrder PassiveOrder = TimerOrder.BeforeInput);

public sealed record TapState(NoteTimer? Timer, bool Done, bool Resolved, bool Mistake = false) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}

public sealed class TapRule : INoteRule, INoteTiming
{
    public NoteTiming Timing { get; }

    public ImmutableArray<NotePartState> GetParts(NoteContext context)
        => [new(NoteParts.TAP, ((TapState)context.State).Resolved)];

    public TapConfiguration Configuration { get; }

    public TapRule(TapConfiguration configuration)
    {
        if (configuration.LateDeadline < 0)
            throw new ArgumentOutOfRangeException(nameof(configuration));

        Configuration = configuration;
        Timing = new NoteTiming(configuration.Windows, configuration.LateDeadline, configuration.NonResolvingResults,
            ResolvedHeadDeadline: configuration.ResolvedWindows?.Entries.Max(window => window.Late) ?? 0);
    }

    public NoteState CreateState(ChartNote note) => new TapState(new NoteTimer(checked(note.StartTime + Configuration.LateDeadline + 1), Configuration.PassiveOrder), false, false);

    long? INoteRule.EarliestCandidateOffset => Math.Min(0, Timing.EarliestInputOffset);

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        if (input.Pressed == Configuration.OnRelease)
            return null;

        var state = (TapState)context.State;
        var offset = context.Time - context.Note.StartTime;
        if (state.Resolved && Configuration.BlockResolvedUntilStart && offset < 0)
            return new InputCandidate(noteIndex, context.Note.StartTime, offset, 0);

        var result = (state.Resolved ? Configuration.ResolvedWindows : Configuration.Windows)?.ResultFor(offset);
        if (Configuration.SuppressRepeatedMistakes && (state.Resolved || (state.Mistake && (result == null || Configuration.RecoveringResults?.Contains(result) != true))))
            return null;

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
        else if (Configuration.SuppressRepeatedMistakes)
            state = state with { Mistake = true };

        return new NoteUpdate(state, [new NoteEmission(nonResolving ? NoteParts.EMPTY : NoteParts.TAP, result, context.Note.StartTime, offset)]);
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (TapState)context.State;
        if (state.Resolved)
            return NoteUpdate.Silent(new TapState(null, true, true));

        return new NoteUpdate(resolvedState(context.Note, context.Time), [
            new NoteEmission(NoteParts.TAP, Configuration.MissResult, context.Note.StartTime,
                SuppressPenalty: state.Mistake && Configuration.SuppressRepeatedMistakes)
        ]);
    }

    private TapState resolvedState(ChartNote note, long time)
    {
        var expiry = Configuration.ResolvedWindows?.Entries.Max(w => w.Late) is { } late ? checked(note.StartTime + late + 1) : time;
        if (Configuration.BlockResolvedUntilStart)
            expiry = Math.Max(expiry, note.StartTime);
        return expiry > time ? new TapState(new NoteTimer(expiry), false, true) : new TapState(null, true, true);
    }
}
