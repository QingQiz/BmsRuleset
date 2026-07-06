# osu! BMS Ruleset

Native osu! ruleset plugin for BMS-family charts (`.bms`, `.bme`, `.bml`, `.pms`).

---

[中文](./README.zh-CN.md)

## Installation

1. Cloning

   When cloning this repository, you can skip the `bms_test_songs` folder to save time and disk space — it contains
   large test audio files only needed for running tests:

    ```bash
    git clone --filter=blob:none --sparse https://github.com/QINGQIZ/BmsRuleset.git
    cd BmsRuleset
    git sparse-checkout set --no-cone '/*' '!bms_test_songs'
    ```

   To restore the folder later (e.g., to run tests):

    ```bash
    git sparse-checkout add bms_test_songs
    ```

2. Build the ruleset:
   ```
   dotnet build "osu.Game.Rulesets.BmsRuleset"
   ```
   Output: `osu.Game.Rulesets.BmsRuleset/bin/Debug/net8.0/osu.Game.Rulesets.BmsRuleset.dll`

3. Copy the DLL to your osu! `rulesets/` folder.

4. Restart osu!. The ruleset will appear in the ruleset selector (currently uses the osu!mania icon as a placeholder).

---

## Importing BMS Charts

1. Open osu! → **Settings** → find the **BMS** section.
2. Click **"Import BMS files"** to open the import screen.
3. Navigate to and select your BMS folder. Each chart file (`.bms`/`.bme`/`.bml`/`.pms`) in the folder becomes a
   separate beatmap; the whole folder becomes one beatmap set.
4. **Only chart files are stored in osu!'s internal database.** Audio and image resources stay on the original
   filesystem and are read directly during gameplay via the chart directory path tracked in `Metadata.Source`.
   Avoid moving or deleting the original BMS folder after import — orphaned sets can be cleaned up from the
   settings screen (see below).

To remove all imported BMS content, use the **"Delete all imported BMS files"** button in the same settings section.
This only removes the chart metadata from osu! — your original BMS folder is not affected.

To clean up beatmaps whose source directory has been moved or deleted, click **"Clean up orphaned BMS sets"** in
the same settings section. This scans for BMS beatmaps whose source directory no longer exists and marks them
for deletion.

---

## Features

<details>
<summary>click to open tech details</summary>

### Parser

**Header fields decoded:**

| Field                   | Command                                                                    | Description                                                                                       |
|-------------------------|----------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|
| Title, Artist, Subtitle | `#TITLE`, `#ARTIST`, `#SUBTITLE`                                           | Subtitle appended to title as `"Title - Subtitle"`                                                |
| SubArtist               | `#SUBARTIST`                                                               | Appended to artist as `"Artist (SubArtist)"`                                                      |
| Maker                   | `#MAKER`                                                                   | Mapped to beatmap Creator                                                                         |
| Genre                   | `#GENRE` (also `#GENLE`)                                                   | Song style, stored in Tags                                                                        |
| URL, Email              | `%URL`, `%EMAIL`                                                           | Stored in Tags                                                                                    |
| Comment                 | `#COMMENT`                                                                 | Stored in Tags                                                                                    |
| Play level              | `#PLAYLEVEL`                                                               | Displayed as difficulty name                                                                      |
| Preview audio           | `#PREVIEW`                                                                 | Declared preview file path for song select, falls back to `preview.*` then to BGM/keysound events |
| Banner image            | `#BANNER`                                                                  | Song select card image, fallback chain: #STAGEFILE -> #BACKBMP                                    |
| Background image        | `#STAGEFILE`                                                               | Song select background image, fallback chain: #BACKBMP -> #BANNER                                 |
| Judge rank              | `#RANK` (0–4)                                                              | Affects hit windows, mapped to `OD`                                                               |
| Gauge total             | `#TOTAL`                                                                   | Gauge recovery coefficient, mapped to `AR`                                                        |
| Base BPM (visual)       | `#BASEBPM`                                                                 | Scroll speed reference BPM, does not affect note timing; overrides the Reference BPM setting      |
| Initial BPM             | `#BPM`                                                                     | Default 130                                                                                       |
| Extended BPM table      | `#BPMxx`                                                                   | Real-number BPM (beyond 0–255 from channel `03`)                                                  |
| STOP table              | `#STOPxx`                                                                  | Stop sequence durations (1 unit = 1/192 of a 4/4 measure)                                         |
| Sample definitions      | `#WAVxx`                                                                   | Audio file paths (WAV/OGG)                                                                        |
| BGA image/video slots   | `#BMPxx`                                                                   | Image or video file paths for BGA layers (see Background Animation below)                         |
| BGA crop definitions    | `#BGAxx`                                                                   | Cropped BGA: `<bmp> <x1> <y1> <x2> <y2> <dx> <dy>` (7-field; w=x2−x1, h=y2−y1)                    |
| Poor BGA mode           | `#POORBGA 0/1/2`                                                           | 0=Replace (hide other layers on miss), 1=Add (overlay), 2=Off                                     |
| Long-note type          | `#LNTYPE 1` / `#LNTYPE 2`                                                  | LN notation: 1=RDM (default), 2=MGQ                                                               |
| Long-note marker        | `#LNOBJ`                                                                   | LN end-point marker value (accumulated in HashSet)                                                |
| Long-note lock mode     | `#LNMODE 1` / `#LNMODE 2` / `#LNMODE 3`                                    | Locks LN type: 1=LN, 2=CN (Charge Note), 3=HCN (Hell Charge Note)                                 |
| Text events             | `#TEXTxx`, `#SONGxx`                                                       | Displayed during gameplay on channel `99`                                                         |
| Base 62 extension       | `#BASE 62`                                                                 | Case-sensitive base-62 encoding for commands and channels                                         |
| Play mode               | `#PLAYER`                                                                  | Ignored                                                                                           |
| Random blocks           | `#RANDOM` / `#RONDAM` (typo tolerance), `#ENDRANDOM`, `#SETRANDOM`         | Random branch with conditional sub-blocks; `#SETRANDOM` fixes the value                           |
|                         | `#IF`, `#ELSEIF`, `#ELSE`, `#ENDIF` / `#END` / `#IFEND` / `#END IF`        |                                                                                                   |
| Switch blocks           | `#SWITCH`, `#ENDSW` / `#ENDSWITCH`, `#SETSWITCH`, `#CASE`, `#DEF`, `#SKIP` | Switch control flow with cases; `#SETSWITCH` fixes the value                                      |
| Scroll speed            | `#SCROLLxx`                                                                | Per-segment display multiplier on scroll coordinate                                               |
| Spacing change          | `#SPEEDxx`                                                                 | Per-segment multiplier on `ScrollSpeedMultiplier`                                                 |

**Not parsed:** `#EXWAVxx`,
`#WAVCMD`, `#VOLWAV`, `#MIDIFILE`, `#DIFFICULTY`, `#EXRANK` / `#EXRANKxx`,
`#DEFEXRANK`, `#EXBPMxx`, `#STP`, `#PATH_WAV` / `#PATH_BMP`, `#OPTION`,
`#CHANGEOPTIONxx`, `#SWBGAxx`, `#@BGAxx`, `#ARGBxx`, `#CHARFILE`,
`#ExtChr`, `#OCT/FP`, `#MATERIALS`

