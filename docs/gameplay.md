# Gameplay and Settings

[Back to README](../README.md) | [中文](./gameplay.zh-CN.md)

## Input and Key Bindings

Use **Up/Down** to temporarily increase/decrease scroll speed during play. Rebind controls in
**Settings → Key Bindings → osu!BMS**.

Each key press plays the next note's keysound in that column, regardless of the judgement.

## Judgements and Scoring

**Judgement tiers (beatoraja timing windows, from `#RANK` / EXRANK):**

| Name       | EX pts | Combo        | RANK 2 (Normal) window           |
|------------|--------|--------------|----------------------------------|
| **PGREAT** | 2      | kept         | ±15 ms                           |
| **GREAT**  | 1      | kept         | ±45 ms                           |
| **GOOD**   | 0      | kept         | ±112.5 ms                        |
| **BAD**    | 0      | reset        | -220ms / +280ms                  |
| **POOR**   | 0      | reset        | > +280ms                         |
| **E-POOR** | 0      | **no break** | [-500ms,-220ms], no note consume |

`#RANK` 0 = Very Hard (±5/15/37.5 ms, BAD -220/+280 ms) → 4 = Very Easy (±25/75/187.5 ms, BAD -220/+280 ms).
`#DEFEXRANK` and unnumbered `#EXRANK` set the initial window width as a percentage; `100` equals Normal (`#RANK 2`).
Channel `A0` applies indexed `#EXRANKxx` values mid-chart. Undefined references leave the window unchanged.

**Score:** `total EX score / max EX score × 1,000,000`

**DJ LEVEL rank:** S (all PGREAT) · AAA ≥ 8/9 · AA ≥ 7/9 · A ≥ 6/9 · B ≥ 5/9 · C otherwise

### Judgement Selection Algorithms

Open **Settings → BMS → Judgement selection algorithm** to choose which note receives a press when judgement windows
overlap in the same column. The four strategies follow beatoraja's `Combo`, `Duration`, `Lowest`, and `Score` algorithms:

| Option | Selection rule |
|--------|----------------|
| **Combo priority (LR2)** — default | Switches to a later note when the current candidate is strictly past its late GOOD boundary and the press is at or after the later note's early GOOD boundary. |
| **Time difference priority (AC)** | Chooses the eligible note closest to the press time. Equal time differences favour the earlier note. |
| **Earliest note priority** | Chooses the earliest eligible note, even if a later note would receive a better judgement. |
| **Score priority** | Uses the same switching rule as Combo priority, with GREAT boundaries instead of GOOD. |

For example, on a 7K `#RANK 2` chart, two notes in the same key column occur at 1000 ms and 1100 ms. A press at 1100 ms
gives the first note GOOD under Combo or Earliest note priority; Time difference or Score priority selects the second
note for PGREAT.

Selection applies to normal notes and long-note heads, including scratch. Each note uses the windows determined by
its layout, RANK/EXRANK, and active judgement-window mods. Long-note release rules are unchanged; E-POOR consumes no note.

The algorithm is fixed at play start; setting changes apply to the next play. Replays use the recorded algorithm
regardless of the viewer's settings. Replays recorded before this setting existed use the original selection behaviour.

## Gauge

Choose from 6 regular gauges (Groove or Survival) and 3 course gauges through mods. Normal is the default:

> [!NOTE]
> The gauge values and algorithms are adapted from **beatoraja** (SEVENKEYS mode), itself a reimplementation of the
> LR2 groove gauge. The `(2 × #TOTAL − 320) / notes` recovery scaling on survival gauges (H1/H2) follows beatoraja's
> `LIMIT_INCREMENT` modifier. Landmine damage uses the BMS spec formula.

