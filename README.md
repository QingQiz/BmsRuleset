# osu! BMS Ruleset

Native osu! ruleset plugin for BMS-family charts (`.bms`, `.bme`, `.bml`, `.pms`).

---

[中文](./README.zh-CN.md)

## Installation

1. Build the ruleset:
   ```
   dotnet build "osu.Game.Rulesets.BmsRuleset"
   ```
   Output: `osu.Game.Rulesets.BmsRuleset/bin/Debug/net8.0/osu.Game.Rulesets.BmsRuleset.dll`

2. Copy the DLL to your osu! `rulesets/` folder.

3. Restart osu!. The ruleset will appear in the ruleset selector (currently uses the osu!mania icon as a placeholder).

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

## Implemented BMS Features

### Parser

**Header fields decoded:**

| Field                   | Command                                                                                                           | Description                                                               |
|-------------------------|-------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------|
| Title, Artist, Subtitle | `#TITLE`, `#ARTIST`, `#SUBTITLE`                                                                                  | Subtitle appended to title as `"Title - Subtitle"`                        |
| SubArtist               | `#SUBARTIST`                                                                                                      | Appended to artist as `"Artist (SubArtist)"`                              |
| Maker                   | `#MAKER`                                                                                                          | Mapped to beatmap Creator                                                 |
| Genre                   | `#GENRE` (also `#GENLE`)                                                                                          | Song style, stored in Tags                                                |
| URL, Email              | `%URL`, `%EMAIL`                                                                                                  | Stored in Tags                                                            |
| Comment                 | `#COMMENT`                                                                                                        | Stored in Tags                                                            |
| Difficulty              | `#DIFFICULTY`                                                                                                     | Numeric classification (not yet mapped)                                   |
| Play level              | `#PLAYLEVEL` (displayed as difficulty name)                                                                       |                                                                           |
| Judge rank              | `#RANK` (0–4, affects hit windows)                                                                                | Mapped to `OD`                                                            |
| Gauge total             | `#TOTAL`                                                                                                          | Mapped to `AR`                                                            |
| Base BPM (visual)       | `#BASEBPM`                                                                                                        | Overrides scroll speed reference BPM without affecting note timing        |
| Initial BPM             | `#BPM`                                                                                                            |                                                                           |
| Extended BPM table      | `#BPMxx`                                                                                                          |                                                                           |
| STOP table              | `#STOPxx`                                                                                                         |                                                                           |
| Sample definitions      | `#WAVxx`                                                                                                          |                                                                           |
| Long-note type          | `#LNTYPE 1` / `#LNTYPE 2` / `#LNOBJ`                                                                              |                                                                           |
| Player mode             | `#PLAYER`                                                                                                         | **ignored** — layout is inferred from channel presence and extension only |
| Measure length          | channel `02`                                                                                                      |                                                                           |
| Text events             | `#TEXTxx`, `#SONGxx`, channel `99`                                                                                |                                                                           |
| Random / Switch         | `#IF`, `#ELSEIF`, `#ELSE`, `#ENDIF` / `#END` / `#IFEND` / `#END IF` (typo tolerance), `#SWITCH`, related commands | gameplay supported                                                        |
| Base 62 extension       | `#BASE 62`                                                                                                        | Allow the case sensitive key present in command or channel                |

**Channels parsed:**

| Channel     | Meaning                                 |
|-------------|-----------------------------------------|
| `01`        | BGM autoplay samples                    |
| `02`        | Measure length (time signature changes) |
| `03`        | Inline hex BPM change                   |
| `08`        | Extended BPM change (`#BPMxx` lookup)   |
| `09`        | STOP event (`#STOPxx` lookup)           |
| `99`        | TEXT event (`#TEXTxx`/`#SONGxx` lookup) |
| `1x` / `2x` | Playable notes — P1 / P2                |
| `5x` / `6x` | Long note channels — P1 / P2            |
| `Dx` / `Ex` | Landmine channels — P1 / P2             |

**Not parsed / not functional:** `#EXRANK`, `#STAGEFILE`, `#BANNER`, BGA/image channels (`04`, `06`,
`07`, …).

---

### Layout and Lanes

