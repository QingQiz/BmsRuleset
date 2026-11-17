# Gameplay and Settings

[Back to README](../README.md) | [中文](./gameplay.zh-CN.md)

## Practice

Select a chart and choose **Practice** from the footer or the chart's context menu. Choose **Set start** or **Set end**
and click the static chart to place that boundary, or drag its line. Browse at a close zoom to set distant endpoints
separately; the locate buttons return to either endpoint. Scroll to browse, use **Ctrl + scroll** or the zoom controls for detail,
and choose **Fit range** to bring the selection into view. Choose a bar or note division for snapping; hold **Alt**
to select freely. The time fields accept seconds and align to the selected grid when committed.
Use the chart thumbnail and density overview to locate patterns; hover over density for notes per second, or click and drag either overview to browse at the current zoom. Long charts split into thumbnail columns, ordered from left to right with time increasing upward in each column.

Choose **Mods** in the footer for this practice session, including the playback rate and gauge, then choose **Start practice**.
Choose **Practice** again or press **Escape** to close the settings.
Selected holds continue to their tails, and notes at the start scroll into view before reaching the judgement line.

Each attempt starts with fresh combo and gauge state and has its own temporary statistics. An empty gauge lets
you finish the range. The result screen shows gauge history and judgement statistics. Choose **Practice settings** to edit
the range or Mods, or **Start next attempt** (Enter) to restart the range when ready.
Enable **Skip results and repeat automatically** in practice settings to reveal **Count In** (0–5 seconds). Each completed attempt immediately reloads the range, then counts down at the beginning before play starts.
Press **Escape** to pause, using the same hold preference as normal gameplay. When automatic repeats are enabled, quit from the pause menu to open the aggregate result screen. Click an attempt to view its full statistics; click it again, click the summary heading, or press Escape to return to the aggregate. Press Enter to start another attempt, or choose **Practice settings** to return to the range editor.
After focus loss, press Enter to restart a paused countdown. The last started range, result preference and Count In are remembered for each chart.

## Input and Key Bindings

Use **Up/Down** to temporarily increase/decrease scroll speed during play. Rebind controls in
**Settings → Key Bindings → osu!BMS**.

Key presses play the keysounds assigned to their column.

