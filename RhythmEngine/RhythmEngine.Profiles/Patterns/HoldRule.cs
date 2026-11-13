using System.Collections.Immutable;

namespace RhythmEngine.Profiles.Patterns;

public sealed class HoldRule : INoteRule
{
    public ImmutableArray<NotePartState> GetParts(NoteContext context)
    {
        var hold = (HoldState)context.State;
        var active = !hold.Done && (Configuration.Balance != null
            ? hold.HeadResult != null && hold.BalanceTime < context.Note.EndTime && (hold.TailSustains || context.Input.Held)
            : !hold.TailResolved && hold.Holding);
        var resolved = Configuration.Balance == null ? hold.TailResolved : hold.Done || hold.BalanceTime >= context.Note.EndTime;
        return [new("head", hold.HeadResult != null), new("body", resolved, active), new("tail", hold.TailResolved)];
    }

    public HoldConfiguration Configuration { get; }

    public HoldRule(HoldConfiguration configuration)
    {
        if (configuration.HeadDeadline < 0 || configuration.TailDeadline < 0 || configuration.Grace < 0
            || configuration.AutomaticEndOffset < 0
            || configuration.Balance is { Threshold: <= 0 } || (configuration.RequiresRetrigger && configuration.Grace == 0))
            throw new ArgumentOutOfRangeException(nameof(configuration));

        Configuration = configuration;
    }

    public NoteState CreateState(ChartNote note)
    {
        if (note.EndTime <= note.StartTime)
            throw new ArgumentException("A hold requires a positive duration.", nameof(note));

        var state = new HoldState(null, false, null, null, false, false, 0, null, 0, note.StartTime);
        return schedule(note, state, long.MinValue, false);
    }

    public IEnumerable<ScoringOpportunity> GetScoringOpportunities(ChartNote note)
    {
        if (Configuration.HeadAccuracy > 0)
            yield return new ScoringOpportunity(note.Id, "head", note.StartTime, Configuration.HeadAccuracy);
        if (Configuration.TailAccuracy > 0)
            yield return new ScoringOpportunity(note.Id, "tail", note.EndTime, Configuration.TailAccuracy);
    }

    public InputCandidate? GetCandidate(NoteContext context, InputEvent input, int noteIndex)
    {
        var state = (HoldState)context.State;
        if (state.TailResolved)
        {
            var resolvedOffset = context.Time - context.Note.StartTime;
            if (input.Pressed && Configuration.ResolvedHeadWindows?.ResultFor(resolvedOffset) is { } resolvedResult)
                return new InputCandidate(noteIndex, context.Note.StartTime, resolvedOffset, Configuration.CandidatePriority?.Invoke(resolvedResult) ?? 1);
            return Configuration.Balance == null ? null : new InputCandidate(noteIndex, context.Note.StartTime, 0, 0, true);
        }

        if (state.HeadResult != null)
        {
            if (state.HeadOffset == null && state.HeadResult == Configuration.MissResult && !Configuration.RecoverMissedHead)
                return Configuration.Balance == null ? null : new InputCandidate(noteIndex, context.Note.StartTime, 0, 0, true);

            if (input.Pressed && Configuration.LatestRecoveryOffset is { } latest && context.Time > context.Note.EndTime + latest)
                return null;

            if (!input.Pressed && !state.Holding)
                return Configuration.Balance == null ? null : new InputCandidate(noteIndex, context.Note.StartTime, 0, 0, true);

            return new InputCandidate(noteIndex, context.Note.StartTime, context.Time - context.Note.EndTime, 0, Configuration.ObserveActiveInput);
        }

        var offset = context.Time - context.Note.StartTime;
        var result = input.Pressed ? Configuration.HeadWindows.ResultFor(offset) : null;
        return result == null ? null : new InputCandidate(noteIndex, context.Note.StartTime, offset, Configuration.CandidatePriority?.Invoke(result) ?? 1);
    }

