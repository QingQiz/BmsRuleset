# HCN and landmine frame-order reference cases

Source: beatoraja commit `9cddf911113e74da87458344bb85a4ea45431d0b`.
The expectations below are literal values derived from the fixed Java source
branches, not from the ruleset's production gauge or judgement formulas. Tests
do not need the sibling checkout or artifacts at runtime. This reference does
not claim an executed full Java-client replay.

- `src/bms/player/beatoraja/play/JudgeManager.java:219-304` scans all lanes.
  Lines 223-228 read the current physical key states. Lines 244-246 immediately
  apply `addValue(-damage)` to a mine that is passing while pressed. A release
  in the current frame avoids it; a press in the current frame detonates it.
  Lines 230-231 skip notes at or before the previous frame, so a later press
  cannot detonate an already passed mine.
- Lines 306-338 then process all HCN bodies. Holding accumulates elapsed time;
  strictly more than 200000 microseconds applies half a GREAT, at most once
  per frame. Key-change judgements are processed only after this body phase.
- `GaugeProperty.java:89` defines NORMAL with initial 20%, bounds 2%-100%,
  PGREAT/GREAT multiplier 1. `GrooveGauge.java:260` scales positive recovery
  by TOTAL / scoring-note count. Each update is clamped separately by
  `GrooveGauge.java:218-220`. Mines are direct damage, not scoring notes.

The formal fixture is `TestSceneBmsBeatorajaHcnLandmineFrameOrder`. Every chart
uses 7K, RANK2, NORMAL, locked HCN, a column-1 HCN [3000,5000], a 10% mine at
3201, and a column-3 ordinary note at 10000 to keep the player alive. HCN head
and tail plus the ordinary note give three scoring endpoints. The tests use
the real `BmsReplayFrame -> replay handler -> playfield -> health/score` path;
they never assign health or call judgement/body update methods directly.

## Ceiling, same column and other column

TOTAL240 gives head +80% and body +40%. Mine column is parameterized as 1/2.
Input: empty at0, Key1 down at3000, Key1+Key2 at3201, Key1 at3210, empty at5000
and11000. The column-2 mine therefore specifically sees a new press at3201.

| Checkpoint | Expected final health | Ordered gauge history at this time | Judgement events |
| --- | --- | --- | --- |
| First3200 | 100% | none (exactly200ms is not enough) | PG HCN head at3000, column1 |
| First3201 | 100% | 90%,100% (mine, body) | previous head, then mine at3201 in column1/2 |
| Rewind3200 | 100% | none; no future history remains | head only; mine event removed |
| Replay3201 | 100% | 90%,100% | head and one mine; no extra endpoints |

The reversed order gives 100%,90%, with final90%. The mine event uses the
ruleset's `Meh/Landmine` representation; it must not be mistaken for a POOR
long-note endpoint. Source column/time/damage and actual/expected timing are
asserted separately from gauge values.

## Unsaturated control

The same cross-column input with TOTAL12 gives head +4% and body +2%.
First3200 and rewind3200 must be24%. First/replayed3201 must apply exactly
one mine (-10%) and one body (+2%): history [14%,16%], final16%.
The reverse order has history [26%,16%]; final16% alone cannot prove order.

## Same-frame release

The mine is column2, TOTAL240. Input: empty at0; Key1+Key2 at3000; Key1 only
at3201; Key1+Key2 at3202; Key1 at3210; empty at5000 and11000. Key2 is therefore
held immediately before the mine and released exactly when it passes.

First3200 is100%, with no gauge operation at3200. At3201 only the HCN body
applies, yielding history [100%] and final100%. At3202 re-pressing Key2 yields
no gauge operation and no mine event. Rewind3200 and replay3201/3202 must give
the same values; every snapshot contains exactly the HCN head event.

All immutable snapshots are collected before `Assert.Multiple` evaluates any
business assertion, so known red first-pass values cannot suppress rewind
execution. Tests preserve the body-before-input and release-expiry contracts
covered by the separate existing fixtures; this fixture does not extend to
multi-HCN enumeration, OS hardware, or old replay-rule compatibility.