Layout is inferred from channel presence and file extension (`.pms` → PMS variants; P2 channels present → Double Play;
channels `18`/`19` present → 7K). `#PLAYER` is ignored.

---

### Note Types

| Type            | Behaviour                                                                                                                       |
|-----------------|---------------------------------------------------------------------------------------------------------------------------------|
| **Normal note** | Press the key as it reaches the hit line                                                                                        |
| **Long note**   | Press and hold until the tail passes the hit line; dropping early results in POOR                                               |
| **Landmine**    | Do **not** press — holding the column key when it crosses the hit line drains the gauge and plays the `#WAV00` explosion sample |

---

### Input and Key Bindings

Default bindings (all rebindable in **Settings → Key Bindings → osu!BMS**):

**ingame controls:** increase/decrease scroll speed temporarily

Key sound of the next upcoming note in a column plays on every key press regardless of judgement result.

---

### Judgements and Scoring

**Judgement tiers (LR2 timing windows, from `#RANK`):**

| Name       | EX pts | Combo        | RANK 2 (Normal) window            |
|------------|--------|--------------|-----------------------------------|
| **PGREAT** | 2      | kept         | ±18 ms                            |
| **GREAT**  | 1      | kept         | ±40 ms                            |
| **GOOD**   | 0      | kept         | ±100 ms                           |
| **BAD**    | 0      | reset        | ±200 ms                           |
| **POOR**   | 0      | reset        | < -200ms / > +200ms               |
| **E-POOR** | 0      | **no break** | [-1000ms,-200ms], no note consume |

`#RANK` 0 = Very Hard (±8/24/40/200 ms) → 4 = Very Easy (±21/60/200/200 ms).

**Score:** `total EX score / max EX score × 1,000,000`

**DJ LEVEL rank:** X (all PGREAT) · S ≥ 8/9 · A ≥ 7/9 · B ≥ 6/9 · C ≥ 5/9 · D otherwise

---

### Gauge (Normal Gauge only)

- Starts at **20%**. No passive drain.
- **Clear condition:** finish the chart at ≥ 80%.
- `#TOTAL` controls the maximum gain rate. Default formula: `max(7.605 × N / (0.01 × N + 6.5), 160)` (LR2 formula, where
  N = total playable notes).
- Gain/loss per judgement: PGREAT/GREAT +`TOTAL/100/N` · GOOD +half · BAD −4% · POOR −6% · E-POOR −2%.
- Landmine damage: base-36 value ÷ 2 percent (e.g., `ZZ` = 647.5% → instant wipe).

Easy / Hard / Ex-Hard / Hazard gauge variants are not yet implemented.

---

### Audio

- **BGM channel 01:** played in sync with the chart clock; handles seek, pause/resume.
- **Key sounds (`#WAVxx`):** played on every key press from the note's declared sample. `.wav` declarations
  automatically resolve to `.ogg` siblings if present.
- **Background samples and key sounds are not affected by the osu! Effect volume slider** — they follow master and music
  volume only.

---

### Mods

| Mod                        | Description                            |                 |
|----------------------------|----------------------------------------|-----------------|
| Autoplay                   | auto play                              |                 |
| Double Time / Half Time    |                                        | Not Tested      |
| No Fail                    |                                        |                 |
| Cinema                     |                                        | Working         |
| Random / gauge mods        |                                        | Not implemented |
| Mirror                     |                                        |                 |
| 2P                         | change the player layout from 1P to 2P |                 |
| Auto Scratch /Hide Scratch | auto play/hide scratch lane            |                 |

---

### Settings

| Setting              | Default | Range    | Description                                                                                                                |
|----------------------|---------|----------|----------------------------------------------------------------------------------------------------------------------------|
| Scroll Speed         | 8.0     | 1.0–60.0 | Note fall speed. In-game `Up`/`Down` keys adjust temporarily. Keys are rebindable under Settings → Key Bindings → osu!BMS. |
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

> ⚠ **Important:** Do **not** add or remove difficulty tables while on the **song select** screen.
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

