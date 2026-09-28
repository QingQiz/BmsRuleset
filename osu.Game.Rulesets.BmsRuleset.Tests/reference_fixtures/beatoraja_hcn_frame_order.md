# HCN input-frame reference cases

Source: beatoraja commit `9cddf911113e74da87458344bb85a4ea45431d0b`.
These expectations come from the following Java source branches, not from the
ruleset's gauge calculator or judgement tables. Tests do not read the sibling
checkout or artifacts at runtime.

- `src/bms/player/beatoraja/play/JudgeManager.java:223–307` scans lanes and
  determines `inclease` from the current physical keys or a previously successful
  tail. The HCN end clears `passing` and accumulated body time.
- Lines 310–338 then process **all** HCN bodies. An unjudged head is skipped.
  Holding adds elapsed microseconds; releasing subtracts them. The strict
  thresholds are `>200000` and `<-200000`; each update applies at most one
  `gauge.update(1, 0.5f)` (half GREAT) or `gauge.update(3, 0.5f)` (half BAD).
- Only afterwards, line 347 onwards processes key changes, including ordinary
  notes and scratch direction tails (359–376); key-up is processed at 502–547.
- `GaugeProperty.java:89` defines NORMAL with start 20%, floor 2%, ceiling 100%,
  PGREAT/GREAT recovery multiplier 1, BAD -3%, and POOR -6%.
- `GrooveGauge.java:260` uses TOTAL / total scoring notes for positive recovery.
  Charge head and tail each contribute one scoring note. `setValue` clamps each
  operation separately, so opposite operation orders differ near the ceiling.
- `JudgeProperty.java` SEVENKEYS RANK2 gives normal-note BAD at -149ms and
  LONGNOTE_END/LONGSCRATCH_END GREAT at -100ms. The independently executed tables
  are also retained in `beatoraja_windows.csv` (microseconds).

All charts below use 7K, RANK2, NORMAL, locked HCN, and a final ordinary note at
10000ms to keep the player alive. No test assigns health directly.

## Cross-column recovery then BAD

HCN [3000,5000] and ordinary note 3350 are in different columns (1/2, then 2/1).
TOTAL320 / 4 gives PG +80% and body +40%. Input: 3000 hold HCN; 3201 additionally
press the other column; 3210 release other; 5000 release HCN.

| Exact time | Reference NORMAL health | Reason |
| --- | --- | --- |
| 3000 | 100% | PG head: 20+80 |
| 3200 | 100% | Exactly 200ms held, no tick |
| 3201 | 100%, then 97% | Body +40 clamps to100, then -149ms BAD -3 |

Repeat 3200 → 3201 after rewind. Score events remain PG@3000 and BAD@3201 with
columns and offsets 0/-149. This must not accumulate duplicate gauge operations.

Unsaturated control uses identical input and TOTAL12: PG +3%, body +1.5%.
3200 is 23%, 3201 is 21.5% with exactly two gauge operations. Both operation
orders give that final value, so this control detects an extra or missing tick
without treating an unsaturated result as proof of correct ordering.

## Cross-column damage then PGREAT

HCN [3000,5000] column1, ordinary note 3201 column2, TOTAL320 / 4. Input: 3000
Key1 down; 3001 Key1 up; 3201 Key2 down; 3210 Key2 up. The early HCN tail is
POOR at offset -1999ms. 3200 has -200ms accumulated, without a body tick.

| Exact time | Reference NORMAL health |
| --- | --- |
| 3001 and 3200 | 94% (100 - 6) |
| 3201 | 92.5%, then 100% (half BAD -1.5, then PG +80) |

The reverse order gives 100%, then 98.5%, an observable opposite-sign mismatch.
The test repeats the 3200 → 3201 rewind with the same fixed expectations.

## Ordinary keyboard input without replay

HCN [3000,4101], TOTAL12 / 3: head/tail PG or GREAT +4%, body +2%. The test
creates the normal Player with `CreateBmsPlayer(null)`. A test keyboard device
provides ordinary `KeyboardKeyInput` to the existing `BmsInputManager` through
its normal `InputHandler.CollectPendingInputs` path. There is no replay handler,
replay score, replay frame, direct action injection, or controller call.

For precise time control, the test freezes only the underlying gameplay clock
using the host's protected `StopGameplayClock`; calling public `Stop()` would
pause the entire no-replay simulation. A protected `AddHandler` reflection
adapter attaches the keyboard; neither adapter modifies production behavior.

Input is a head down at3000 and, at4001, either Z up (column1) or one keyboard
batch of LShift up then LControl down (scratch column0). The scratch batch is
one input-manager poll/update, so Java's lane scan sees the final held direction
before body and before tail key-change processing.

| Time | Ordinary key-up reference | Scratch reversal reference |
| --- | --- | --- |
| 3000 | 24% | 24% |
| 3200 | 24% | 24% |
| 3201 | 26% | 26% |
| 4000 | 32% | 32% |
| 4001 | 36% (accumulator 200→199; GREAT +4) | 34%, then38% (200→201; body +2 then GREAT +4) |
| 4002 | 36% (199→200) | 38% |
| 4003 | 38% (200→201; body +2) | 38%, no new gauge operation |

Both scenarios require exactly PG@3000 and GREAT@4001, offsets 0/-100, and the
appropriate source column. Every checkpoint and keyboard event asserts that
`LastReplayState` is null; replay handler and `HasReplayLoaded` are also checked.
The keyboard observer records the updated physical key state and pre-dispatch
column state, but these column implementation details are diagnostic evidence,
not acceptance constraints on a future correct scheduling architecture.
