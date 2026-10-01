using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal delegate BmsLongNoteState BmsHoldStateQuery(int slot);
internal delegate bool BmsRepressOperation(int slot, in BmsPressContext press);
internal delegate bool BmsReleaseOperation(int slot, in BmsReleaseContext release);
internal delegate void BmsFinishOperation(int slot, in BmsFrameContext frame, double elapsed, bool holding);
internal delegate bool BmsPauseOperation(int slot, in BmsFrameContext frame);
internal delegate bool BmsAutomaticTailOperation(int slot, in BmsFrameContext frame);

internal sealed partial class BmsColumnJudgementContext
{
    private readonly List<BmsLongNoteRuntime> normalHoldStates = [];
    private readonly List<BmsChargeNoteRuntime> chargeHoldStates = [];
    private readonly List<BmsHellChargeNoteRuntime> hellChargeHoldStates = [];
    private int[] holdStateSlots = [];
    private int[] holdOperationSlots = [];
    private BmsHoldStateQuery[] holdQueries = [];
    private BmsRepressOperation[] repressOperations = [];
    private BmsReleaseOperation[] releaseOperations = [];
    private BmsFinishOperation[] finishOperations = [];
    private BmsPauseOperation[] pauseOperations = [];
    private BmsAutomaticTailOperation[] automaticTailOperations = [];
    private Action<int, double>[] rewindOperations = [];
    private Action<int, bool>[] configurationOperations = [];
    private Func<int, double>[] headDeadlines = [];
    private BmsLongNoteMode[] registeredHoldModes = [];

    private void initialiseHoldOperations(int noteCount, int count)
    {
        holdOperationSlots = new int[noteCount];
        holdStateSlots = new int[count];
        registeredHoldModes = new BmsLongNoteMode[count];
        holdQueries = new BmsHoldStateQuery[count];
        repressOperations = new BmsRepressOperation[count];
        releaseOperations = new BmsReleaseOperation[count];
        finishOperations = new BmsFinishOperation[count];
        pauseOperations = new BmsPauseOperation[count];
        automaticTailOperations = new BmsAutomaticTailOperation[count];
        rewindOperations = new Action<int, double>[count];
        configurationOperations = new Action<int, bool>[count];
        headDeadlines = new Func<int, double>[count];
    }

    private static BmsLongNoteMode holdMode(BmsLongNote note) => note.Beatmap.LockedLongNoteMode == BmsLongNoteMode.Undefined
        ? BmsLongNoteMode.LongNote : note.Beatmap.LockedLongNoteMode;

    private void refreshHoldConfiguration(int slot, bool preserveHistory)
    {
        if (holdMode(longNotes[slot]) != registeredHoldModes[holdOperationSlots[slot]])
            throw new InvalidOperationException("Changing a registered long-note mode requires a new gameplay engine.");
        configurationOperations[holdOperationSlots[slot]](slot, preserveHistory);
    }

    private sealed class LongNoteOperations
    {
        private readonly BmsColumnJudgementContext column;
        private readonly BmsJudgementExecutionContext execution;
        private readonly BmsLongNoteJudgementRule rule;
        private readonly BmsPressOperation press;
        private readonly BmsPassiveOperation passive;
        private readonly BmsHoldStateQuery query;
        private readonly BmsRepressOperation repress;
        private readonly BmsReleaseOperation release;
        private readonly BmsFinishOperation finish;
        private readonly BmsPauseOperation pause;
        private readonly BmsAutomaticTailOperation automaticTail;
        private readonly Action<int, double> rewind;
        private readonly Action<int, bool> configure;
        private readonly Func<int, double> deadline;

        internal LongNoteOperations(BmsColumnJudgementContext column, IBmsJudgementOutput output, BmsLongNoteJudgementRule rule)
        {
            this.column = column;
            this.rule = rule;
            execution = new BmsJudgementExecutionContext(column, output);
            press = Press;
            passive = Passive;
            query = Query;
            repress = Repress;
            release = Release;
            finish = Finish;
            pause = Pause;
            automaticTail = AutomaticTail;
            rewind = Rewind;
            configure = Configure;
            deadline = Deadline;
        }

        private BmsLongNoteRuntime state(int slot) => column.normalHoldStates[column.holdStateSlots[column.holdOperationSlots[slot]]];