| Key             | Description                                 | Example |
|-----------------|---------------------------------------------|---------|
| `Layout`        | Layout identifier (required)                | `7K`    |
| `HitPosition`   | Hit target Y from bottom (480-height space) | `440`   |
| `LightPosition` | Column key-light Y                          | `440`   |
| `ScorePosition` | Judgement popup Y                           | `250`   |
| `ComboPosition` | Combo counter Y from top                    | `300`   |
| `JudgementLine` | Show white line at hit position (`1`/`0`)   | `1`     |

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

| Key                         | Description                    |
|-----------------------------|--------------------------------|
| `StageHint`                 | Hit target image               |
| `StageLeft` / `StageRight`  | Left/right stage border images |
| `StageBottom`               | Bottom stage foreground image  |
| `StageLight` / `LightImage` | Column light/glow image        |
| `LightingN`                 | Normal hit explosion image     |
| `LightingL`                 | LN hit explosion image         |
| `LightFramePerSecond`       | Column light animation FPS     |

### HUD Components

The combo counter (`BmsComboCounter`) and health display (`BmsHealthDisplay`) implement
`ISerialisableDrawable` and can be repositioned in the **Skin Editor** during gameplay.
Open the skin editor and drag the combo counter or health bar to
your preferred position.

On the next play session the saved layout is automatically loaded.

**Judgement images:**

| Key         | BMS judgement |
|-------------|---------------|
| `HitPGreat` | PGREAT        |
| `HitGreat`  | GREAT         |
| `HitGood`   | GOOD          |
| `HitBad`    | BAD           |
| `HitPoor`   | POOR / E-POOR |

#### osu!mania Skin Compatibility

You can also use `[Mania]` sections from a standard osu!mania skin. The ruleset will match on:

- `[Mania]` with `SpecialStyle: 1` and `Keys: 6` → 5K layout
- `[Mania]` with `SpecialStyle: 1` and `Keys: 8` → 7K layout
- `[Mania]` with the exact key count

In `[Mania]` sections, use mania standard judgement names: `Hit300g` (PGREAT), `Hit300` (GREAT), `Hit200` (GOOD),
`Hit50` (BAD), `Hit0` (POOR).

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
LightingN : lightingN
LightingL : lightingL

