using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsJudgementColumnDefinition(BmsHitObject[] Objects, BmsLayoutVariant Layout, int Index, bool BatchUpdates, IBmsJudgementOutput Output);

internal sealed class BmsJudgementRules
{
    internal readonly BmsNoteJudgementRule Note = new();
    internal readonly BmsLongNoteJudgementRule LongNote = new();
    internal readonly BmsChargeNoteJudgementRule ChargeNote = new();
    internal readonly BmsHellChargeNoteJudgementRule HellChargeNote = new();
    internal readonly BmsLandmineJudgementRule Landmine = new();
}

/// <summary>
/// Owns column state and the ordered simulation phases, independently of representations and clocks.
/// </summary>
internal sealed class BmsJudgementEngine
{
    private readonly BmsJudgementRules rules = new();
    private readonly BmsColumnJudgementRuntime[] columns;
    private readonly BmsColumnJudgementRuntime[] columnsByIndex;
    private readonly List<BmsJudgementInput> pendingInputs = [];
    private readonly int[] framePressedDirections;
    private long scheduleEpoch;
    private long scheduleStep;
    private long scheduleBatch;
    private double scheduleTime = double.NaN;
    private bool processing;

    internal BmsJudgementEngine(params BmsJudgementColumnDefinition[] definitions)
    {
        if (definitions.Length == 0 || definitions.Any(d => d.Index < 0) || definitions.Select(d => d.Index).Distinct().Count() != definitions.Length)
            throw new ArgumentException("Each judgement column needs a unique non-negative index.", nameof(definitions));
        columns = new BmsColumnJudgementRuntime[definitions.Length];
        columnsByIndex = new BmsColumnJudgementRuntime[definitions.Max(d => d.Index) + 1];
        framePressedDirections = new int[columnsByIndex.Length];
        for (var i = 0; i < definitions.Length; i++)
        {
            var definition = definitions[i];
            var column = new BmsColumnJudgementRuntime(definition.Objects, definition.Layout, definition.Index, definition.BatchUpdates, definition.Output, rules);
            columns[i] = column;
            columnsByIndex[column.Index] = column;
        }
    }

    internal BmsJudgementEngine(BmsHitObject[] notes, BmsLayoutVariant layout, int column, bool batchUpdates, IBmsJudgementOutput output)
        : this(new BmsJudgementColumnDefinition(notes, layout, column, batchUpdates, output))
    {
    }

    internal bool HasPendingInputs => pendingInputs.Count > 0;

    internal void EnableAutoScratch()
    {
        foreach (var column in columns)
            column.EnableAutoScratch();
    }

    internal BmsColumnJudgementRuntime GetColumn(int index)
        => index >= 0 && index < columnsByIndex.Length && columnsByIndex[index] is { } column ? column
            : throw new ArgumentOutOfRangeException(nameof(index));

    internal void QueueInput(double time, int column, bool pressed, bool reverseScratch)
    {
        if (GetColumn(column).AutoScratchEnabled)
            return;
        pendingInputs.Add(new BmsJudgementInput(time, column, pressed, reverseScratch));
    }

    internal void ProcessFrame(in BmsFrameContext frame, double elapsed, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo,
                               bool suppressJudgements = false, bool resumeRewinding = false)
    {
        if (processing)
            throw new InvalidOperationException("Result observers may queue inputs, but cannot recursively process a frame.");
        processing = true;
        var inputCount = pendingInputs.Count;
        try
        {
            if (frame.SimulationTime != scheduleTime)
            {
                if (frame.SimulationTime < scheduleTime)
                    scheduleEpoch++;
                scheduleTime = frame.SimulationTime;
                scheduleStep++;
            }
            scheduleBatch++;
            foreach (var column in columns)
            {
                column.Schedule.BeginBatch(scheduleEpoch, scheduleStep, scheduleBatch);
                framePressedDirections[column.Index] = column.PressedDirections;
            }

            for (var i = 0; i < inputCount; i++)
            {
                var input = pendingInputs[i];
                var direction = columnsByIndex[input.Column].IsScratch && input.ReverseScratch ? 2 : 1;
                if (input.Pressed)
                    framePressedDirections[input.Column] |= direction;
                else
                    framePressedDirections[input.Column] &= ~direction;
            }

            if (suppressJudgements || frame.IsRewinding || elapsed < 0)
            {
                foreach (var column in columns)
                    column.PressedDirections = framePressedDirections[column.Index];
                return;
            }

            // The batch's final physical state drives all mines, then all bodies, before its ordered inputs.
            if (!resumeRewinding)
            {
                foreach (var column in columns)
                    column.PrepareAutomaticHolds();
                foreach (var column in columns)
                    column.UpdateLandmines(frame, framePressedDirections[column.Index] != 0);
                foreach (var column in columns)
                    column.UpdateRegisteredBodies(frame, elapsed, framePressedDirections[column.Index] != 0);
            }

            for (var i = 0; i < inputCount; i++)
            {
                var input = pendingInputs[i];
                var column = columnsByIndex[input.Column];
                if (input.Pressed)
                {
                    var outcome = column.HandleNotePress(new BmsPressContext(input.Time, frame, input.ReverseScratch, resumeRewinding), algorithm);
                    if (outcome is { Kind: PressOutcomeKind.EmptyPoor, ExpectedTime: { } expectedTime })
                        column.ApplyEmptyPoor(expectedTime, frame);
                }
                else
                    column.HandleExecutionRelease(input.Time, frame, input.ReverseScratch);
            }

            // Automation consumes simulation steps, so deferred and final visual traversals share one outcome.
            if (!resumeRewinding)
            {
                foreach (var column in columns)
                    column.UpdateAutoScratch(frame);
            }
        }
        finally
        {
            // Synchronous feedback may enqueue another batch at the same timestamp.
            pendingInputs.RemoveRange(0, inputCount);
            processing = false;
        }
    }

    internal void CompleteLongNotesAtPause(in BmsFrameContext frame)
    {
        foreach (var column in columns)
            column.CompleteLongNotesAtPause(frame);
    }

    private readonly record struct BmsJudgementInput(double Time, int Column, bool Pressed, bool ReverseScratch);
}