        internal void Register(BmsLongNote note, int slot)
        {
            var operationSlot = column.holdOperationSlots[slot];
            column.holdStateSlots[column.holdOperationSlots[slot]] = column.normalHoldStates.Count;
            column.normalHoldStates.Add(new BmsLongNoteRuntime(note));
            column.NoteRegistrations[slot] = new BmsNoteRegistration(note, slot, press, passive);
            column.holdQueries[operationSlot] = query;
            column.repressOperations[operationSlot] = repress;
            column.releaseOperations[operationSlot] = release;
            column.finishOperations[operationSlot] = finish;
            column.pauseOperations[operationSlot] = pause;
            column.automaticTailOperations[operationSlot] = automaticTail;
            column.rewindOperations[operationSlot] = rewind;
            column.configurationOperations[operationSlot] = configure;
            column.headDeadlines[operationSlot] = deadline;
            column.registeredHoldModes[operationSlot] = BmsLongNoteMode.LongNote;
        }

        private bool Press(int slot, HitResult result, in BmsPressContext input) => rule.TryPress(state(slot), execution, result, input);
        private void Passive(int slot, in BmsFrameContext frame) => rule.CheckPassive(state(slot), execution, frame);
        private BmsLongNoteState Query(int slot) => state(slot).State;
        private bool Repress(int slot, in BmsPressContext input) => rule.TryRepress(state(slot), execution, input);
        private bool Release(int slot, in BmsReleaseContext input) => rule.TryRelease(state(slot), execution, input);
        private void Finish(int slot, in BmsFrameContext frame, double elapsed, bool holding) => rule.Finish(state(slot), execution, frame, elapsed, holding);
        private bool Pause(int slot, in BmsFrameContext frame) => rule.CompleteAtPause(state(slot), execution, frame);
        private bool AutomaticTail(int slot, in BmsFrameContext frame) => rule.TryRelease(state(slot), execution, new BmsReleaseContext(frame.SimulationTime, frame));
        private void Rewind(int slot, double time) => rule.Rewind(state(slot), time);
        private double Deadline(int slot) => state(slot).Hold.NextHeadTime;
        private void Configure(int slot, bool preserveHistory)
        {
            state(slot).Hold.RefreshWindows();
        }
    }

    private sealed class ChargeNoteOperations
    {
        private readonly BmsColumnJudgementContext column;
        private readonly BmsJudgementExecutionContext execution;
        private readonly BmsChargeNoteJudgementRule rule;
        private readonly BmsPressOperation press;
        private readonly BmsPassiveOperation passive;
        private readonly BmsHoldStateQuery query;
        private readonly BmsRepressOperation repress;
        private readonly BmsReleaseOperation release;
        private readonly BmsFinishOperation finish;
        private readonly BmsPauseOperation pause;
        private readonly BmsAutomaticTailOperation automaticTail;
        private readonly Action<int, double> rewind;
        private readonly Action<int, bool> configure;
        private readonly Func<int, double> deadline;

        internal ChargeNoteOperations(BmsColumnJudgementContext column, IBmsJudgementOutput output, BmsChargeNoteJudgementRule rule)
        {
            this.column = column;
            this.rule = rule;
            execution = new BmsJudgementExecutionContext(column, output);
            press = Press;
            passive = Passive;
            query = Query;
            repress = Repress;
            release = Release;
            finish = Finish;
            pause = Pause;
            automaticTail = AutomaticTail;
            rewind = Rewind;
            configure = Configure;
            deadline = Deadline;
        }

        private BmsChargeNoteRuntime state(int slot) => column.chargeHoldStates[column.holdStateSlots[column.holdOperationSlots[slot]]];

        internal void Register(BmsLongNote note, int slot)
        {
            var operationSlot = column.holdOperationSlots[slot];
            column.holdStateSlots[column.holdOperationSlots[slot]] = column.chargeHoldStates.Count;
            column.chargeHoldStates.Add(new BmsChargeNoteRuntime(note));
            column.NoteRegistrations[slot] = new BmsNoteRegistration(note, slot, press, passive);
            column.holdQueries[operationSlot] = query;
            column.repressOperations[operationSlot] = repress;
            column.releaseOperations[operationSlot] = release;
            column.finishOperations[operationSlot] = finish;
            column.pauseOperations[operationSlot] = pause;
            column.automaticTailOperations[operationSlot] = automaticTail;
            column.rewindOperations[operationSlot] = rewind;
            column.configurationOperations[operationSlot] = configure;
            column.headDeadlines[operationSlot] = deadline;
            column.registeredHoldModes[operationSlot] = BmsLongNoteMode.ChargeNote;
        }