HitPGreat : j-pgreat
HitGreat : j-great
HitGood : j-good
HitBad : j-bad
HitPoor : j-poor
```

The full built-in skin (covering all 6 layouts) is at
`osu.Game.Rulesets.BmsRuleset/Resources/Skins/Modern/skin.ini` — use it as a reference.

---

## Not Yet Implemented

| Area          | What is missing                                                                             | Priority  |
|---------------|---------------------------------------------------------------------------------------------|-----------|
| **Audio**     | `#WAVCMD` (MacBeat) — pitch/volume/playback-time per WAV slot                               |
| **Audio**     | `#EXWAVxx` (nanasi) — pan/volume/frequency per WAV file                                     |
| **Audio**     | `#VOLWAV` (BM98) — global volume scalar                                                     |
| **Audio**     | Hijack preview song to play BMS samples                                                     | 1(failed) |
| **Audio**     | `#xxx97` (fgt) — dynamic BGM volume change channel                                          |           |
| **Converter** | Mania 7K → BMS chart conversion                                                             | 3         |
| **Gauge**     | Easy / Hard / Ex-Hard / Hazard gauge variants                                               | 2         |
| **Gauge**     | LN-specific gauge events (head miss ≠ body drop ≠ tail miss)                                | 1         |
| **Import**    | Resource files in subdirectories — only the filename is used, relative paths are unresolved | 1         |
| **Input**     | Scratch turntable semantics — scratch is routed as a plain column key                       |
| **Input**     | Judgement offset adjustment capability                                                      |
| **Mods**      | Different health bar types                                                                  | 2         |
| **Mods**      | Random / S-Random / H-Random column shuffle                                                 | 2         |
| **Mods**      | Gauge-selection mods                                                                        | 2         |
| **Mods**      | assist options                                                                              |
| **Mods**      | Remember last used mod combination                                                          |
| **Mods**      | BG: make key sounds → background samples (hit results don't affect music)                   | 2         
| **Parser**    | `#BGAxx` / `#POORBGA` / `#SWBGAxx` / `#@BGAxx` / `#ARGBxx` — BGA definitions                |
| **Parser**    | `#BMPxx` / `#EXBMPxx` — image definitions (non-resource-scan)                               |
| **Parser**    | `#CDDA` / `#MIDIFILE` — CD / MIDI                                                           |
| **Parser**    | `#CHARFILE` / `#ExtChr` — character / skin                                                  |
| **Parser**    | `#DEFEXRANK` — extended rank definition                                                     |
| **Parser**    | `#EXBPMxx` — `#BPMxx` alias (BMSC parser bug workaround)                                    |
| **Parser**    | `#EXRANK` / channel `A0` — extended rank definition                                         |
| **Parser**    | `#EXWAVxx`, `#WAVCMD`, `#VOLWAV` — advanced audio controls                                  |
| **Parser**    | `#MATERIALS` / `#MATERIALSWAV` / `#MATERIALSBMP` / `#DIVIDEPROP` — resource groups          |
| **Parser**    | `#OCT/FP` — octave / pedal                                                                  |
| **Parser**    | `#OPTION` — forced option                                                                   |
| **Parser**    | `#PATH_WAV` / `#PATH_BMP` — resource path prefixes                                          |
| **Parser**    | `#STAGEFILE`, `#BANNER`, `#BACKBMP`, `#MOVIE` — metadata only                               |
| **Parser**    | `#STP` — absolute STOP sequence                                                             |
| **Parser**    | `#VIDEOFILE` / `#VIDEOf/s` / `#VIDEOCOLORS` / `#VIDEODLY` / `#MOVIE` / `#SEEKxx` — video    |
| **Parser**    | Channel `04`/`06`/`07`/`0A`–`0E` — BGA layers                                               |
| **Parser**    | Channel `17` / `27` — free-zone keys                                                        |
| **Parser**    | Channel `31`–`49` — invisible notes                                                         |
| **Parser**    | Channel `97` — dynamic BGM volume                                                           |
| **Parser**    | Channel `98` — dynamic KEY volume (counterpart to channel 97)                               |
| **Parser**    | Channel `A6` / `#CHANGEOPTIONxx` — dynamic option changes                                   |
| **Renderer**  | BGA / movie / stagefile / background image                                                  |
| **Renderer**  | Key beams (column light during hold)                                                        | 2         |
| **Renderer**  | BGA                                                                                         |
| **Replay**    | Replay not available                                                                        | 2         |
| **Scoring**   | Results screen — EX score, DJ LEVEL, clear type, gauge end % are not shown                  | 2         |
| **Scoring**   | ExRank support                                                                              | 3         |
| **Scoring**   | Different judgement text colours on results screen                                          | 4         |
| **Scoring**   | Beatmap statistics — show more info (e.g., random branch count)                             | 2         |
| **Scoring**   | LN head judgement                                                                           | 1         |
| **Skin**      | Column start position — value or enum (leftN, rightN, center)                               | 3         |
| **Skin**      | BGA position/size configuration                                                             |
| **Skin**      | Non-legacy BMS skin — fully configurable via skin editor                                    |
| **Skin**      | `HitGreat` → `HitGreatLate` / `HitGreatEarly` split images                                  |
| **Skin**      | E-POOR judgement image                                                                      | 3         |
| **UI**        | Lane cover / skin / movement                                                                | 2         |
| **UI**        | Rewrite health bar — red/yellow/green gradient, no border, no overall colour change         | 2         |
| **Perf**      | parser performance                                                                          | 3         |
| **Perf**      | high GC pressure during importing (sr) (consider pre compute and query)                     | 1         |

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

Switching the active ruleset from BMS to any other (or from any other to BMS) crashes with
`BeatmapInvalidForRulesetException` because the beatmap title wedge tries to recalculate
difficulty using the wrong converter while the carousel selection is stale.

---

## Appendix: Reference Links

| Subject                            | Link                                              |
|------------------------------------|---------------------------------------------------|
| Star-Rating-Rebirth (SR algorithm) | <https://github.com/sunnyxxy/Star-Rating-Rebirth> |
| BMS Command Specification          | <https://hitkey.nekokan.dyndns.info/cmds.htm>     