dynamic volume channels (`97`, `98`),
dynamic rank channel (`A0`), and dynamic option channel (`A6`).

> [!NOTE]
> The resolved preview source is exposed so callers can tell whether a declared single-file (`#PREVIEW` / `preview.*`)
> or BGM/keysound playback is active. BGM/keysound playback often gives a more representative preview; declared files
> may
> not reflect the chart's full audio content.
> Whether a user-facing "prefer BGM/keysound preview" option is needed depends on player feedback.

**Channels parsed:**

| Channel   | Meaning                                         |
|-----------|-------------------------------------------------|
| `01`      | BGM autoplay samples                            |
| `02`      | Measure length (time signature changes)         |
| `03`      | Inline hex BPM change (0–255)                   |
| `08`      | Extended BPM change (`#BPMxx` lookup)           |
| `09`      | STOP event (`#STOPxx` lookup)                   |
| `99`      | TEXT event (`#TEXTxx`/`#SONGxx` lookup)         |
| `SC`      | SCROLL factor change (`#SCROLLxx` lookup)       |
| `SP`      | SPEED factor change (`#SPEEDxx` lookup)         |
| `04`      | BGA base layer (`#BMPxx`/`#BGAxx`)              |
| `06`      | BGA poor layer (shown on MISS)                  |
| `07`      | BGA overlay layer 1                             |
| `0A`      | BGA overlay layer 2                             |
| `0B`–`0E` | BGA layer opacity (base/layer1/layer2/poor)     |
| `11`–`15` | Playable notes — P1 lanes 1–5 (origin: 5-key)   |
| `16`      | Scratch / turntable — P1                        |
| `17`      | Free-zone — P1                                  |
| `18`–`19` | Playable notes — P1 lanes 6–7 (7-key extension) |
| `21`–`25` | Playable notes — P2 lanes 1–5                   |
| `26`      | Scratch / turntable — P2                        |
| `27`      | Free-zone — P2                                  |
| `28`–`29` | Playable notes — P2 lanes 6–7                   |
| `51`–`59` | Long notes — P1 (mapped to `11`–`19`)           |
| `61`–`69` | Long notes — P2 (mapped to `21`–`29`)           |
| `D1`–`D9` | Landmine / mine — P1 (base-36 encoded)          |
| `E1`–`E9` | Landmine / mine — P2 (base-36 encoded)          |

**Not parsed:** invisible note channels (`31`–`39`, `41`–`49`),
dynamic BGM volume (`97`), dynamic KEY volume (`98`),
dynamic rank change (`A0`), dynamic option change (`A6`).

> Channel `02` controls per-measure length (time signature changes), defined by `#xxx02`. A value of `1` means standard
> length (4/4), `0.5` half length, `2` double length.
> Measure duration (ms) = `#xxx02 × 240000 / BPM` (at a fixed BPM).
> `1/1024` is the smallest value that can be accurately represented. Smaller values may round to 0 ticks, collapsing all
> events in that measure to the same position.

</details>

---

### Clear Lamps

BMS beatmap panels show a clear lamp based on your best matching local score. Lamps cover the usual BMS result states:
No Play, Failed, Assist Clear, Easy Clear, Clear, Hard Clear, EX Hard Clear, Full Combo, Perfect, and Max.

The lamp follows the currently selected gameplay-affecting mods when possible. For example, a score set with
Hide Scratch, Auto Scratch, Constant, Half Time, or Double Time is only used for the lamp when the matching mod setup is
selected in song select.

### Song Preview

Song select preview audio is generated from the original BMS folder rather than from a stored osu! audio file. The ruleset
first tries a declared `#PREVIEW` file, then a `preview.*` file in the chart folder, and finally falls back to the chart's
BGM/keysound event timeline. This makes charts without a dedicated preview file still audible in song select.

---

## Input and Key Bindings

Default bindings (all rebindable in **Settings → Key Bindings → osu!BMS**):

**ingame controls:** increase/decrease scroll speed temporarily

Key sound of the next upcoming note in a column plays on every key press regardless of judgement result.

---

## Judgements and Scoring

**Judgement tiers (beatoraja timing windows, from `#RANK`):**

| Name       | EX pts | Combo        | RANK 2 (Normal) window           |
|------------|--------|--------------|----------------------------------|
| **PGREAT** | 2      | kept         | ±15 ms                           |
| **GREAT**  | 1      | kept         | ±45 ms                           |
| **GOOD**   | 0      | kept         | ±112.5 ms                        |
| **BAD**    | 0      | reset        | -220ms / +280ms                  |
| **POOR**   | 0      | reset        | > +280ms                         |
| **E-POOR** | 0      | **no break** | [-500ms,-220ms], no note consume |

`#RANK` 0 = Very Hard (±5/15/37.5 ms, BAD -220/+280 ms) → 4 = Very Easy (±25/75/187.5 ms, BAD -220/+280 ms).

**Score:** `total EX score / max EX score × 1,000,000`

**DJ LEVEL rank:** X (all PGREAT) · S ≥ 8/9 · A ≥ 7/9 · B ≥ 6/9 · C ≥ 5/9 · D otherwise

---

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

---

## Mods

| Mod                      | Description                                                     |         |
|--------------------------|-----------------------------------------------------------------|---------|
| Autoplay                 | auto play                                                       |         |
| Double Time / Half Time  |                                                                 |         |
| No Fail                  |                                                                 |         |
| Mirror                   | Mirrors the key layout                                          |         |
| 2P                       | change the player layout from 1P to 2P                          |         |
| Auto Scratch (AS)        | auto play/hide scratch lane                                     |         |
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

---

## Settings

| Setting              | Default | Range    | Description                                                                                                                |
|----------------------|---------|----------|----------------------------------------------------------------------------------------------------------------------------|
| Scroll Speed         | 8.0     | 1.0–60.0 | Note fall speed. In-game `Up`/`Down` keys adjust temporarily. Keys are rebindable under Settings → Key Bindings → osu!BMS. |
| Reference BPM        | Main BPM | enum     | Scroll speed reference BPM used when a chart has no `#BASEBPM`: Start BPM, Max BPM, Main BPM, or Min BPM. Main BPM uses the BPM with the most playable notes; ties use the earliest occurrence. |
| BGA Dim              | 0.7     | 0–1      | Background animation dim. 0 = full brightness, 1 = hidden (BGA still present, just invisible)                              |
| Show 5K / 7K / 9K    | ✓       | on/off   | Toggle visibility of single-play layouts in song select                                                                    |
| Show DP 5K / 7K / 9K | ✓       | on/off   | Toggle visibility of double-play layouts in song select                                                                    |

Layout visibility filters are applied live in song select — uncheck a layout to hide all beatmaps of that type.

You can also filter by key count from the search box using `k=`, `key=` or `keys=` (supports operators `=`, `!=`, `<`,
`<=`, `>`, `>=` and comma-separated values, e.g. `keys=7` or `k>5`).

---

## Difficulty Tables

