using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Patterns;

public sealed record MineState(NoteTimer? Timer, bool Done, bool Active) : NoteState
{
    public override NoteTimer? NextTimer => Timer;

    public override bool Completed => Done;
}

public sealed class MineRule(
    long earlyWindow,
    long lateWindow,
    ResultId hitResult,
    ResultId avoidedResult,
    TimerOrder order = TimerOrder.AfterInput,
    bool shareInput = true,
    long? expiryWindow = null,
    bool acceptsInput = true) : INoteRule
{
    public ImmutableArray<NotePartState> GetParts(NoteContext context)
        => [new(NoteParts.MINE, context.State.Completed)];

    public NoteState CreateState(ChartNote note)
    {
        if (earlyWindow < 0 || lateWindow < 0)
            throw new ArgumentOutOfRangeException(nameof(earlyWindow));

        return new MineState(new NoteTimer(note.StartTime, order), false, false);
    }

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note) => [];

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
        => acceptsInput && input.Pressed && context.Time >= context.Note.StartTime - earlyWindow && context.Time <= context.Note.StartTime + lateWindow
            ? new InputCandidate(noteIndex, context.Note.StartTime, context.Time - context.Note.StartTime, shareInput ? 0 : 1, shareInput)
            : null;

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
        => new(new MineState(null, true, false), [new NoteEmission(NoteParts.MINE, hitResult, context.Note.StartTime, context.Time - context.Note.StartTime)]);

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = (MineState)context.State;
        if (!state.Active && context.Input.Held)
            return new NoteUpdate(new MineState(null, true, false), [new NoteEmission(NoteParts.MINE, hitResult, context.Note.StartTime)]);

        if (!state.Active && lateWindow > 0)
            return NoteUpdate.Silent(new MineState(new NoteTimer(checked(context.Note.StartTime + (expiryWindow ?? lateWindow) + 1), order), false, true));

        return new NoteUpdate(new MineState(null, true, false), [new NoteEmission(NoteParts.MINE, avoidedResult, context.Note.StartTime)]);
    }
}
