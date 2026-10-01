using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal readonly record struct BmsLongNoteRegistration(BmsLongNote Object, BmsLongNoteJudgementController State);

/// <summary>
/// Transitional long-note calculation bridge. P3a replaces the controller and this legacy sink together.
/// The binding is shared by a column; it borrows the operation frame only during a synchronous call.
/// </summary>
internal sealed class BmsLongNoteOperationBinding : IBmsLongNoteJudgementSink
{
    private readonly BmsColumnJudgementContext column;
    private readonly BmsJudgementExecutionContext execution;
    private BmsFrameContext frame;
    private BmsLongNoteState publishedState;
    private BmsLongNote currentNote = null!;
    private readonly Action<bool, double> bodyTick;

    internal BmsLongNoteOperationBinding(BmsColumnJudgementContext column, IBmsJudgementOutput output)
    {
        this.column = column;
        execution = new BmsJudgementExecutionContext(column, output);
        Press = press;
        Passive = passive;
        bodyTick = (holding, scale) => execution.ApplyBodyTick(currentNote, holding, scale, frame);
    }

    internal BmsPressOperation Press { get; }
    internal BmsPassiveOperation Passive { get; }

    private BmsLongNoteRegistration begin(int slot, in BmsFrameContext operationFrame)
    {
        frame = operationFrame;
        var registration = column.LongNoteRegistrations[slot];
        currentNote = registration.Object;
        publishedState = registration.State.State;
        return registration;
    }

    private void publish(BmsLongNote note)
    {
        publishedState = column.GetLongNoteState(note);
        execution.PublishState(note, publishedState, frame);
    }

    private void finish(BmsLongNoteRegistration registration)
    {
        // Deferred release and passive POOR also change state without a legacy visual callback.
        if (registration.State.State != publishedState)
            publish(registration.Object);
    }

    private bool press(int slot, HitResult result, in BmsPressContext press)
    {
        var registration = begin(slot, press.Frame);
        var success = !execution.GetMainResult(registration.Object).HasResult
                      && registration.State.TryHit(this, frame.SimulationTime, result, frame.GameplayRate, press.ReverseScratch);
        finish(registration);
        return success;
    }

    private void passive(int slot, in BmsFrameContext operationFrame)
    {
        var registration = begin(slot, operationFrame);
        if (!frame.IsRewinding)
        {
            if (!execution.GetMainResult(registration.Object).HasResult)
                registration.State.CheckPassiveResult(this, frame.SimulationTime, frame.GameplayRate);
            else if (registration.State.State.HasPendingTail)
                // A CN head owns the main result; the pending tail keeps its separate post-result entry.
                registration.State.UpdatePostResult(this, frame.SimulationTime, 0, false, frame.GameplayRate);
        }
        finish(registration);
    }

    internal bool Repress(int slot, in BmsPressContext press)
    {
        var registration = begin(slot, press.Frame);
        var success = registration.State.TryRepress(this, press.InputTime, press.ReverseScratch, frame.GameplayRate);
        finish(registration);
        return success;
    }

    internal bool Release(int slot, double inputTime, in BmsFrameContext operationFrame, bool reverseScratch)
    {
        var registration = begin(slot, operationFrame);
        var note = registration.Object;
        var table = BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant, note.Column, note.EffectiveJudgementRate, true);
        var success = registration.State.TryRelease(this, frame.SimulationTime, inputTime - note.EndTime, table, frame.GameplayRate, reverseScratch);
        finish(registration);
        return success;
    }

    internal void PostResult(int slot, in BmsFrameContext operationFrame, double elapsed, bool holding)
    {
        var registration = begin(slot, operationFrame);
        registration.State.UpdatePostResult(this, frame.SimulationTime, elapsed, holding, frame.GameplayRate);
        finish(registration);
    }

    internal void UpdateBody(int slot, in BmsFrameContext operationFrame, double elapsed, bool holding)
    {
        var registration = begin(slot, operationFrame);
        registration.State.UpdateHellChargeBody(this, frame.SimulationTime, elapsed, holding);
        finish(registration);
    }

    internal bool CompleteAtPause(int slot, in BmsFrameContext operationFrame)
    {
        var registration = begin(slot, operationFrame);
        var success = registration.State.CompleteAtPause(this, frame.SimulationTime, frame.GameplayRate);
        // The pause's final freeze follows the endpoint's own synchronous feedback checkpoint.
        frame = frame with { IsPauseCompletion = true };
        finish(registration);
        return success;
    }

    void IBmsLongNoteJudgementSink.OnUserHeadJudged(BmsLongNote note) => publish(note);
    void IBmsLongNoteJudgementSink.ClearVisualIfTailWasNotPoor(BmsLongNote note, HitResult result) => publish(note);
    void IBmsLongNoteJudgementSink.Retire(BmsLongNote note) => publish(note);
    void IBmsLongNoteJudgementSink.ApplyJudgementResult(BmsLongNote note, HitResult result, IReadOnlyList<BmsLongNoteEndpointResult> endpoints)
        => execution.ApplyMainResult(note, result, endpoints, frame);

    void IBmsLongNoteJudgementSink.OnHellChargeHeadPoor(BmsLongNote note, double eventTime, double lifetimeEnd)
        => publish(note);
    void IBmsLongNoteJudgementSink.ApplySyntheticEndpoint(BmsLongNoteEndpointResult endpoint)
        => execution.ApplySyntheticEndpoint(endpoint, frame);
    Action<bool, double> IBmsLongNoteJudgementSink.HellChargeTick => bodyTick;
}