        private bool Press(int slot, HitResult result, in BmsPressContext input) => rule.TryPress(state(slot), execution, result, input);
        private void Passive(int slot, in BmsFrameContext frame) => rule.CheckPassive(state(slot), execution, frame);
        private BmsLongNoteState Query(int slot) => state(slot).State;
        private bool Repress(int slot, in BmsPressContext input) => rule.TryRepress(state(slot), execution, input);
        private bool Release(int slot, in BmsReleaseContext input) => rule.TryRelease(state(slot), execution, input);
        private void Finish(int slot, in BmsFrameContext frame, double elapsed, bool holding) => rule.Finish(state(slot), execution, frame, elapsed, holding);
        private bool Pause(int slot, in BmsFrameContext frame) => rule.CompleteAtPause(state(slot), execution, frame);
        private bool AutomaticTail(int slot, in BmsFrameContext frame) => rule.TryRepress(state(slot), execution, new BmsPressContext(frame.SimulationTime, frame, ReverseScratch: true));
        private void Rewind(int slot, double time) => rule.Rewind(state(slot), time);
        private double Deadline(int slot) => state(slot).Hold.NextHeadTime;
        private void Configure(int slot, bool preserveHistory)
        {
            state(slot).Hold.RefreshWindows();
        }
    }

    private sealed class HellChargeNoteOperations
    {
        private readonly BmsColumnJudgementContext column;
        private readonly BmsJudgementExecutionContext execution;
        private readonly BmsHellChargeNoteJudgementRule rule;
        private readonly BmsPressOperation press;
        private readonly BmsPassiveOperation passive;
        private readonly BmsHoldStateQuery query;
        private readonly BmsRepressOperation repress;
        private readonly BmsReleaseOperation release;
        private readonly BmsFinishOperation finish;
        private readonly BmsPauseOperation pause;
        private readonly BmsAutomaticTailOperation automaticTail;
        private readonly Action<int, double> rewind;
        private readonly Action<int, bool> configure;
        private readonly Func<int, double> deadline;

        internal HellChargeNoteOperations(BmsColumnJudgementContext column, IBmsJudgementOutput output, BmsHellChargeNoteJudgementRule rule)
        {
            this.column = column;
            this.rule = rule;
            execution = new BmsJudgementExecutionContext(column, output);
            press = Press;
            passive = Passive;
            query = Query;
            repress = Repress;
            release = Release;
            finish = Finish;
            pause = Pause;
            automaticTail = AutomaticTail;
            rewind = Rewind;
            configure = Configure;
            deadline = Deadline;
        }

        private BmsHellChargeNoteRuntime state(int slot) => column.hellChargeHoldStates[column.holdStateSlots[column.holdOperationSlots[slot]]];

        internal void Register(BmsLongNote note, int slot)
        {
            var operationSlot = column.holdOperationSlots[slot];
            column.holdStateSlots[column.holdOperationSlots[slot]] = column.hellChargeHoldStates.Count;
            column.hellChargeHoldStates.Add(new BmsHellChargeNoteRuntime(note));
            column.NoteRegistrations[slot] = new BmsNoteRegistration(note, slot, press, passive);
            column.holdQueries[operationSlot] = query;
            column.repressOperations[operationSlot] = repress;
            column.releaseOperations[operationSlot] = release;
            column.finishOperations[operationSlot] = finish;
            column.pauseOperations[operationSlot] = pause;
            column.automaticTailOperations[operationSlot] = automaticTail;
            column.rewindOperations[operationSlot] = rewind;
            column.configurationOperations[operationSlot] = configure;
            column.headDeadlines[operationSlot] = deadline;
            column.registeredHoldModes[operationSlot] = BmsLongNoteMode.HellChargeNote;
        }

        private bool Press(int slot, HitResult result, in BmsPressContext input) => rule.TryPress(state(slot), execution, result, input);
        private void Passive(int slot, in BmsFrameContext frame) => rule.CheckPassive(state(slot), execution, frame);
        private BmsLongNoteState Query(int slot) => state(slot).State;
        private bool Repress(int slot, in BmsPressContext input) => rule.TryRepress(state(slot), execution, input);
        private bool Release(int slot, in BmsReleaseContext input) => rule.TryRelease(state(slot), execution, input);
        private void Finish(int slot, in BmsFrameContext frame, double elapsed, bool holding) => rule.Finish(state(slot), execution, frame, elapsed, holding);
        private bool Pause(int slot, in BmsFrameContext frame) => rule.CompleteAtPause(state(slot), execution, frame);
        private bool AutomaticTail(int slot, in BmsFrameContext frame) => rule.TryRepress(state(slot), execution, new BmsPressContext(frame.SimulationTime, frame, ReverseScratch: true));
        private void Rewind(int slot, double time) => rule.Rewind(state(slot), time);
        private double Deadline(int slot) => state(slot).Hold.NextHeadTime;
        private void Configure(int slot, bool preserveHistory)
        {
            state(slot).Hold.RefreshWindows();
            state(slot).Body.HistoryEnabled = preserveHistory;
        }
    }
}