Difficulty tables (LR2/beatoraja format) provide difficulty ratings and markers for BMS charts. The ruleset supports
importing tables from a JSON file or URL, and automatically matches charts by MD5 hash.

### Preset Tables

The following well-known tables are available as one-click presets in the autocomplete dropdown:

| Table                 | Symbol | URL                                                    |
|-----------------------|--------|--------------------------------------------------------|
| Satellite (sl)        | sl     | `http://zris.work/bmstable/satellite/header.json`      |
| Stella (st)           | st     | `http://zris.work/bmstable/stella/header.json`         |
| 発狂BMS難易度表 (★)         | ★      | `http://zris.work/bmstable/insane/insane_header.json`  |
| 通常難易度表 (☆)            | ☆      | `http://zris.work/bmstable/normal/normal_header.json`  |
| NEW GENERATION 発狂 (▼) | ▼      | `http://zris.work/bmstable/insane2/insane_header.json` |
| 第三期Overjoy (★★)       | ★★     | `http://zris.work/bmstable/overjoy/header.json`        |
| Scramble (SB)         | SB     | `http://zris.work/bmstable/scramble/header.json`       |
| Luminous (ln)         | ln     | `http://zris.work/bmstable/luminous/header.json`       |
| BMS図書館 (T)            | T      | `http://zris.work/bmstable/turbow/header.json`         |

### Importing a Table

1. Open **Settings → BMS** → scroll to **Difficulty Tables**.
2. Paste a URL (e.g. `http://zris.work/bmstable/turbow/header.json`) or a local file path into the text box.
3. Click **Import** (or press Enter).

The importer supports three JSON formats:

- **Separate files** — `header.json` + `data.json` linked by `data_url`
- **Combined file** — a single JSON with both header fields (name, symbol, level_order) and `"charts": [...]`
- **HTML page** — a web page with `<meta name="bmstable" content="URL">` pointing to the JSON

### How Markers Work

After import, every beatmap whose MD5 matches a table entry gets a **marker** appended to its difficulty name:

```
DP ☆NOTHER [TT★1 TT★2]
```

The marker shows the table symbol and the entry's level. Markers update automatically when tables are added or removed.

> [!IMPORTANT]
> Do **not** add or remove difficulty tables while on the **song select** screen.
> Removing a table triggers a full marker rebuild across all BMS beatmaps, which contends with the
> beatmap carousel's active Realm reads and will freeze the UI. Always switch to the **main menu**
> before importing or deleting a difficulty table.

### Collections

Each table also creates a **BeatmapCollection** named `[BMS] {table.Name}` containing all matched charts. This lets you
browse the table's songs directly from the song select collection list.
(The collection name uses invisible characters under the hood, so you don't need to worry about it colliding with your
own collections.)

### Table Row Display

Each imported table shows its name and symbol in the settings list. Long names wrap to fit. Hover a row
to see a tooltip with the chart count per level.

### Subdivide

Click **Subdivide** to split a table's collection into per-level collections with ordering indices
(e.g., `[BMS] Table [00] ★1`, `[BMS] Table [01] ★2`). The index width adapts to the number of levels
(1 digit for &lt;10, 2 for &lt;100, etc.). Click **Unsubdivide** to merge them back into one.

### Update

Click **Upd** to re-import the table from its original source URL or file path.

---

## Skin System

### How It Works

The ruleset uses a **three-layer skin fallback chain**:

```
1. Chart's own embedded skin (if present)
2. Your current osu! user skin (if it provides BMS resources)
3. Ruleset built-in fallback skin (always present)
```

A user skin is recognized as providing BMS resources if:

- Its `skin.ini` contains a `[BMS]` section, **or**
- Its `skin.ini` contains a `[Mania]` section with the correct key count, **or**
- It has `mania-key1` or `mania-keyS` textures

If none of these apply, the ruleset's built-in skin is used. Two built-in variants exist:

- **LegacyModern** — used when your active osu! skin is Argon/Triangles
- **LegacyOld** — used when your active osu! skin is the default legacy skin

### Creating a Skin

Place your images and a `skin.ini` in a folder, then import it as a normal osu! skin.

#### skin.ini — `[BMS]` Section

Write one `[BMS]` section per layout you want to support. The `Layout:` key is required.

**Layout values:** `5K`, `7K`, `9K`, `10K`, `14K`, `18K`  
(Aliases: `BMS5K`, `BME7K`, `PMS9K`, `BMS5KDouble`, `BME7KDouble`, `PMS9KDouble`)

#### All Supported Keys

**Layout and positions:**

| Key              | Description                                 | Example |
|------------------|---------------------------------------------|---------|
| `Layout`         | Layout identifier (required)                | `7K`    |
| `HitPosition`    | Hit target Y from bottom (480-height space) | `440`   |
| `LightPosition`  | Column key-light Y                          | `440`   |
| `ScorePosition`  | Judgement popup Y                           | `250`   |
| `ComboPosition`  | Combo counter Y from top                    | `300`   |
| `JudgementLine`  | Show white line at hit position (`1`/`0`)   | `1`     |
| `KeysUnderNotes` | Draw key images under notes (`1`/`0`)       | `0`     |

**Column geometry:**

| Key                       | Description                                             | Example                   |
|---------------------------|---------------------------------------------------------|---------------------------|
| `ColumnWidth`             | Comma-separated widths for all N columns                | `45,45,45,45,45,45,45,45` |
| `ColumnLineWidth`         | Separator line widths — N+1 values                      | `0,1,1,1,1,1,1,1,0`       |
| `ColumnSpacing`           | Spacing between columns                                 | `2,2,2,2,2,2,2`           |
| `WidthForNoteHeightScale` | Reference width for note height scaling                 | `50`                      |
| `BarlineHeight`           | Bar line height multiplier (a line before each measure) | `1.2`                     |

> **Scratch column special notes**
>
> Column index 0 is **always** the scratch column, regardless of visual position.
> - `ColumnWidth` — the first value is always the scratch lane width
> - `NoteImage0` / `KeyImage0` — always refer to the scratch column
>
> With the **2P mod** (SecondPlayer), scratch moves to the rightmost visual position.
> Skin config applies automatically — no duplicate `[BMS]` section needed.
>
> For 5K (`s,1,2,3,4,5`):
>
> **ColumnLineWidth index remapping under 2P:**
>
> ```
> 1P visual order:   [s] [1] [2] [3] [4] [5]
> gap index:        0   1   2   3   4   5   6
>
> 2P visual order:   [1] [2] [3] [4] [5] [s]
> gap index:        0   2   3   4   5   1   6
> ```
>
> **ColumnSpacing index remapping under 2P:**
>
> ```
> 1P visual order: [s] [1] [2] [3] [4] [5]
> gap index:          1   2   3   4   5
>
> 2P visual order: [1] [2] [3] [4] [5] [s]
> gap index:          2   3   4   5   1
> ```

**Colors:**

