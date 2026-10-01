using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Judgements;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

/// <summary>
/// Coordinates the judgement phases for one playfield.
/// </summary>
/// <remarks>
/// Column contexts own input selection, indexes and long-note history. Landmine rules continue
/// to use their existing drawable adapters until their migration stage.
/// </remarks>
internal sealed class BmsJudgementEngine
{
    private readonly BmsPlayfield playfield = null!;
    private readonly BmsColumnJudgementContext[] columns;
    private readonly List<BmsJudgementInput> pendingInputs = [];
    private readonly int[] framePressedDirections;
    private long scheduleEpoch;
    private long scheduleStep;
    private long scheduleBatch;
    private double scheduleTime = double.NaN;

    internal BmsJudgementEngine(BmsPlayfield playfield, BmsColumn[] orderedColumns)
    {
        this.playfield = playfield;
        columns = new BmsColumnJudgementContext[orderedColumns.Length];
        framePressedDirections = new int[playfield.TotalColumns];

        for (var i = 0; i < orderedColumns.Length; i++)
        {
            var context = new BmsColumnJudgementContext(playfield, orderedColumns[i]);
            columns[i] = context;
            context.Column.Judgements = context;
            context.Container.Judgements = context;
        }
    }

    internal bool HasPendingInputs => pendingInputs.Count > 0;

    // The bounded data host executes supplied operations via GetColumn; frame-plan generation is still the legacy host's responsibility.
    internal BmsJudgementEngine(BmsHitObject[] notes, BmsLayoutVariant layout, int column, bool canBatchUpdates, IBmsJudgementOutput output)
    {
        columns = [new BmsColumnJudgementContext(notes, layout, column, canBatchUpdates, output)];
        framePressedDirections = new int[column + 1];
    }

    internal BmsColumnJudgementContext GetColumn(int index) => columns.Single(c => c.Index == index);

    internal void EnableNoteExecution(int column) => GetColumn(column).EnableNoteExecution();
    internal void EnableLongNoteExecution(int column) => GetColumn(column).EnableNoteExecution(true);

    internal void EnableCoreScheduling()
    {
        if (playfield.Beatmap.HitObjects.Any(n => n is BmsLongNote) && playfield.Beatmap.LockedLongNoteMode != BmsLongNoteMode.HellChargeNote)
            throw new InvalidOperationException("The bounded scheduling path supports ordinary notes and HCN only.");
        foreach (var column in columns)
        {
            column.EnableNoteExecution(true);
            column.EnableCoreScheduling();
        }
    }

    internal void QueueInput(double time, int column, bool pressed, bool reverseScratch)
        => pendingInputs.Add(new BmsJudgementInput(time, column, pressed, reverseScratch));

    internal void ProcessFrame(bool suppressJudgements = false)
    {
        var time = playfield.Time.Current;
        if (time != scheduleTime)
        {
            if (time < scheduleTime) scheduleEpoch++;
            scheduleTime = time;
            scheduleStep++;
        }
        scheduleBatch++;
        foreach (var context in columns)
            context.Schedule?.BeginBatch(scheduleEpoch, scheduleStep, scheduleBatch);

        foreach (var context in columns)
            framePressedDirections[context.Index] = context.PressedDirections;

        foreach (var input in pendingInputs)
        {
            var column = playfield.Stage.Columns[input.Column];
            var direction = column.IsScratch && input.ReverseScratch ? 2 : 1;
            if (input.Pressed)
                framePressedDirections[input.Column] |= direction;
            else
                framePressedDirections[input.Column] &= ~direction;
        }

        if (suppressJudgements || (playfield.Clock as IGameplayClock)?.IsRewinding == true || playfield.Time.Elapsed < 0)
        {
            // Rewinding updates physical key state while result stacks restore the judgement state.
            foreach (var context in columns)
                context.PressedDirections = framePressedDirections[context.Index];

            pendingInputs.Clear();
            return;
        }

        // Keep the phase boundaries explicit: passing mines precede HCN bodies, both use the
        // final physical state for this input batch, and input endpoints run afterwards.
        if (!playfield.IsResumeRewinding)
        {
            foreach (var context in columns)
                context.Container.UpdateLandmines(framePressedDirections[context.Index] != 0);

            foreach (var context in columns)
                context.UpdateHellChargeBodies(framePressedDirections[context.Index] != 0);
        }

        foreach (var input in pendingInputs)
        {
            var column = playfield.Stage.Columns[input.Column];
            if (input.Pressed)
            {
                var outcome = column.HandlePress(input.Time, input.ReverseScratch);
                if (outcome is { Kind: PressOutcomeKind.EmptyPoor, ExpectedTime: { } expectedTime })
                    playfield.RegisterEmptyPoor(expectedTime, outcome.Column);
            }
            else
                column.HandleRelease(input.Time, input.ReverseScratch);
        }

        pendingInputs.Clear();
    }

    internal void CompleteLongNotesAtPause(double currentTime)
    {
        foreach (var context in columns)
            context.CompleteLongNotesAtPause(currentTime);
    }

    private readonly struct BmsJudgementInput(double time, int column, bool pressed, bool reverseScratch)
    {
        internal double Time { get; } = time;
        internal int Column { get; } = column;
        internal bool Pressed { get; } = pressed;
        internal bool ReverseScratch { get; } = reverseScratch;
    }

}
