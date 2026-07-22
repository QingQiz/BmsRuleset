# Gameplay and Settings

[Back to README](../README.md) | [中文](./gameplay.zh-CN.md)

## Input and Key Bindings

Default bindings (all rebindable in **Settings → Key Bindings → osu!BMS**):

**ingame controls:** increase/decrease scroll speed temporarily

Key sound of the next upcoming note in a column plays on every key press regardless of judgement result.

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
`#DEFEXRANK` and bare `#EXRANK` set the initial judge-window width as a percentage where `100` matches Normal
(`#RANK 2`). Indexed `#EXRANKxx` values can be applied mid-chart through channel `A0`; undefined `A0` references leave
the current window unchanged.

**Score:** `total EX score / max EX score × 1,000,000`

**DJ LEVEL rank:** X (all PGREAT) · S ≥ 8/9 · A ≥ 7/9 · B ≥ 6/9 · C ≥ 5/9 · D otherwise

## Gauge

The ruleset implements 6 selectable BMS gauge types covering both Groove and Survival modes.
Default play uses the Normal gauge. Gauge types are selected via mods:

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

- **Groove gauges** (E2/E1/Normal): recoverable, start at 20%, must reach clear threshold by song end. Bar colour
  transitions from red (< 20%) → amber (< clear) → green (≥ clear) based on the active gauge's threshold.
- **Survival gauges** (H1/H2/H3): start at 100%, damage-only (no recovery for H3). Gauge uses a fixed colour with no
  clear line. Pass condition is purely survival (HP never hit 0).
- Hard (H1) has **guts protection**: damage is reduced at low HP (50% → ×0.8, 40% → ×0.7, …, 10% → ×0.4).
- `#TOTAL` controls the maximum gain rate for TOTAL-algorithm gauges. Default formula:
  `max(7.605 × N / (0.01 × N + 6.5), 160)` (LR2 formula, where N = total playable notes).
- Landmine damage: base-36 value ÷ 2 percent (e.g., `ZZ` = 647.5% → instant wipe).
- Gauge mods are mutually exclusive.
- **Auto Gauge (AG)** is an Automation mod that chains all six gauges hardest-first
  (Hazard → EX Hard → Hard → Normal → Easy → Assist Easy). You start on the hardest tier; when HP hits 0 the
  active gauge drops to the next tier and play continues — the run only fails once every tier is exhausted. The
  resulting score is attributed to the hardest tier you reached.

## Mods

| Mod                      | Description                                                     |         |
|--------------------------|-----------------------------------------------------------------|---------|
| Autoplay                 | auto play                                                       |         |
| Double Time / Half Time  |                                                                 |         |
| No Fail                  |                                                                 |         |
| Mirror                   | Mirrors the key layout                                          |         |
| 2P                       | change the player layout from 1P to 2P                          |         |
| Auto Scratch (AS)        | auto play scratch lane                                          |         |
| Hide Scratch (HS)        | Remove the scratch notes and hide scratch lane                  |         |
| Background Keysound (BK) | Play all keysounds as background audio instead of on key press. |         |
| Lane Random (LR)         | RANDOM: permutes lane columns                                   |         |
| Note Random (NR)         | S-RANDOM / H-RANDOM: per-note random                            |         |
| Rotation Random (RR)     | R-RANDOM: rotate + optional mirror                              |         |
| Assist Easy Gauge (E2)   | Use Assist Easy BMS gauge                                       |         |
| Easy Gauge (E1)          | Use Easy BMS gauge                                              |         |
| Hard Gauge (H1)          | Use Hard BMS gauge                                              |         |
| EX Hard Gauge (H2)       | Use EX Hard BMS gauge                                           |         |
| Hazard Gauge (H3)        | Use Hazard BMS gauge                                            |         |
| Auto Gauge (AG)          | Start with the hardest gauge; drop a tier on failure            |         |
| Long Note (L1)           | LN judgement: LN mode: single endpoint judged at tail           |         |
| Charge Note (L2)         | LN judgement: CN mode: head & tail judged separately            |         |
| Hell Charge Note (L3)    | LN judgement: HCN mode: CN + body gauge drain/recover           |         |

## Settings

| Setting                       | Default  | Range / options                            | Description                                                                                                                                                                                     |
|-------------------------------|----------|--------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Scroll speed                  | 8.0      | 1.0–50.0, step 0.1                        | Note fall speed. In-game scroll-speed actions adjust it temporarily; keys are rebindable under **Settings → Key Bindings → osu!BMS**.                                                         |
| Reference BPM                 | Main BPM | Start BPM, Max BPM, Main BPM, or Min BPM   | Scroll-speed reference used when a chart has no `#BASEBPM`. Main BPM uses the BPM containing the most playable notes; ties use the earliest occurrence.                                       |
| BGA dim                       | 70%      | 0%–100%                                   | Dims background animation without removing it. 0% keeps full brightness; 100% makes the BGA invisible while it continues to run.                                                             |
| Use dedicated preview audio   | On       | On / off                                   | Uses `#PREVIEW` or `preview.*` when available. When disabled, song-select previews are synthesized only from BGM and keysound samples.                                                        |
| Show BMS 5K                   | On       | On / off                                   | Shows or hides single-play BMS 5K charts in song select.                                                                                                                                       |
| Show BME 7K                   | On       | On / off                                   | Shows or hides single-play BME 7K charts in song select.                                                                                                                                       |
| Show PMS 9K                   | On       | On / off                                   | Shows or hides single-play PMS 9K charts in song select.                                                                                                                                       |
| Show BMS 5K DP                | On       | On / off                                   | Shows or hides double-play BMS 5K charts in song select.                                                                                                                                       |
| Show BME 7K DP                | On       | On / off                                   | Shows or hides double-play BME 7K charts in song select.                                                                                                                                       |
| Show PMS 9K DP                | On       | On / off                                   | Shows or hides double-play PMS 9K charts in song select.                                                                                                                                       |

Layout visibility filters are applied live in song select — uncheck a layout to hide all beatmaps of that type.

The same BMS settings section also contains chart import and cleanup actions. Difficulty-table management is described
in the [difficulty table guide](./difficulty-tables.md).

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