| Mod         | Acronym     | Type                 | Algorithm       | Initial HP | Clear   | Bar              |
|-------------|-------------|----------------------|-----------------|------------|---------|------------------|
| Assist Easy | **E2**      | Difficulty Reduction | TOTAL (#TOTAL)  | 20%        | ≥ 60%   | Groove (dynamic) |
| Easy        | **E1**      | Difficulty Reduction | TOTAL (#TOTAL)  | 20%        | ≥ 80%   | Groove (dynamic) |
| Normal      | *(default)* | —                    | TOTAL (#TOTAL)  | 20%        | ≥ 80%   | Groove (dynamic) |
| Hard        | **H1**      | Difficulty Increase  | Limit Increment | 100%       | Survive | Fixed red        |
| EX Hard     | **H2**      | Difficulty Increase  | Limit Increment | 100%       | Survive | Fixed purple     |
| Hazard      | **H3**      | Difficulty Increase  | Fixed           | 100%       | Survive | Fixed gold       |
| Class       | **C1**      | Course                | Fixed           | 100%       | Survive | Fixed red        |
| EX Class    | **C2**      | Course                | Fixed           | 100%       | Survive | Fixed purple     |
| EX Hard Class | **C3**      | Course                | Fixed           | 100%       | Survive | Fixed gold       |

- **Groove gauges** (E2/E1/Normal): recoverable, start at 20%, must reach clear threshold by song end. Bar colour
  transitions from red (< 20%) → amber (< clear) → green (≥ clear) based on the active gauge's threshold.
- **Survival gauges** (H1/H2/H3): start at 100% and fail at 0%. H3 has no recovery. The bar uses a fixed colour with
  no clear line.
- **Course gauges** (C1/C2/C3): Survival gauges that carry HP between stages; reaching 0% fails the course. Bars are
  red, purple, or gold. The selected regular gauge maps to the corresponding Class tier.
- Hard (H1) has **guts protection**: below each HP threshold, damage is reduced (below 50% → ×0.8, below 40% → ×0.7,
  …, below 10% → ×0.4).
- `#TOTAL` controls the maximum gain rate for TOTAL-algorithm gauges. When it is omitted, 5-key, 7-key, PMS, and LR2
  layouts use `max(7.605 × N / (0.01 × N + 6.5), 260)`. 24-key layouts use
  `max(7.605 × (N + 100) / (0.01 × N + 6.5), 300)` (where N = total playable notes).
- Landmine damage: base-36 value ÷ 2 percent (e.g., `ZZ` = 647.5% → instant wipe).

## Lamps

Song panels, stage panels, and course cards show **clear lamps** for the best result. Colours, flashing, and lamp
order follow **beatoraja**'s default `lamp.png`, which retains LR2 conventions. From lowest to highest:

| Lamp     | Fill colour | Flash colour | Meaning                                        |
|----------|-------------|--------------|------------------------------------------------|
| NO PLAY  | `#282C30`   | —            | no score recorded                              |
| FAILED   | `#E92F0A`   | `#0F0300`    | failed; flashes at a 60 ms cycle               |
| L-ASSIST | `#DDA2DF`   | —            | passed with the **Assist Easy** gauge          |
| EASY     | `#56CA43`   | —            | passed with the Easy gauge                     |
| NORMAL   | `#F5C758`   | —            | passed with the Normal gauge                   |
| HARD     | `#F8F7F5`   | —            | passed with the Hard gauge                     |
| EX-HARD  | `#EFFD09`   | `#FD0909`    | passed with the EX-Hard or Hazard gauge; flashes at a 60 ms cycle |
| FULL COMBO | `#FFFFFF` | `#09FAFD`    | full combo; flashes at a 60 ms cycle           |
| PERFECT  | `#FFFFFF`   | `#3FFF4D`    | PGREAT + GREAT only; flashes at a 60 ms cycle  |
| MAX      | `#FFFFFF`   | `#FFEB42`    | all PGREAT; flashes at a 60 ms cycle           |

The panel shows the highest lamp and rank among scores matching the selected mods. A clear on an easier gauge
does not downgrade an existing lamp.

Lowering the difficulty keeps lamps earned under harder conditions visible, while raising it hides lamps earned
under easier conditions. Only Double Time is treated as a difficulty increase for lamp filtering.

Beatoraja distinguishes pattern assists (**ASSIST**, dark purple) from the Assist Easy gauge (**L-ASSIST**).
This ruleset has no pattern-level assists, so it awards L-ASSIST for Assist Easy clears and never awards ASSIST.

### Course Lamps

| Course tier (gauge)  | Lamp    |
|----------------------|---------|
| Class (C1)           | NORMAL  |
| Ex Class (C2)        | HARD    |
| Ex Hard Class (C3)   | EX-HARD |

Courses do not award FULL COMBO, PERFECT, or MAX lamps. Course cards show the best lamp and rank among results
matching the selected mods.

## Mods

| Mod                      | Description                                                     | Options                                            |
|--------------------------|-----------------------------------------------------------------|----------------------------------------------------|
| Autoplay                 | Plays automatically                                             |         |
| Double Time / Half Time  |                                                                 | Speed; adjust pitch                                |
| No Fail                  |                                                                 |         |
| Mirror                   | Mirrors the key layout                                          |         |
| Invert (IN)              | Converts each note except the lane's last into a hold note      | Randomise LN length; seed                          |
| 2P                       | Switches the layout from 1P to 2P                               |         |
| Constant (CN)            | Disables scroll-speed changes, including #SPEED/#SCROLL/BPM     |         |
| Auto Scratch (AS)        | Plays the scratch lane automatically                            |         |
| Hide Scratch (HS)        | Removes scratch notes and hides the lane                        |         |
| No Mine (NM)             | Removes all landmines, including those in scratch lanes         |         |
| Background Keysound (BK) | Plays keysounds as background audio instead of on key press     |         |
| Lane Random (LR)         | RANDOM: permutes lane columns                                   | Include scratch; seed; lane order                  |
| Note Random (NR)         | S-RANDOM / H-RANDOM: per-note random                            | Include scratch; mode; seed                        |
| Rotation Random (RR)     | R-RANDOM: rotate + optional mirror                              | Include scratch; seed                              |
| Assist Easy Gauge (E2)   | Use Assist Easy BMS gauge                                       |         |
| Easy Gauge (E1)          | Use Easy BMS gauge                                              |         |
| Hard Gauge (H1)          | Use Hard BMS gauge                                              |         |
| EX Hard Gauge (H2)       | Use EX Hard BMS gauge                                           |         |
| Hazard Gauge (H3)        | Use Hazard BMS gauge                                            |         |
| Auto Gauge (AG)          | Start with the hardest gauge; drop a tier on failure            |         |
| No Good (NG)             | Removes the GOOD judgement window                               |         |
| No Great (NE)            | Removes the GREAT and GOOD judgement windows                    |         |
| Long Note (L1)           | LN: judges only the tail                                       |         |
| Charge Note (L2)         | CN: judges head and tail separately                            |         |
| Hell Charge Note (L3)    | HCN: CN with gauge drain/recovery during the body              |         |

## Settings

| Setting                       | Default  | Range / options                            | Description                                                                                                                                                                                     |
|-------------------------------|----------|--------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Scroll speed | 8.0 | 1.0–50.0, step 0.1 | Note fall speed; in-game controls adjust it temporarily. |
| Reference BPM                 | Main BPM | Start BPM, Max BPM, Main BPM, or Min BPM   | Scroll-speed reference used when a chart has no `#BASEBPM`. Main BPM uses the BPM containing the most playable notes; ties use the earliest occurrence.                                       |
| Judgement selection algorithm | Combo priority (LR2) | Combo priority (LR2), Time difference priority (AC), Earliest note priority, Score priority | Selects which note receives a press when windows overlap. See [selection rules](#judgement-selection-algorithms). |
| BGA dim | 70% | 0%–100% | 0% keeps full brightness; 100% hides the BGA without stopping playback. |
| Unlock frame rate limit       | Off      | On / off                                  | Removes osu!'s 1000 Hz frame and input polling cap during BMS gameplay. Higher GC and GPU pressure may cause extra stutters; disable this option if that happens. |
| Visual offset | 0 ms | -500–500 ms, step 1 ms | Positive values display notes earlier (for more Slow judgements); negative values display them later (for more Fast judgements). |
| LN tail visual offset | 0 ms | 0–1000 ms, step 1 ms | Advances long-note tails visually, shortening notes without moving tails before their heads. |
| Adjust visual offset automatically | Off | On / off | Applies each valid local play's suggested offset. See [calibration](#visual-offset-calibration). |
| Use dedicated preview audio   | On       | On / off                                   | Uses `#PREVIEW` or `preview.*` when available. When disabled, song-select previews are synthesized only from BGM and keysound samples.                                                        |
| Show BMS 5K                   | On       | On / off                                   | Shows or hides single-play BMS 5K charts in song select.                                                                                                                                       |
| Show BME 7K                   | On       | On / off                                   | Shows or hides single-play BME 7K charts in song select.                                                                                                                                       |
| Show PMS 9K                   | On       | On / off                                   | Shows or hides single-play PMS 9K charts in song select.                                                                                                                                       |
| Show BMS 5K DP                | On       | On / off                                   | Shows or hides double-play BMS 5K charts in song select.                                                                                                                                       |
| Show BME 7K DP                | On       | On / off                                   | Shows or hides double-play BME 7K charts in song select.                                                                                                                                       |
| Show PMS 9K DP                | On       | On / off                                   | Shows or hides double-play PMS 9K charts in song select.                                                                                                                                       |

Layout filters take effect immediately in song select.

The BMS settings also provide chart import, cleanup, and [difficulty-table management](./difficulty-tables.md).

### Visual Offset Calibration

Visual offset and LN tail visual offset affect only the display; audio, judgements, scoring, and keysounds stay unchanged.

Local plays generate suggestions from their median hit error. Apply the average of recent suggestions in the BMS
settings, or enable **Adjust visual offset automatically** to apply each new suggestion after a play.
Replays, automatic play, and plays with fewer than 50 timed hits are excluded.

## Song Select Search

The song-select search box supports these BMS filters:

| Filter | Short form | Value |
|--------|------------|-------|
| `stars` | `star`, `sr` | osu! star difficulty |
| `keys` | `k`, `key` | Playable key count, excluding scratch; for example, `k=7` finds 7K + scratch charts |
| `source` | `src` | Text contained in the chart's source path |
| `ln` | `lns` | Percentage of objects that are long notes, from 0 to 100 |
| `scratch` | `sc` | Percentage of objects in scratch lanes, from 0 to 100 |
| `table` | `tb` | Difficulty table name or symbol |
| `level` | `lv` | Difficulty table level |

Numeric filters support `=`, `!=`, `<`, `<=`, `>` and `>=`, such as `ln>=25` or `sc<10`. Key equality filters also
accept comma-separated values, such as `keys=5,7`.
