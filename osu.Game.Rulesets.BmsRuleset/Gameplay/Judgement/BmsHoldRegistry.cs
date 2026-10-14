using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>
/// Owns typed hold state and resolves a note slot to one immutable operation binding.
/// Rules remain stateless; only this registry knows how their state is stored.
/// </summary>
internal sealed class BmsHoldRegistry
{
    private readonly Binding[] bindings;
    private readonly int[] bindingIndices;
    private readonly HellChargeOperations? hellCharge;

    internal BmsPressOperation PressOperation { get; }

    internal BmsPassiveOperation PassiveOperation { get; }

    internal BmsHoldRegistry(BmsHitObject[] notes, BmsJudgementRules rules, BmsJudgementExecutionContext execution)
    {
        // A mixed column needs one compact index per note, but binding records only for actual holds.
        bindings = new Binding[notes.Count(n => n is BmsLongNote)];
        bindingIndices = bindings.Length == 0 ? [] : new int[notes.Length];
        Array.Fill(bindingIndices, -1);
        var bindingIndex = 0;
        LongNoteOperations? normal = null;
        ChargeOperations? charge = null;
        for (var slot = 0; slot < notes.Length; slot++)
        {
            if (notes[slot] is not BmsLongNote note) continue;

            HoldOperations operations = mode(note) switch
            {
                BmsLongNoteMode.LongNote => normal ??= new LongNoteOperations(rules.LongNote, execution),
                BmsLongNoteMode.ChargeNote => charge ??= new ChargeOperations(rules.ChargeNote, execution),
                BmsLongNoteMode.HellChargeNote => hellCharge ??= new HellChargeOperations(rules.HellChargeNote, execution),
                _ => throw new InvalidOperationException("Unknown long-note mode."),
            };
            bindingIndices[slot] = bindingIndex;
            bindings[bindingIndex++] = new Binding(note, operations, operations.Add(note));
        }

        PressOperation = press;
        PassiveOperation = checkPassive;
    }

    private static BmsLongNoteMode mode(BmsLongNote note) => note.Beatmap.LockedLongNoteMode == BmsLongNoteMode.Undefined
        ? BmsLongNoteMode.LongNote
        : note.Beatmap.LockedLongNoteMode;

    internal BmsLongNote GetNote(int slot) => bindings[bindingIndices[slot]].Note;

    internal BmsLongNoteState GetState(int slot)
    {
        var binding = bindings[bindingIndices[slot]];
        return binding.Operations.GetState(binding.StateIndex);
    }

    internal double NextEventTime(int slot)
    {
        var binding = bindings[bindingIndices[slot]];
        return binding.Operations.NextEventTime(binding.StateIndex);
    }

    private bool press(int slot, HitResult result, in BmsPressContext press)
    {
        var binding = bindings[bindingIndices[slot]];
        return binding.Operations.Press(binding.StateIndex, result, press);
    }

    private void checkPassive(int slot, in BmsFrameContext frame)
    {
        var binding = bindings[bindingIndices[slot]];
        binding.Operations.CheckPassive(binding.StateIndex, frame);
    }

    internal bool Repress(int slot, in BmsPressContext press)
    {
        var binding = bindings[bindingIndices[slot]];
        return binding.Operations.Repress(binding.StateIndex, press);
    }

    internal bool Release(int slot, in BmsReleaseContext release)
    {
        var binding = bindings[bindingIndices[slot]];
        return binding.Operations.Release(binding.StateIndex, release);
    }

    internal void Finish(int slot, in BmsFrameContext frame, double elapsed, bool holding)
    {
        var binding = bindings[bindingIndices[slot]];
        binding.Operations.Finish(binding.StateIndex, frame, elapsed, holding);
    }

    internal bool CompleteAtEnd(int slot, in BmsFrameContext frame)
    {
        var binding = bindings[bindingIndices[slot]];
        return binding.Operations.CompleteAtEnd(binding.StateIndex, frame);
    }

    internal void Rewind(double time)
    {
        // Inactive holds retain history so pool activation cannot resurrect a future head.
        foreach (var binding in bindings)
            binding.Operations.Rewind(binding.StateIndex, time);
    }

    internal void UpdateBody(int slot, in BmsFrameContext frame, double elapsed, bool holding)
        => hellCharge!.UpdateBody(bindings[bindingIndices[slot]].StateIndex, frame, elapsed, holding);

    internal double NextBodyTime(int slot, bool holding)
        => hellCharge!.NextBodyTime(bindings[bindingIndices[slot]].StateIndex, holding);

    internal void Configure(int slot, bool preserveHistory)
    {
        var binding = bindings[bindingIndices[slot]];
        if (mode(binding.Note) != binding.Operations.Mode)
            throw new InvalidOperationException("Changing a registered long-note mode requires a new gameplay engine.");

        binding.Operations.Configure(binding.StateIndex, preserveHistory);
    }

    private readonly record struct Binding(BmsLongNote Note, HoldOperations Operations, int StateIndex);

    // Every hold supports these operations. Ordinary notes never acquire an empty hold capability.
    private abstract class HoldOperations(BmsJudgementExecutionContext execution)
    {
        protected readonly BmsJudgementExecutionContext Execution = execution;

        internal abstract BmsLongNoteMode Mode { get; }

        internal abstract int Add(BmsLongNote note);

        internal abstract BmsLongNoteState GetState(int index);

        internal abstract double NextEventTime(int index);

        internal abstract bool Press(int index, HitResult result, in BmsPressContext press);

        internal abstract void CheckPassive(int index, in BmsFrameContext frame);