| Key                           | Description                      | Example           |
|-------------------------------|----------------------------------|-------------------|
| `ColourColumnLine`            | Column separator color (R,G,B,A) | `255,255,255,50`  |
| `ColourJudgementLine`         | Hit target line color            | `255,255,255,255` |
| `ColourBarline`               | Measure bar line color           | `0,255,0,255`     |
| `ColourBreak`                 | Combo break flash color          | `255,0,0`         |
| `Colour1`–`ColourN`           | Per-column background color      | `0,0,0,0`         |
| `ColourLight1`–`ColourLightN` | Per-column key-light glow color  | `255,200,0`       |
| `Colour`                      | All-column background shorthand  | `0,0,0,0`         |
| `ColourLight`                 | All-column light shorthand       | `0,0,0`           |

**Fonts:**

| Key           | Description                                   | Default |
|---------------|-----------------------------------------------|---------|
| `ComboPrefix` | Texture prefix for combo counter digit images | `score` |

The combo counter loads textures as `{ComboPrefix}-0.png` through `{ComboPrefix}-9.png`

If the digit textures are missing, the combo counter is silently hidden.

**Note images:**

| Key                                           | Description                                    |
|-----------------------------------------------|------------------------------------------------|
| `NoteImage0`–`NoteImageN`                     | Per-column normal note image                   |
| `NoteImage0L`–`NoteImageNL` (or `NoteImageL`) | Per-column LN body image                       |
| `NoteImage0T`–`NoteImageNT` (or `NoteImageT`) | Per-column LN tail image                       |
| `NoteImage0H`–`NoteImageNH`                   | Per-column LN head (falls back to `NoteImage`) |
| `MineImage` / `MineImage0`–`MineImageN`       | Landmine image                                 |

**Key images:**

| Key                       | Description                  |
|---------------------------|------------------------------|
| `KeyImage0`–`KeyImageN`   | Per-column key (not pressed) |
| `KeyImage0D`–`KeyImageND` | Per-column key (pressed)     |

**Stage and effects:**

| Key                   | Description                                                    |
|-----------------------|----------------------------------------------------------------|
| `StageHint`           | Hit target image                                               |
| `StageLeft`           | Left stage border image                                        |
| `StageRight`          | Right stage border image                                       |
| `StageBottom`         | Bottom stage foreground image                                  |
| `StageLight`          | Column light/glow image (shown while key is held)              |
| `LightingN`           | Normal hit explosion image                                     |
| `LightingL`           | LN hit explosion image (also pulses during LN hold)            |
| `LightingNWidth`      | Normal explosion scale widths, comma-separated per column      |
| `LightingLWidth`      | LN explosion scale widths, comma-separated per column          |
| `LightFramePerSecond` | Column light animation FPS (alias: `StageLightFramePerSecond`) |

**Judgement images:**

| Key         | BMS judgement |
|-------------|---------------|
| `HitPGreat` | PGREAT        |
| `HitGreat`  | GREAT         |
| `HitGood`   | GOOD          |
| `HitBad`    | BAD           |
| `HitPoor`   | POOR / E-POOR |

#### Frame Animations (N-suffix)

Most image assets can be provided as multi-frame animations. Append `-0`, `-1`, `-2`, … to the
texture name:

```
lightingN-0.png
lightingN-1.png
lightingN-2.png
```

If frames are found, they play as an animation at the rate specified by `LightFramePerSecond`
(for `StageLight`) or a frame-length derived from frame count (for `LightingN`/`LightingL`).
If only a single frame (the plain name, e.g. `lightingN.png`) is found, it is used as a static
sprite.

This works for any image key: `NoteImage`, `KeyImage`/`KeyImageD`, `StageLight`, `LightingN`,
`LightingL`, `StageHint`, judgement images, etc.

#### osu!mania Skin Compatibility

You can also use `[Mania]` sections from a standard osu!mania skin. The ruleset will match on:

- `[Mania]` with `SpecialStyle: 1` and `Keys: 6` → 5K layout
- `[Mania]` with `SpecialStyle: 1` and `Keys: 8` → 7K layout
- `[Mania]` with the exact key count

In `[Mania]` sections, use mania standard judgement names: `Hit300g` (PGREAT), `Hit300` (GREAT), `Hit200` (GOOD),
`Hit50` (BAD), `Hit0` (POOR).

### HUD Components

HUD components implement `ISerialisableDrawable` and can be repositioned and resized freely in the
**Skin Editor** during gameplay. Open the skin editor and drag any component to your preferred position.

On the next play session the saved layout is automatically loaded.

Additionally, select a component in the skin editor to configure its properties in the sidebar:

| HUD components | Skin editor properties                                                                                                                                                                                         |
|----------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Combo          | Auto-hide delay, min visible combo.                                                                                                                                                                            |
| Judgement      | *(none)*                                                                                                                                                                                                       |
| Health Bar     | **Groove gauge colours** — low (red zone), mid (yellow zone), high (green zone). **Fixed gauge colours** — Hard, ExHard, Hazard, each independently editable. All colours have a colour picker in the sidebar. |
| BGA            | *(none)* — renders behind the playfield; aspect-fit (letterbox) is fixed. BGA dim is a global setting, not per-component.                                                                                      |
| Text           | *(none)* — shows a "Sample Text Event" placeholder while editing so the (otherwise alpha=0) box can be positioned. Driven by channel `99` / `#TEXTxx` at runtime.                                              |

### Example skin.ini (7K)

```ini
[General]
Name : My BMS Skin
Author : yourname

[BMS]
Layout : 7K

HitPosition : 440
LightPosition : 440
ScorePosition : 250
JudgementLine : 1
KeysUnderNotes : 0

LightFramePerSecond : 40
ColumnWidth : 45,45,45,45,45,45,45,45
WidthForNoteHeightScale : 50
BarlineHeight : 1.2

ColourBarline : 0,255,0,255
ColourColumnLine : 255,255,255,50
ColourJudgementLine : 255,255,255,255
ColumnLineWidth : 0,1,1,1,1,1,1,1,0

Colour : 0,0,0,0
ColourLight : 0,0,0

; BMS 7K column order: scratch even odd even center even odd even
NoteImage0 : note-scratch
NoteImage1 : note-even
NoteImage2 : note-odd
NoteImage3 : note-even
NoteImage4 : note-center
NoteImage5 : note-even
NoteImage6 : note-odd
NoteImage7 : note-even

NoteImageL : note-ln-body
NoteImageT : note-ln-tail
MineImage : note-mine

KeyImage0 : mania-keyS
KeyImage0D : mania-keySD
KeyImage1 : mania-key1
KeyImage1D : mania-key1D
KeyImage2 : mania-key2
KeyImage2D : mania-key2D
KeyImage3 : mania-key1
KeyImage3D : mania-key1D
KeyImage4 : mania-key2
KeyImage4D : mania-key2D
KeyImage5 : mania-key1
KeyImage5D : mania-key1D
KeyImage6 : mania-key2
KeyImage6D : mania-key2D
KeyImage7 : mania-key1
KeyImage7D : mania-key1D

StageHint : mania-stage-hint
StageLeft : stage-left
StageRight : stage-right
StageBottom : stage-bottom
StageLight : stage-light
LightingN : lightingN
LightingL : lightingL
LightingNWidth : 50,50,50,50,50,50,50,50
LightingLWidth : 50,50,50,50,50,50,50,50

HitPGreat : j-pgreat
HitGreat : j-great
HitGood : j-good
HitBad : j-bad
HitPoor : j-poor
```