Scratch has two directions, bound to **Scratch** and **Reverse Scratch**. Either direction can hit a scratch note.
For long scratches, press one direction at the head and follow the [long-note rules](./judgements.md#beatoraja-long-notes) at the tail.

| Layout / side | Scratch | Reverse Scratch |
|---------------|---------|-----------------|
| 5K / 7K, 1P | Left Shift | Left Ctrl |
| 5K / 7K, 2P layout | Right Shift | Right Ctrl |
| Double play, P1 | Left Shift | Left Ctrl |
| Double play, P2 | Right Shift | Right Ctrl |

For keyboard play, bind both directions to comfortable keys. In CN and HCN, press the opposite direction near the
tail to complete a long scratch.

## Judgements, Gauges, and Scoring

- [beatoraja (default rules)](./judgements.md#beatoraja) · [Gauges](./judgements.md#beatoraja-gauges) · [Courses](./judgements.md#beatoraja-course-rules)
- [LR2 (R2)](./judgements.md#lr2) · [Gauges](./judgements.md#lr2-gauges)
- [osu!mania (OM)](./judgements.md#osu-mania) · [Gauges](./judgements.md#osu-mania-gauges)

## Pausing and Resuming

Resuming shows the same countdown as osu!mania, then continues from the pause point. Long notes retain their
state while paused; hold the appropriate keys when play resumes. The saved score receives the **Paused (PA)** marker.

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
| Auto Scratch (AS)        | Automatically presses, holds and releases scratch inputs, including CN/HCN tail reversals |         |
| Double Time / Half Time  |                                                                 | Speed; adjust pitch                                |
| Mirror                   | Mirrors the key layout                                          |         |
| Invert (IN)              | Converts each note except the lane's last into a hold note      | Randomise LN length; seed                          |
| 2P                       | Switches the layout from 1P to 2P                               |         |
| Constant (CN)            | Disables scroll-speed changes, including #SPEED/#SCROLL/BPM     |         |
| Hide Scratch (HS)        | Removes scratch notes and hides the lane                        |         |
| No Mine (NM)             | Removes all landmines, including those in scratch lanes         |         |
| Background Keysound (BK) | Plays keysounds automatically with the song                     |         |
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
| No Bad (NB)              | Removes the BAD judgement window                           |         |
| LR2 (R2)                 | Uses LR2 judgement and gauge rules, with classic LN holds       |         |
| osu!mania (OM)           | Uses osu!mania judgements, health, and scoring                  | OD; HP; use lazer rules |
| Long Note (L1)           | LN: hold from head to tail for one combined judgement           |         |
| Charge Note (L2)         | CN: judges head and tail separately                            |         |
| Hell Charge Note (L3)    | HCN: separate head/tail judgements, plus body recovery and damage |         |

## Settings

| Setting                       | Default  | Range / options                            | Description                                                                                                                                                                                     |
|-------------------------------|----------|--------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Scroll speed | 8.0 | 1.0–50.0, step 0.1 | Note fall speed; in-game controls adjust it temporarily. |
| Reference BPM                 | Main BPM | Start BPM, Max BPM, Main BPM, or Min BPM   | Scroll-speed reference used when a chart has no `#BASEBPM`. Main BPM uses the BPM containing the most playable notes; ties use the earliest occurrence.                                       |
| Judgement selection algorithm | Combo priority (LR2) | Combo priority (LR2), Time difference priority (AC), Earliest note priority, Score priority | Selects which note receives a press when windows overlap. See [selection rules](./judgements.md#beatoraja-note-selection). |
| BGA dim | 70% | 0%–100% | 0% keeps full brightness; 100% fully darkens the BGA. |
| Unlock frame rate limit       | Off      | On / off                                  | Removes osu!'s 1000 Hz frame and input polling cap during BMS gameplay. Higher GC and GPU pressure may cause extra stutters; disable this option if that happens. |
| Use dedicated preview audio   | On       | On / off                                   | Uses `#PREVIEW` or `preview.*` when available. When disabled, song-select previews are synthesized only from BGM and keysound samples.                                                        |
| Fixed note visibility duration | Off | On / off | Reveals notes in slow sections closer to the judgement line, using the visible time at your reference scroll speed. |
| Note fade-in duration | 0 ms | -1000–1000 ms, step 1 ms | Positive values make notes start appearing earlier; negative values start the fade at the default appearance time, making notes fully visible later. Larger absolute values give a longer fade; 0 reveals notes instantly at the time determined by your reference scroll speed. |
| Visual offset | 0 ms | -500–500 ms, step 1 ms | Positive values display notes earlier (for more Slow judgements); negative values display them later (for more Fast judgements). |
| LN tail visual offset | 0 ms | 0–1000 ms, step 1 ms | Advances long-note tails visually, making holds look shorter. |
| Adjust visual offset automatically | Off | On / off | Applies each valid local play's suggested offset. See [calibration](#visual-offset-calibration). |
| Show BMS 5K                   | On       | On / off                                   | Shows or hides single-play BMS 5K charts in song select.                                                                                                                                       |
| Show BME 7K                   | On       | On / off                                   | Shows or hides single-play BME 7K charts in song select.                                                                                                                                       |
| Show Pop’n 9K                   | On       | On / off                                   | Shows or hides single-play Pop’n 9K charts in song select.                                                                                                                                       |
| Show BMS 5K DP                | On       | On / off                                   | Shows or hides double-play BMS 5K charts in song select.                                                                                                                                       |
| Show BME 7K DP                | On       | On / off                                   | Shows or hides double-play BME 7K charts in song select.                                                                                                                                       |
| Show Pop’n 9K DP                | On       | On / off                                   | Shows or hides double-play Pop’n 9K charts in song select.                                                                                                                                       |
| Show Pop’n 5K | On | On / off | Shows or hides Pop’n 5K charts. |
| Show Keyboard 24K | On | On / off | Shows or hides Keyboard 24K charts. |
| Show Keyboard 24K DP | On | On / off | Shows or hides double-play Keyboard 24K charts. |
| Show generic keyboard layouts | On | On / off | Shows or hides all generic keyboard layouts. |

Layout filters take effect immediately in song select.

The **Beatmap management** group at the bottom of BMS settings contains chart visibility filters and import, cleanup, and deletion actions. [Difficulty-table management](./difficulty-tables.md) appears above it.

### Visual Offset Calibration

Visual offset adjusts when notes appear at the judgement line. LN tail visual offset makes holds look shorter by
advancing their displayed tails.

Manual local plays with at least 50 timed hits generate suggestions from their median hit error. Apply the average
of recent suggestions in the BMS settings, or enable **Adjust visual offset automatically** to apply each new
suggestion after a play.

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