        internal abstract bool Repress(int index, in BmsPressContext press);

        internal abstract bool Release(int index, in BmsReleaseContext release);

        internal abstract void Finish(int index, in BmsFrameContext frame, double elapsed, bool holding);

        internal abstract bool CompleteAtEnd(int index, in BmsFrameContext frame);

        internal abstract void Rewind(int index, double time);

        internal abstract void Configure(int index, bool preserveHistory);
    }

    private sealed class LongNoteOperations(BmsLongNoteJudgementRule rule, BmsJudgementExecutionContext execution) : HoldOperations(execution)
    {
        private readonly List<BmsLongNoteRuntime> states = [];

        internal override BmsLongNoteMode Mode => BmsLongNoteMode.LongNote;

        internal override int Add(BmsLongNote note)
        {
            var index = states.Count;
            states.Add(new BmsLongNoteRuntime(note));
            return index;
        }

        internal override BmsLongNoteState GetState(int index) => states[index].State;

        internal override double NextEventTime(int index) => states[index].Hold.NextEventTime(Mode);

        internal override bool Press(int index, HitResult result, in BmsPressContext press) => rule.TryPress(states[index], Execution, result, press);

        internal override void CheckPassive(int index, in BmsFrameContext frame) => rule.CheckPassive(states[index], Execution, frame);

        internal override bool Repress(int index, in BmsPressContext press) => rule.TryRepress(states[index], Execution, press);

        internal override bool Release(int index, in BmsReleaseContext release) => rule.TryRelease(states[index], Execution, release);

        internal override void Finish(int index, in BmsFrameContext frame, double elapsed, bool holding) => rule.Finish(states[index], Execution, frame, elapsed, holding);

        internal override bool CompleteAtEnd(int index, in BmsFrameContext frame) => rule.CompleteAtEnd(states[index], Execution, frame);

        internal override void Rewind(int index, double time) => rule.Rewind(states[index], time);

        internal override void Configure(int index, bool preserveHistory)
        {
            states[index].Hold.RefreshWindows();
        }
    }

    private sealed class ChargeOperations(BmsChargeNoteJudgementRule rule, BmsJudgementExecutionContext execution) : HoldOperations(execution)
    {
        private readonly List<BmsChargeNoteRuntime> states = [];

        internal override BmsLongNoteMode Mode => BmsLongNoteMode.ChargeNote;

        internal override int Add(BmsLongNote note)
        {
            var index = states.Count;
            states.Add(new BmsChargeNoteRuntime(note));
            return index;
        }

        internal override BmsLongNoteState GetState(int index) => states[index].State;

        internal override double NextEventTime(int index) => states[index].Hold.NextEventTime(Mode);

        internal override bool Press(int index, HitResult result, in BmsPressContext press) => rule.TryPress(states[index], Execution, result, press);

        internal override void CheckPassive(int index, in BmsFrameContext frame) => rule.CheckPassive(states[index], Execution, frame);

        internal override bool Repress(int index, in BmsPressContext press) => rule.TryRepress(states[index], Execution, press);

        internal override bool Release(int index, in BmsReleaseContext release) => rule.TryRelease(states[index], Execution, release);

        internal override void Finish(int index, in BmsFrameContext frame, double elapsed, bool holding) => rule.Finish(states[index], Execution, frame, elapsed, holding);

        internal override bool CompleteAtEnd(int index, in BmsFrameContext frame) => rule.CompleteAtEnd(states[index], Execution, frame);

        internal override void Rewind(int index, double time) => rule.Rewind(states[index], time);

        internal override void Configure(int index, bool preserveHistory)
        {
            states[index].Hold.RefreshWindows();
        }
    }

    private sealed class HellChargeOperations(BmsHellChargeNoteJudgementRule rule, BmsJudgementExecutionContext execution) : HoldOperations(execution)
    {
        private readonly List<BmsHellChargeNoteRuntime> states = [];

        internal override BmsLongNoteMode Mode => BmsLongNoteMode.HellChargeNote;

        internal override int Add(BmsLongNote note)
        {
            var index = states.Count;
            states.Add(new BmsHellChargeNoteRuntime(note));
            return index;
        }

        internal override BmsLongNoteState GetState(int index) => states[index].State;

        internal override double NextEventTime(int index) => states[index].Hold.NextEventTime(Mode);

        internal override bool Press(int index, HitResult result, in BmsPressContext press) => rule.TryPress(states[index], Execution, result, press);

        internal override void CheckPassive(int index, in BmsFrameContext frame) => rule.CheckPassive(states[index], Execution, frame);

        internal override bool Repress(int index, in BmsPressContext press) => rule.TryRepress(states[index], Execution, press);

        internal override bool Release(int index, in BmsReleaseContext release) => rule.TryRelease(states[index], Execution, release);

        internal override void Finish(int index, in BmsFrameContext frame, double elapsed, bool holding) => rule.Finish(states[index], Execution, frame, elapsed, holding);

        internal override bool CompleteAtEnd(int index, in BmsFrameContext frame) => rule.CompleteAtEnd(states[index], Execution, frame);

        internal override void Rewind(int index, double time) => rule.Rewind(states[index], time);

        internal override void Configure(int index, bool preserveHistory)
        {
            states[index].Hold.RefreshWindows();
            states[index].Body.HistoryEnabled = preserveHistory;
        }

        internal void UpdateBody(int index, in BmsFrameContext frame, double elapsed, bool holding)
            => rule.UpdateBody(states[index], Execution, frame, elapsed, holding);

        internal double NextBodyTime(int index, bool holding) => rule.NextBodyTime(states[index], holding);
    }
}