The full built-in skin (covering all 6 layouts) is at
`osu.Game.Rulesets.BmsRuleset/Resources/Skins/Modern/skin.ini` — use it as a reference.

---

## Comprehensive BMS Command Reference

<details>
<summary>click to open tech details</summary>

This section catalogs **all known BMS commands** (header, channel, and control flow) across the
original BM98 specification, community players (LR2, beatoraja), and extended format proposals.
Commands are grouped by origin and listed with their status in this ruleset.

> Legend: ✓ = implemented · ◐ = partial · ✗ = not implemented · — = N/A

### 1. Header Commands

#### 1.1 BM98 Original (1998)

| Command         | Status | Notes                                                                         |
|-----------------|--------|-------------------------------------------------------------------------------|
| `#PLAYER [1-4]` | —      | 1=Single, 2=Couple, 3=Double, 4=Battle; ignored here, layout inferred instead |
| `#GENRE`        | ✓      | Also accepts `#GENLE` typo                                                    |
| `#TITLE`        | ✓      |                                                                               |
| `#ARTIST`       | ✓      |                                                                               |
| `#BPM`          | ✓      | Initial BPM (default 130)                                                     |
| `#MIDIFILE`     | ✗      | MIDI background music                                                         |
| `#PLAYLEVEL`    | ✓      | Difficulty level displayed as difficulty name                                 |
| `#RANK [0-3]`   | ✓      | Judgment: 0=Very Hard, 1=Hard, 2=Normal, 3=Easy; also accepts 4 (Very Easy)   |
| `#VOLWAV`       | ✗      | Global volume scalar (0–100) for WAV playback                                 |
| `#WAVxx`        | ✓      | Audio file definitions (xx = 00–ZZ base-62)                                   |
| `#BMPxx`        | ✓      | Bitmap image/video definitions (xx = 00–FF hex, later extended to base-62)    |
| `#BMP00`        | ✓      | Special: shown on POOR judgment                                               |

#### 1.2 bemaniaDX Extensions (c. 2000)

| Command  | Status | Notes                                                                   |
|----------|--------|-------------------------------------------------------------------------|
| `#BPMxx` | ✓      | Real-number BPM definitions (>255 or decimal), referenced by channel 08 |
| `#BGAxx` | ✓      | BGA image with trim coordinates: `BMPnum x1 y1 x2 y2 dx dy`             |

#### 1.3 BM98k Extensions

| Command      | Status | Notes                                        |
|--------------|--------|----------------------------------------------|
| `#STAGEFILE` | ✓      | Splash screen (640x480) shown during loading |

#### 1.4 DDR (Delight Delight Reduplication) Extensions

| Command        | Status | Notes                                                    |
|----------------|--------|----------------------------------------------------------|
| `#STOPxx`      | ✓      | Stop sequence duration (1 unit = 1/192 of a 4/4 measure) |
| `#BACKBMP`     | ✓      | Background image displayed behind gameplay area          |
| `#WAVxx` (ogg) | ✓      | Ogg Vorbis support via same `#WAVxx` command             |

#### 1.5 nanasigroove Extensions

| Command                                   | Status | Notes                                                        |
|-------------------------------------------|--------|--------------------------------------------------------------|
| `#SUBTITLE`                               | ✓      | Explicit subtitle                                            |
| `#DIFFICULTY [1-5]`                       | —      | Difficulty classification index                              |
| `#BANNER`                                 | ✓      | Banner image for music selection                             |
| `#RANK 4`                                 | ✓      | VERY EASY judgment; 1.2× wider than EASY                     |
| `#DEFEXRANK`                              | ✗      | Fine-grained judgment width multiplier (100 = EASY baseline) |
| `#EXRANKxx`                               | ✗      | Extended rank values for dynamic rank change (channel A0)    |
| `#WAV00`                                  | ✓      | Explosion sound for landmine objects (Dx/Ex channels)        |
| `#SWITCH` / `#ENDSW`                      | ✓      | Switch control-flow block                                    |
| `#CASE` / `#SKIP` / `#DEF` / `#SETSWITCH` | ✓      | Sub-commands within switch block                             |
| `#OPTION`                                 | ✗      | Forced gameplay option                                       |
| `#CHANGEOPTIONxx`                         | ✗      | Dynamic option change during play (channel A6)               |
| `#BMPxx` (base-36/base-62)                | ✓      | Extended BMP index range using the active value encoding     |

#### 1.6 LR2 (Lunatic Rave 2) Extensions

| Command          | Status | Notes                                                         |
|------------------|--------|---------------------------------------------------------------|
| `#SUBARTIST`     | ✓      | Contributor names (waveform-slicer, movie-maker, noter, etc.) |
| `#BMPxx` (video) | ✓      | Video file assigned to BMP slot                               |

#### 1.7 RDM / ruv-it Extensions (Long Notes)

| Command     | Status | Notes                                                        |
|-------------|--------|--------------------------------------------------------------|
| `#LNOBJ`    | ✓      | LN end-point marker value (accumulated in HashSet)           |
| `#LNTYPE 1` | ✓      | RDM-notation LN: first non-00 starts LN, next non-00 ends it |
| `#LNTYPE 2` | ✓      | MGQ-notation LN: non-00 opens section, next 00 closes it     |

#### 1.8 feeling pomu / pomu2 Extensions

| Command      | Status | Notes                                              |
|--------------|--------|----------------------------------------------------|
| `#CHARFILE`  | ✗      | Character file for pop'n music character imitation |
| `#TEXTxx`    | ✓      | In-game text display strings (channel 99)          |
| `#SETRANDOM` | ✓      | Fixes random block to a constant value for testing |

#### 1.9 MacBeat Extensions

| Command   | Status | Notes                                                            |
|-----------|--------|------------------------------------------------------------------|
| `#WAVCMD` | ✗      | Pseudo-MOD effect command per WAV slot; files use .MBM extension |

#### 1.10 BM98de (Drink Edition) Extensions

| Command           | Status | Notes                                         |
|-------------------|--------|-----------------------------------------------|
| `#BGAxx` (coords) | ✓      | BGA with partial trim and display coordinates |

#### 1.11 bemaniaDX Additions

| Command | Status | Notes                            |
|---------|--------|----------------------------------|
| `#STP`  | ✗      | STOP sequence (multiple allowed) |

#### 1.12 DTX/GDAC2/BMEV Extensions

| Command     | Status | Notes                                 |
|-------------|--------|---------------------------------------|
| `#PATH_WAV` | ✗      | Source directory prefix for WAV files |
| `#PATH_BMP` | ✗      | Source directory prefix for BMP files |

#### 1.13 BMSManager Extensions

| Command  | Status | Notes                              |
|----------|--------|------------------------------------|
| `%URL`   | ✓      | URL metadata, auto-added by BMSC   |
| `%EMAIL` | ✓      | Email metadata, auto-added by BMSC |

#### 1.14 beatoraja Extensions