    public NoteUpdate ApplyInput(NoteContext context, InputEvent input)
    {
        var state = integrate(context, (HoldState)context.State);
        var emissions = ImmutableArray.CreateBuilder<NoteEmission>();
        state = emitBalance(context, state, emissions);
        if (state.TailResolved && input.Pressed && Configuration.ResolvedHeadWindows?.ResultFor(context.Time - context.Note.StartTime) is { } ghost)
            emissions.Add(new NoteEmission("empty", ghost, context.Note.StartTime, context.Time - context.Note.StartTime));
        if (state.TailResolved || (state.HeadResult == Configuration.MissResult && state.HeadOffset == null && !Configuration.RecoverMissedHead))
            return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), emissions.ToImmutable());

        if (state.HeadResult != null && Configuration.EndMode == HoldEndMode.Automatic
                                     && context.Time >= context.Note.EndTime + Configuration.AutomaticEndOffset)
        {
            var valid = state.Holding || state.DropDeadline > context.Time;
            return resolve(context, state, valid ? Configuration.AutomaticResult : Configuration.DropResult, null, HoldResolution.Automatic, emissions);
        }

        if (state.HeadResult == null)
        {
            var offset = context.Time - context.Note.StartTime;
            var result = Configuration.HeadWindows.ResultFor(offset)!;
            if (Configuration.NonResolvingHeadResults.Contains(result))
                return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), [new NoteEmission("empty", result, context.Note.StartTime, offset)]);

            state = state with
            {
                HeadResult = result,
                HeadOffset = offset,
                Holding = true,
                Control = input.Control,
                Broken = false,
                DropDeadline = Configuration.RequiresRetrigger ? Math.Max(context.Time, context.Note.StartTime) + Configuration.Grace : null,
            };
            emissions.Add(new NoteEmission("head", result, context.Note.StartTime, offset));
            if (Configuration.TerminalHeadResults.Contains(result))
                return resolve(context, state, result, offset, HoldResolution.Head, emissions);
        }
        else if (input.Pressed)
        {
            if (Configuration.TailOnOppositeControl && input.Control != state.Control)
            {
                var offset = context.Time - context.Note.EndTime;
                var result = Configuration.TailWindows.ResultFor(offset) ?? Configuration.DropResult;
                return resolve(context, state, result, offset, HoldResolution.Release, emissions);
            }

            state = state with { Holding = true, DropDeadline = Configuration.RequiresRetrigger ? Math.Max(context.Time, context.Note.StartTime) + Configuration.Grace : null };
        }
        else if (Configuration.ReleaseByOwner ? input.Control == state.Control : !context.Input.Held)
        {
            var offset = context.Time - context.Note.EndTime;
            var result = Configuration.TailWindows.ResultFor(offset);
            if (Configuration.TailOnOppositeControl && result != null)
                return new NoteUpdate(schedule(context.Note, state with { Holding = false }, context.Time, context.Input.Held), emissions.ToImmutable());

            if (Configuration.EndMode != HoldEndMode.Automatic && !Configuration.TailOnOppositeControl && result != null)
                return resolve(context, state, result, offset, HoldResolution.Release, emissions);

            state = state with { Holding = false };

            switch (Configuration.DropMode)
            {
                case HoldDropMode.Resolve:
                    return resolve(context, state, Configuration.DropResult, offset, HoldResolution.Drop, emissions);

                case HoldDropMode.Recover:
                    if (!state.Broken && Configuration.BreakResult is { } bodyResult)
                        emissions.Add(new NoteEmission("body", bodyResult, context.Note.EndTime));
                    state = state with { Broken = true };
                    break;

                case HoldDropMode.Grace:
                    if (!Configuration.RequiresRetrigger)
                        state = state with { DropDeadline = Math.Max(context.Time, context.Note.StartTime) + Configuration.Grace };
                    break;
            }
        }

        if (Configuration.EndMode != HoldEndMode.Release && context.Time >= context.Note.EndTime + Configuration.AutomaticEndOffset)
        {
            var valid = state.Holding || state.DropDeadline > context.Time;
            return resolve(context, state, valid ? Configuration.AutomaticResult : Configuration.DropResult, null, HoldResolution.Automatic, emissions);
        }

        return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), emissions.ToImmutable());
    }

    public NoteUpdate ApplyTimer(NoteContext context)
    {
        var state = integrate(context, (HoldState)context.State);
        var emissions = ImmutableArray.CreateBuilder<NoteEmission>();

        if (state.TailResolved)
        {
            state = emitBalance(context, state, emissions);
            return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), emissions.ToImmutable());
        }

        if (state.HeadResult == null && context.Time > context.Note.StartTime + Configuration.HeadDeadline)
        {
            state = state with { HeadResult = Configuration.MissResult, Holding = false };
            emissions.Add(new NoteEmission("head", Configuration.MissResult, context.Note.StartTime));
            if (!Configuration.ContinueAfterMissedHead && !Configuration.RecoverMissedHead)
                return resolve(context, state, Configuration.MissResult, null, HoldResolution.MissedHead, emissions);
        }

        if (state.DropDeadline is { } drop && context.Time >= drop)
        {
            state = state with { DropDeadline = null, Broken = true };
            return resolve(context, state, Configuration.DropResult, null, HoldResolution.Drop, emissions);
        }

        state = emitBalance(context, state, emissions);

        var endTime = context.Note.EndTime + Configuration.AutomaticEndOffset;
        if (!state.TailResolved && Configuration.EndMode != HoldEndMode.Release && context.Time >= endTime)
        {
            var held = state.Holding || state.DropDeadline > context.Time;
            return resolve(context, state, held ? Configuration.AutomaticResult : Configuration.DropResult, null, HoldResolution.Automatic, emissions);
        }

        if (!state.TailResolved && context.Time > context.Note.EndTime + Configuration.TailDeadline)
            return resolve(context, state, Configuration.MissResult, null, HoldResolution.Timeout, emissions);

        return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), emissions.ToImmutable());
    }

    private HoldState emitBalance(NoteContext context, HoldState state, ImmutableArray<NoteEmission>.Builder emissions)
    {
        if (Configuration.Balance is { } balance && state.HeadResult != null && context.Time < context.Note.EndTime)
        {
            if (state.Balance > balance.Threshold)
            {
                emissions.Add(new NoteEmission("body", balance.GainResult, context.Time));
                state = state with { Balance = state.Balance - balance.Threshold };
            }
            else if (state.Balance < -balance.Threshold)
            {
                emissions.Add(new NoteEmission("body", balance.LossResult, context.Time));
                state = state with { Balance = state.Balance + balance.Threshold };
            }
        }

        return state;
    }

    private NoteUpdate resolve(NoteContext context, HoldState state, string tailResult, long? offset, HoldResolution reason,
                               ImmutableArray<NoteEmission>.Builder emissions)
    {
        var evaluation = new HoldEvaluation(context.Note, state.HeadResult ?? Configuration.MissResult, state.HeadOffset,
            tailResult, offset, state.Broken, reason);
        var result = Configuration.ResolveResult(evaluation);
        if (result is { } emission)
            emissions.Add(emission);

        if (tailResult == Configuration.MissResult && !state.Broken && Configuration.BreakResult is { } bodyResult)
        {
            emissions.Add(new NoteEmission("body", bodyResult, context.Note.EndTime));
            state = state with { Broken = true };
        }

        if (Configuration.Balance is { } balance && context.Time < context.Note.EndTime)
        {
            state = state with { TailResolved = true, TailSustains = result is { } tail && balance.SustainingTailResults.Contains(tail.Result), DropDeadline = null };
            return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), emissions.ToImmutable());
        }

        state = state with { TailResolved = true, DropDeadline = null };
        return new NoteUpdate(schedule(context.Note, state, context.Time, context.Input.Held), emissions.ToImmutable());
    }

    private HoldState integrate(NoteContext context, HoldState state)
    {
        if (Configuration.Balance == null || state.HeadResult == null)
            return state with { BalanceTime = Math.Max(context.Time, context.Note.StartTime) };

        var until = Math.Clamp(context.Time, context.Note.StartTime, context.Note.EndTime);
        var duration = Math.Max(0, until - state.BalanceTime);
        // Input context contains both sides of the edge; integration must use the preceding interval.
        var held = context.Input.PreviouslyHeld || state.TailSustains;
        return state with { Balance = state.Balance + (held ? duration : -duration), BalanceTime = until };
    }

    private HoldState schedule(ChartNote note, HoldState state, long now, bool held)
    {
        if (state.TailResolved)
        {
            var until = Configuration.ResolvedHeadWindows?.Entries.Max(w => note.StartTime + w.Late + 1) ?? now;
            if (Configuration.Balance != null)
                until = Math.Max(until, note.EndTime + Configuration.AutomaticEndOffset);
            if (now >= until)
                return state with { Timer = null, Done = true };
            if (Configuration.Balance == null || now >= note.EndTime)
                return state with { Timer = new NoteTimer(until), Done = false };
        }

        long? next = null;

        void consider(long? candidate)
        {
            if (candidate is { } candidateTime && candidateTime > now && (next == null || candidateTime < next))
                next = candidateTime;
        }

        if (state.HeadResult == null)
            consider(note.StartTime + Configuration.HeadDeadline + 1);

        if (Configuration.EndMode == HoldEndMode.Release && !state.TailResolved)
            consider(note.EndTime + Configuration.TailDeadline + 1);
        else if (state.HeadResult != null)
            consider(note.EndTime + Configuration.AutomaticEndOffset);
        consider(state.DropDeadline);

        if (Configuration.Balance is { } balance && state.HeadResult != null && now < note.EndTime)
        {
            var from = Math.Max(now, note.StartTime);
            consider(from + (held || state.TailSustains ? balance.Threshold - state.Balance : balance.Threshold + state.Balance) + 1);
        }

        var beforeInput = (next == note.StartTime + Configuration.HeadDeadline + 1 && state.HeadResult == null)
                          || next == state.DropDeadline
                          || (next == note.EndTime + Configuration.TailDeadline + 1 && Configuration.EndMode == HoldEndMode.Release);
        return state with { Timer = next is { } time ? new NoteTimer(time, beforeInput ? TimerOrder.BeforeInput : TimerOrder.AfterInput) : null };
    }
}
