using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal readonly record struct BmsJudgementColumnDefinition(BmsHitObject[] Objects, BmsLayoutVariant Layout, int Index, IBmsJudgementOutput Output);

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
    private readonly BmsColumnJudgementRuntime[] bodyColumns;
    private readonly BmsColumnJudgementRuntime[] columnsByIndex;
    private readonly List<BmsJudgementInput> pendingInputs = [];
    private double scheduleTime = double.NaN;
    private bool processing;

    internal BmsJudgementEngine(params BmsJudgementColumnDefinition[] definitions)
    {
        if (definitions.Length == 0 || definitions.Any(d => d.Index < 0) || definitions.Select(d => d.Index).Distinct().Count() != definitions.Length)
            throw new ArgumentException(@"Each judgement column needs a unique non-negative index.", nameof(definitions));

        columns = new BmsColumnJudgementRuntime[definitions.Length];
        columnsByIndex = new BmsColumnJudgementRuntime[definitions.Max(d => d.Index) + 1];
        // beatoraja visits each side's ordinary lanes before its scratch; gauge clipping depends on this order.
        var firstScratchLane = definitions[0].Layout switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => 5,
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => 7,
            _ => -1,
        };
        var orderedDefinitions = definitions.OrderBy(definition => firstScratchLane < 0 || definition.Index > firstScratchLane
            ? definition.Index
            : definition.Index == 0
                ? firstScratchLane
                : definition.Index - 1).ToArray();
        for (var i = 0; i < orderedDefinitions.Length; i++)
        {
            var definition = orderedDefinitions[i];
            var column = new BmsColumnJudgementRuntime(definition.Objects, definition.Layout, definition.Index, definition.Output, rules);
            columns[i] = column;
            columnsByIndex[column.Index] = column;
        }

        bodyColumns = columns.Where(column => column.NoteRegistrations.Any(registration =>
            registration.Object is BmsLongNote { Beatmap.LockedLongNoteMode: BmsLongNoteMode.HellChargeNote })).ToArray();
    }

    internal BmsJudgementEngine(BmsHitObject[] notes, BmsLayoutVariant layout, int column, IBmsJudgementOutput output)
        : this(new BmsJudgementColumnDefinition(notes, layout, column, output))
    {
    }

    internal bool HasPendingInputs => pendingInputs.Count > 0;

    internal double CurrentTime => scheduleTime;

    internal double NextEventTime
    {
        get
        {
            var next = double.PositiveInfinity;
            foreach (var column in columns)
            {
                next = Math.Min(next, column.NextAutomaticTime);
            }

            foreach (var column in bodyColumns)
                next = Math.Min(next, column.NextBodyTime(column.PressedDirections != 0));

            return next;
        }
    }

    internal BmsColumnJudgementRuntime GetColumn(int index)
        => index >= 0 && index < columnsByIndex.Length && columnsByIndex[index] is { } column
            ? column
            : throw new ArgumentOutOfRangeException(nameof(index));

    internal void QueueInput(double time, int column, bool pressed, bool reverseScratch)
    {
        GetColumn(column);
        pendingInputs.Add(new BmsJudgementInput(time, column, pressed, reverseScratch));
    }

    internal void ProcessFrame(in BmsFrameContext frame, double elapsed, BmsJudgementAlgorithm? algorithm = BmsJudgementAlgorithm.Combo)
    {
        if (processing)
            throw new InvalidOperationException("Result observers may queue inputs, but cannot recursively process a frame.");

        processing = true;
        var inputCount = pendingInputs.Count;
        try
        {
            scheduleTime = frame.SimulationTime;

            if (frame.IsRewinding || elapsed < 0)
            {
                for (var i = 0; i < inputCount; i++) applyInputState(pendingInputs[i]);
                return;
            }

            for (var i = 0; i < inputCount;)
            {
                var time = pendingInputs[i].Time;
                advance(time, frame, false);
                var inputFrame = frame with { SimulationTime = time };
                updateBodies(inputFrame);
                do
                {
                    var input = pendingInputs[i++];
                    var column = columnsByIndex[input.Column];
                    if (input.Pressed)
                    {
                        var outcome = column.HandleNotePress(new BmsPressContext(time, inputFrame, input.ReverseScratch), algorithm);
                        if (outcome is { Kind: PressOutcomeKind.EmptyPoor, ExpectedTime: { } expectedTime })
                            column.ApplyEmptyPoor(expectedTime, inputFrame);
                    }
                    else column.HandleExecutionRelease(time, inputFrame, input.ReverseScratch);
                } while (i < inputCount && pendingInputs[i].Time == time);

                updateBodies(inputFrame);
                foreach (var column in columns) column.ProcessAutomatic(inputFrame);
            }

            advance(frame.SimulationTime, frame, true);
            updateBodies(frame);
        }
        finally
        {
            // Synchronous feedback may enqueue another batch at the same timestamp.
            pendingInputs.RemoveRange(0, inputCount);
            processing = false;
        }
    }

    internal void CompleteLongNotesAtEnd(in BmsFrameContext frame)
    {
        advance(frame.SimulationTime, frame, false);
        updateBodies(frame);
        foreach (var column in columns) column.CompleteLongNotesAtEnd(frame);
        foreach (var column in columns) column.ProcessAutomatic(frame);
    }

    private void advance(double time, in BmsFrameContext frame, bool inclusive)
    {
        while (true)
        {
            var next = NextEventTime;
            if (next > time || (!inclusive && next == time) || !double.IsFinite(next)) return;

            var step = frame with { SimulationTime = next };
            updateBodies(step);
            foreach (var column in columns) column.ProcessAutomatic(step);
        }
    }

    private void updateBodies(in BmsFrameContext frame)
    {
        foreach (var column in bodyColumns)
            column.UpdateRegisteredBodies(frame, 0, column.PressedDirections != 0);
    }

    private void applyInputState(in BmsJudgementInput input)
    {
        var column = columnsByIndex[input.Column];
        var direction = column.IsScratch && input.ReverseScratch ? 2 : 1;
        if (input.Pressed) column.PressedDirections |= direction;
        else column.PressedDirections &= ~direction;
    }

    private readonly record struct BmsJudgementInput(double Time, int Column, bool Pressed, bool ReverseScratch);
}