| Command              | Status | Notes                                                                          |
|----------------------|--------|--------------------------------------------------------------------------------|
| `#PREVIEW`           | ✓      | Preview audio path for music selection; auto-fallback to `preview.*` in folder |
| `#LNMODE [1-3]`      | ✓      | 1=LN, 2=CN (Charge Note), 3=HCN (Hell Charge Note); locks LN type              |
| `#RANK 4` (redef)    | ✓      | VERY EASY: 1.2× wider than EASY (#RANK 3)                                      |
| `#DEFEXRANK` (redef) | ✗      | Overrides `#RANK`; value 100 = EASY baseline                                   |

#### 1.15 Generalized / Modern Extensions

| Command         | Status | Notes                                                                    |
|-----------------|--------|--------------------------------------------------------------------------|
| `#EXBPMxx`      | ✗      | `#BPMxx` alias (BMSC parser bug workaround)                              |
| `#BASEBPM`      | ✓      | Visual scroll speed reference BPM (does not affect timing); overrides the Reference BPM setting |
| `#SONGxx`       | ✓      | Song-related text (merged with `#TEXTxx`)                                |
| `#MAKER`        | ✓      | Charter/noter name                                                       |
| `#EXWAVxx`      | ✗      | Extended WAV with pan/volume/frequency (nanasi)                          |
| `#EXBMPxx`      | ✗      | Extended BMP definition slot                                             |
| `#EXRANK`       | ✗      | Extended rank definition header                                          |
| `#POORBGA`      | ✓      | POOR BGA display mode (0=Replace, 1=Add, 2=Off)                          |
| `#SWBGAxx`      | ✗      | Switchable BGA definition                                                |
| `#@BGAxx`       | ✗      | Extended BGA crop with dest w/h (9 fields); only 7-field `#BGAxx` parsed |
| `#ARGBxx`       | ✗      | ARGB color/alpha definition for BGA elements                             |
| `#POORBGAxx`    | ✗      | Per-slot POOR BGA crop definition (distinct from scalar `#POORBGA` mode) |
| `#BGAEXPAND`    | ✗      | Global BGA scaling: 0=stretch, 1=keep aspect, 2=no expand                |
| `#BGAOFF`       | ✗      | Disable BGA for the chart                                                |
| `#SCROLLxx`     | ✓      | Scroll speed change definitions; per-segment visual multiplier           |
| `#SPEEDxx`      | ✓      | Spacing change definitions via ChartSpeedFactor`                         |
| `#VIDEOFILE`    | ✗      | Video file path                                                          |
| `#MOVIE`        | ✗      | Movie file path                                                          |
| `#SEEKxx`       | ✗      | Seek position for video                                                  |
| `#VIDEOf/s`     | ✗      | Video frame rate setting                                                 |
| `#VIDEOCOLORS`  | ✗      | Video color configuration                                                |
| `#VIDEODLY`     | ✗      | Video delay setting                                                      |
| `#OCT/FP`       | ✗      | Octave/FootPedal play mode flag                                          |
| `MATERIALS`     | ✗      | Materials section marker                                                 |
| `#MATERIALSWAV` | ✗      | Materials audio definition                                               |
| `#MATERIALSBMP` | ✗      | Materials image definition                                               |
| `#DIVIDEPROP`   | ✗      | Divide property configuration                                            |
| `#CHARSET`      | ✗      | Character encoding specification                                         |
| `#CDDA`         | ✗      | CD audio track reference                                                 |
| `#ExtChr`       | ✗      | BM98 proprietary: extended character sprite display                      |

### 2. Channel Identifiers

#### 2.1 Audio and Timing

| Channel | Name               | Status | Description                                           |
|---------|--------------------|--------|-------------------------------------------------------|
| `01`    | BGM                | ✓      | Auto-played audio samples (not merged across lines)   |
| `02`    | Measure Length     | ✓      | Time signature: 1 = 4/4, float supported              |
| `03`    | BPM (hex)          | ✓      | Integer BPM 0–255 via hex values                      |
| `08`    | Extended BPM       | ✓      | Real-number BPM via `#BPMxx` lookup                   |
| `09`    | STOP sequence      | ✓      | Freeze duration via `#STOPxx` lookup                  |
| `97`    | Dynamic BGM volume | ✗      | BGM volume change mid-chart (fgt)                     |
| `98`    | Dynamic KEY volume | ✗      | Key sound volume change mid-chart (counterpart to 97) |

#### 2.2 BGA (Background Animation)

| Channel   | Name        | Status | Description                                         |
|-----------|-------------|--------|-----------------------------------------------------|
| `04`      | BGA Base    | ✓      | Background image layer (via `#BMPxx`) - normal play |
| `06`      | BGA Poor    | ✓      | Background image on POOR judgment (via `#BMPxx`)    |
| `07`      | BGA Layer   | ✓      | Overlay layer on top of `04` (BM98k)                |
| `0A`      | BGA Layer 2 | ✓      | Additional overlay layer                            |
| `0B`–`0E` | BGA opacity | ✓      | Opacity changes for base/layer/layer2/poor BGA      |

> [!NOTE]
> Layer z-order (back-to-front): Base (`04`) → Layer 1 (`07`) → Layer 2 (`0A`) → Poor (`06`, replaces the
> others while active on MISS). Opacity channels `0B`–`0E` are decoded as hex bytes (`00`–`FF` → 0–1); whether
> LR2 uses hex-byte vs base-36 encoding is a pending spec verification. See *Not Yet Implemented* below for
> BGA rendering gaps (video time-base, etc.).

#### 2.3 Playable Note Lanes — Player 1

| Channel | Name      | Status | Description                                           |
|---------|-----------|--------|-------------------------------------------------------|
| `11`    | KEY 1     | ✓      | Lane 1 (5-key origin, leftmost)                       |
| `12`    | KEY 2     | ✓      | Lane 2                                                |
| `13`    | KEY 3     | ✓      | Lane 3                                                |
| `14`    | KEY 4     | ✓      | Lane 4                                                |
| `15`    | KEY 5     | ✓      | Lane 5 (5-key origin, rightmost)                      |
| `16`    | SCRATCH   | ✓      | Scratch / turntable lane                              |
| `17`    | FREE-ZONE | ◐      | Free scratch zone; repurposed as foot pedal/9K button |
| `18`    | KEY 6     | ✓      | 7-key extension (FlashTerminal/Project2DX)            |
| `19`    | KEY 7     | ✓      | 7-key extension (rightmost in 7K)                     |

#### 2.4 Playable Note Lanes — Player 2 (Couple / Double)

| Channel | Name      | Status | Description            |
|---------|-----------|--------|------------------------|
| `21`    | KEY 1     | ✓      | P2 lane 1              |
| `22`    | KEY 2     | ✓      | P2 lane 2              |
| `23`    | KEY 3     | ✓      | P2 lane 3              |
| `24`    | KEY 4     | ✓      | P2 lane 4              |
| `25`    | KEY 5     | ✓      | P2 lane 5              |
| `26`    | SCRATCH   | ✓      | P2 scratch / turntable |
| `27`    | FREE-ZONE | ◐      | P2 free scratch zone   |
| `28`    | KEY 6     | ✓      | P2 7-key extension     |
| `29`    | KEY 7     | ✓      | P2 7-key extension     |

#### 2.5 Invisible Notes

| Channel   | Name                         | Status | Description                           |
|-----------|------------------------------|--------|---------------------------------------|
| `31`–`36` | 1P Invisible KEY1–5, SCRATCH | ✗      | Not displayed, not judged, not scored |
| `38`–`39` | 1P Invisible KEY6–7          | ✗      | 7-key invisible extension             |
| `41`–`46` | 2P Invisible KEY1–5, SCRATCH | ✗      | P2 invisible notes                    |
| `48`–`49` | 2P Invisible KEY6–7          | ✗      | P2 7-key invisible extension          |

#### 2.6 Long Notes

| Channel   | Name            | Status | Description                               |
|-----------|-----------------|--------|-------------------------------------------|
| `51`      | 1P LN KEY1      | ✓      | LN mapped to column of channel `11`       |
| `52`      | 1P LN KEY2      | ✓      | LN mapped to column of channel `12`       |
| `53`      | 1P LN KEY3      | ✓      | LN mapped to column of channel `13`       |
| `54`      | 1P LN KEY4      | ✓      | LN mapped to column of channel `14`       |
| `55`      | 1P LN KEY5      | ✓      | LN mapped to column of channel `15`       |
| `56`      | 1P LN SCRATCH   | ✓      | LN mapped to scratch column               |
| `57`      | 1P LN KEY6/Free | ✓      | LN mapped to column of channel `17`/`18`  |
| `58`      | 1P LN KEY7      | ✓      | LN mapped to column of channel `18`/`19`  |
| `59`      | 1P LN KEY8      | ✓      | LN mapped to column of channel `19`       |
| `61`      | 2P LN KEY1      | ✓      | P2 long notes (mirrors `51`–`59` mapping) |
| `62`–`69` | 2P LN KEY2–8    | ✓      | P2 long notes                             |

#### 2.7 Landmine / Mine

| Channel   | Name                        | Status | Description                                            |
|-----------|-----------------------------|--------|--------------------------------------------------------|
| `D1`–`D9` | 1P Landmine KEY1–8, SCRATCH | ✓      | Penalty objects (base-36 encoded), sound from `#WAV00` |
| `E1`–`E9` | 2P Landmine KEY1–8, SCRATCH | ✓      | P2 landmines                                           |

#### 2.8 Extended Control

| Channel | Name          | Status | Description                                          |
|---------|---------------|--------|------------------------------------------------------|
| `99`    | TEXT Display  | ✓      | In-game text via `#TEXTxx` / `#SONGxx`               |
| `A0`    | RANK Change   | ✗      | Dynamic rank change via `#EXRANKxx` (nanasigroove)   |
| `A6`    | OPTION Change | ✗      | Dynamic option change via `#CHANGEOPTIONxx`          |
| `SC`    | SCROLL Change | ✓      | Applies `#SCROLLxx` factor to scroll coordinates     |
| `SP`    | SPEED Change  | ✓      | Applies `#SPEEDxx` factor to `ScrollSpeedMultiplier` |

### 3. Control Flow Commands

#### 3.1 Random Block

Commands from the original BM98 spec (enhanced by angolmois and others):

| Command         | Status | Description                                              |
|-----------------|--------|----------------------------------------------------------|
| `#RANDOM n`     | ✓      | Opens random block with `n` variations (generates 1..n)  |
| `#RONDAM`       | ✓      | Common misspelling/tolerance for `#RANDOM`               |
| `#SETRANDOM n`  | ✓      | Fixes the random value to `n` for testing                |
| `#ENDRANDOM`    | ✓      | Closes the innermost random block                        |
| `#IF value`     | ✓      | Conditional: includes lines if random == value           |
| `#ELSEIF value` | ✓      | Alternative condition                                    |
| `#ELSE`         | ✓      | Default fallback branch                                  |
| `#ENDIF`        | ✓      | Ends IF block (also accepts `#END`, `#IFEND`, `#END IF`) |

#### 3.2 Switch Block

The `#SWITCH` family (nanasigroove origin) provides mutually exclusive sections:

| Command        | Status | Description                                   |
|----------------|--------|-----------------------------------------------|
| `#SWITCH n`    | ✓      | Opens switch block with n states              |
| `#SETSWITCH n` | ✓      | Fixes switch to value `n` for testing         |
| `#CASE n`      | ✓      | Case label within switch block                |
| `#DEF`         | ✓      | Default case                                  |
| `#SKIP`        | ✓      | Break/exit from switch fallthrough            |
| `#ENDSW`       | ✓      | Ends switch block (also accepts `#ENDSWITCH`) |

### 4. Index Range Evolution

The BMS format has evolved its index encoding across implementations:

| Era        | Format            | Range | Slots | Description                                                    |
|------------|-------------------|-------|-------|----------------------------------------------------------------|
| Early      | Hex               | 00–FF | 256   | `[0-9A-Fa-f][0-9A-Fa-f]`                                       |
| Pre-modern | Base-36 (limited) | 00–ZZ | 576   | `[0-9A-Fa-f][0-9A-Za-z]`; LR2-era                              |
| Modern     | Base-36 (full)    | 00–ZZ | 1296  | `[0-9A-Za-z][0-9A-Za-z]`; case-sensitive (requires `#BASE 62`) |

### 5. PMS (pop'n music) Channel Mapping

PMS files (`.pms` extension) reinterpret the standard channel layout for 9-key / 18-key play.

#### 5.1 Standard PMS (BMS-DP type)

| Button | 1P Channel | 2P Channel (18K) |
|--------|------------|------------------|
| 1      | `11`       | `21`             |
| 2      | `12`       | `22`             |
| 3      | `13`       | `23`             |
| 4      | `14`       | `24`             |
| 5      | `15`       | `25`             |
| 6      | `22`       | `28`             |
| 7      | `23`       | `29`             |
| 8      | `24`       | `26` (scratch)   |
| 9      | `25`       | `27` (free-zone) |

#### 5.2 BME-SP Type (9K)

| Button | Channel                     |
|--------|-----------------------------|
| 1–5    | `11`–`15`                   |
| 6–7    | `18`–`19`                   |
| 8      | `16` (scratch repurposed)   |
| 9      | `17` (free-zone repurposed) |

</details>

---

## Not Yet Implemented

<details>
<summary>click to open tech details</summary>

| Area          | What is missing                                                                                                               | Priority |
|---------------|-------------------------------------------------------------------------------------------------------------------------------|----------|
| **Audio**     | `#WAVCMD` (MacBeat) — pitch/volume/playback-time per WAV slot                                                                 |
| **Audio**     | `#EXWAVxx` (nanasi) — pan/volume/frequency per WAV file                                                                       |
| **Audio**     | `#VOLWAV` (BM98) — global volume scalar                                                                                       |
| **Audio**     | `HT`, `DT` preview audio only changed the time gap between events now                                                         | 4        |
| **Audio**     | `#xxx97` (fgt) — dynamic BGM volume change channel                                                                            |          |
| **Converter** | Mania 7K → BMS chart conversion                                                                                               | 3        |
| **Input**     | Scratch turntable semantics — scratch is routed as a plain column key                                                         |
| **Input**     | Judgement offset adjustment capability                                                                                        |
| **Mods**      | DP only mods (FLIP / BATTLE / SP -> DP / SYNCHRONIZE RANDOM / SYMMETRY RANDOM)                                                |          |
| **Parser**    | `#@BGAxx` — extended BGA crop with dest w/h (9 fields); only 7-field `#BGAxx` parsed                                          | 2        |
| **Parser**    | `#SWBGAxx` — switchable BGA definition                                                                                        | 3        |
| **Parser**    | `#ARGBxx` — ARGB color/alpha definition for BGA elements                                                                      | 3        |
| **Parser**    | `#EXBMPxx` — extended BMP definition slot                                                                                     | 3        |
| **Parser**    | `#POORBGAxx` — per-slot POOR BGA crop definition (distinct from scalar `#POORBGA` mode)                                       | 2        |
| **Parser**    | `#BGAEXPAND` — global BGA scaling mode (0=stretch, 1=keep aspect, 2=no expand)                                                | 2        |
| **Parser**    | `#BGAOFF` — disable BGA for the chart                                                                                         | 2        |
| **Parser**    | BMSON support                                                                                                                 |
| **Parser**    | `#BMPxx` / `#EXBMPxx` — image definitions (non-resource-scan)                                                                 |
| **Parser**    | `#CDDA` / `#MIDIFILE` — CD / MIDI                                                                                             |
| **Parser**    | `#CHARFILE` / `#ExtChr` — character / skin                                                                                    |
| **Parser**    | `#EXBPMxx` — `#BPMxx` alias (BMSC parser bug workaround)                                                                      |
| **Parser**    | `#EXRANK` / channel `A0` — extended rank definition                                                                           |
| **Parser**    | `#EXWAVxx`, `#WAVCMD`, `#VOLWAV` — advanced audio controls                                                                    |
| **Parser**    | `#MATERIALS` / `#MATERIALSWAV` / `#MATERIALSBMP` / `#DIVIDEPROP` — resource groups                                            |
| **Parser**    | `#OCT/FP` — octave / pedal                                                                                                    |
| **Parser**    | `#OPTION` — forced option                                                                                                     |
| **Parser**    | `#PATH_WAV` / `#PATH_BMP` — resource path prefixes                                                                            |
| **Parser**    | `#MOVIE` — metadata only                                                                                                      |          | 
| **Parser**    | `#STP` — absolute STOP sequence                                                                                               |
| **Parser**    | `#VIDEOFILE` / `#VIDEOf/s` / `#VIDEOCOLORS` / `#VIDEODLY` / `#MOVIE` / `#SEEKxx` — video                                      |
| **Parser**    | `#DEFEXRANK` — fine-grained judgment width multiplier (overrides `#RANK`)                                                     |
| **Parser**    | `#CHARSET` — character encoding specification                                                                                 |
| **Parser**    | `#ExtChr` — BM98 extended character sprite display                                                                            |
| **Parser**    | Channel `17` / `27` — free-zone keys                                                                                          |
| **Parser**    | Channel `31`–`49` — invisible notes                                                                                           |
| **Parser**    | Channel `97` — dynamic BGM volume                                                                                             | 1.5      |
| **Parser**    | Channel `98` — dynamic KEY volume (counterpart to channel 97)                                                                 | 1.5      | 
| **Parser**    | Channel `A6` / `#CHANGEOPTIONxx` — dynamic option changes                                                                     |
| **Renderer**  | POOR BGA duration hardcoded 500ms (beatoraja uses config-driven `misslayerDuration`)                                          | 3        |
| **Replay**    | Replay not available                                                                                                          | 2        |
| **Scoring**   | Results screen — EX score, DJ LEVEL, clear type, gauge end % are not shown                                                    | 2        |
| **Scoring**   | ExRank support                                                                                                                | 3        |
| **Scoring**   | Different judgement text colours on results screen                                                                            | 4        |
| **Scoring**   | 24KEYS / 24KEYS DOUBLE judgement profile matching beatoraja `KEYBOARD`                                                        | 3        |
| **Scoring**   | `#DEFEXRANK`, `#EXRANK`, and judge-window-rate support                                                                        | 3        |
| **Scoring**   | Course constraints that alter judgement windows, including NO_GOOD/NO_GREAT                                                   | 4        |
| **Scoring**   | beatoraja non-default judge algorithms: Duration, Lowest, Score                                                               | 4        |
| **Skin**      | Column start position — value or enum (leftN, rightN, center)                                                                 | 3        |
| **Skin**      | Non-legacy BMS skin — fully configurable via skin editor                                                                      |
| **Skin**      | `HitGreat` → `HitGreatLate` / `HitGreatEarly` split images                                                                    |
| **Skin**      | E-POOR judgement image                                                                                                        | 3        |
| **UI**        | Lane cover / skin / movement                                                                                                  | 2        |
| **Perf**      | fps is not stable when a large amount of mine disposed                                                                        | 4        |

### FIXME

```text
2026-06-01 15:13:14 [error]: osu.Game.Rulesets.UI.BeatmapInvalidForRulesetException:
  Beatmap can not be converted for the ruleset
  (ruleset: osu.Game.Rulesets.Mania.ManiaRuleset, osu.Game.Rulesets.Mania,
   converter: osu.Game.Rulesets.Mania.Beatmaps.ManiaBeatmapConverter).
  at osu.Game.Beatmaps.WorkingBeatmap.GetPlayableBeatmap(...)
  at osu.Game.Screens.Select.BeatmapTitleWedge.DifficultyDisplay.<>c__DisplayClass36_0
       .<updateCountStatistics>b__0()
```

Switching the active ruleset from BMS to any other crashes with
`BeatmapInvalidForRulesetException` because the beatmap title wedge tries to recalculate
difficulty using the wrong converter while the carousel selection is stale.

</details>

---

## Appendix: Reference Links

| Subject                            | Link                                                                                                                                           |
|------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------|
| Star-Rating-Rebirth (SR algorithm) | https://github.com/sunnyxxy/Star-Rating-Rebirth                                                                                                |
| BMS Command Specification          | https://hitkey.nekokan.dyndns.info/cmds.htm                                                                                                    |
| 62-Base BMS Format Specification   | [Google Docs](https://docs.google.com/document/d/e/2PACX-1vTl8zOS3ukl5HpuNsBUlN8rn_ZaNdJSHb8a4se3Z3ap9Y6UJ1nB8LA3HnxWAk9kMTDp0j9orpg43-tl/pub) |
| beatoraja Extension Manual         | https://raw.githubusercontent.com/exch-bms2/beatoraja/master/manual/extension.txt                                                              |
| LR2 BMS Option Reference           | http://hitkey.nekokan.dyndns.info/option.htm                                                                                                   |
| BMS Gimmick Techniques (JP)        | https://note.com/numuther/n/n57bf895e7969                                                                                                      |
| Benchmark                          | https://hitkey.nekokan.dyndns.info/bmsbench.shtml                                                                                              |
| `#SPEED` vs `#SCROLL`              | https://hitkey.nekokan.dyndns.info/bmse_help_full/Capture/_read.htm#SAMPLEBMS                                                                  |
