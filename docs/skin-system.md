# Skin System

[Back to README](../README.md) | [中文](./skin-system.zh-CN.md)

## Configuration Priority

For BMS 7K (seven keys plus scratch), skin configuration is selected in this order:

1. `[BMS]` with `Layout: 7K`
2. `[Mania]` with `Keys: 8` and `SpecialStyle: 1`
3. `[Mania]` with `Keys: 8`
4. `[Mania]` with `Keys: 7`
5. The ruleset's built-in fallback skin

## Creating a Skin

Place your images and a `skin.ini` in a folder, then import it as a normal osu! skin.

### skin.ini — `[BMS]` Section

Write one `[BMS]` section per layout you want to support. The `Layout:` key is required.

**Layout values:** `5K`, `7K`, `9K`, `10K`, `14K`, `18K`
(Aliases: `BMS5K`, `BME7K`, `PMS9K`, `BMS5KDouble`, `BME7KDouble`, `PMS9KDouble`)

### All Supported Keys

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
>
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

The combo counter uses `{ComboPrefix}-0.png` through `{ComboPrefix}-9.png` and stays hidden if the textures are missing.

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

### Frame Animations (N-suffix)

To provide a multi-frame animation, append `-0`, `-1`, `-2`, … to the texture name:

```
lightingN-0.png
lightingN-1.png
lightingN-2.png
```

`StageLight` uses the frame rate in `LightFramePerSecond`; `LightingN`/`LightingL` derive frame duration from the
frame count. A single file with no suffix, such as `lightingN.png`, displays as a static sprite.

This works for any image key: `NoteImage`, `KeyImage`/`KeyImageD`, `StageLight`, `LightingN`,
`LightingL`, `StageHint`, judgement images, etc.

### Missing image resources

Default image names apply only to omitted configuration keys. For a configured name, the ruleset searches the active
skin hierarchy for that exact resource. If it is missing everywhere, no image is drawn; the default name is not tried.

For example, an omitted `KeyImage1` uses `mania-key1` and may retrieve it from a fallback skin.
`KeyImage1: custom-key` with no `custom-key` resource renders an empty up-state instead of retrying
`mania-key1`.

Long-note tails fall back to heads, then normal notes; heads fall back to normal notes, as in osu!mania.
If the current skin cannot supply a judgement component, a lower-priority skin may supply the whole component.

### osu!mania Skin Compatibility

`[Mania]` sections are matched in this order:

- `[Mania]` with `SpecialStyle: 1` and scratch-inclusive keys (`6`, `8`, `12`, `16`)
- `[Mania]` with scratch-inclusive keys (`6`, `8`, `12`, `16`), before exact key-only fallback
- `[Mania]` with the exact key count

In `[Mania]` sections, use mania standard judgement names: `Hit300g` (PGREAT), `Hit300` (GREAT), `Hit200` (GOOD),
`Hit50` (BAD), `Hit0` (POOR).

## Skin Components

Open the **Skin Editor** during gameplay to add, remove, move, and resize components. Select a component to edit its
settings in the sidebar. Positions, sizes, and settings are saved in the active skin's BMS layout and restored when
that skin is used again. Chart files and `skin.ini` are unchanged.

Moving and resizing affect appearance only. Special resize rules are described below.

### Combo

Displays the combo using `ComboPrefix` digit textures (`score` by default). Digits animate on each increment and flash
with `ColourBreak` on a combo break. Missing digit textures hide the counter.

- **Auto-hide delay**: seconds before hiding after the last combo increment; each increment restarts the timer.
  Range: -1 to 100; default: 3; -1 disables automatic hiding.
- **Min visible combo**: minimum combo to display. Combos below it hide immediately. Range: 0 to 100; default: 10.

### Judgement

Displays the latest PGREAT, GREAT, GOOD, BAD, or POOR/E-POOR, replacing the previous result and restarting its
animation. Uses `[BMS]` images `HitPGreat` through `HitPoor`, or their osu!mania equivalents listed above.

- **Show E-POOR** toggles whether empty POOR judgements appear in the popup. It is enabled by default.

Edit the skin image files to change the artwork or animation.

### Hit Error Meter

Shows timing errors with Fast on the left and Slow on the right. The white 0 ms marker stays centred. Both sides
have equal space; asymmetric windows leave unused space on the shorter side.

Judgement windows form a continuous colour bar. E-POOR adds a grey segment beyond Fast BAD without shortening other
segments. POOR has no finite late boundary, so it appears at the Slow end.

- **Judgement line thickness** controls the width of each displayed timing line from 1 to 8 (default 4).
- **Judgement fade duration** controls how many seconds a timing line takes to fade out, from 0.1 to 20 seconds
  (default 5 seconds).
- **Show colour bars** toggles the judgement-window bar.
- **Show moving average** toggles the average-position chevron. POOR and E-POOR never affect this average.
- **Show E-POOR** toggles both E-POOR timing lines and the additional grey Fast-side segment. It is enabled by default.
- **Show POOR** toggles POOR timing lines at the final Slow position. It is enabled by default.
- **Centre marker style** selects a circle, line, or no marker at 0 ms. Both visible styles use white.
- **Label style** selects Fast/Slow icons, text labels, or no labels.

Horizontal resizing changes the length of the timing axis. Vertical resizing changes the span of the judgement lines;
the two directions can be adjusted independently.

### Score Graph

Compares the current EX score with two references during play:

- **Personal best**: the highest saved EX score with the selected mods or harder mods. Replay judgement data, when
  available, reconstructs its score progression.
- **Target**: the minimum EX score for the rank above the personal best (C, B, A, AA, AAA, then S). At S, the target stays S.

The graph shows rank thresholds, three live score bars, differences from personal best and target, and judgement
counts from PGREAT to E-POOR. Personal-best judgement counts show as unavailable without replay judgement data.

- **Current score colour** changes the live EX-score bar and current-score accents.
- **Personal best colour** changes the personal-best bar, its final-score ghost, and personal-best accents.
- **Target colour** changes the target bar, its final-score ghost, and target accents.
- **Show score bars**, **Show score differences**, and **Show judgement comparison** toggle each section.
  At least one must stay visible.

The component enforces a minimum width and enough height for the visible sections.

<details>
<summary>Example</summary>

![](https://github.com/user-attachments/assets/ab14a6e8-e853-42ed-ae08-61cc4427e667)

</details>

### Health Bar

Fills from bottom to top. Assist Easy, Easy, and Normal show a clear line and change colour at the red-zone and clear
thresholds. The red zone ends at 20%; Assist Easy clears at 60%, Easy and Normal at 80%.

- **Groove low health colour** is used below the red-zone threshold.
- **Groove mid health colour** is used from the red-zone threshold up to the clear threshold.
- **Groove high health colour** is used at or above the clear threshold.
- **Hard**, **ExHard**, and **Hazard gauge fill colour** each set the single fill colour used by that survival gauge.

Class, ExClass, and ExHard Class use the Hard, ExHard, and Hazard fill colours respectively.

### Song Progress

Shows playback progress with a glowing marker: it stays at the top during the intro, then moves downward during play.
The track sits at the Stage's left edge by default; its height determines the marker's travel distance.

**Indicator colour** changes the marker and its glow.

---

### BGA

Displays the chart's base, layer 1, layer 2, and POOR BGA layers, including crop and opacity events. Misses briefly
show the POOR layer according to the chart's POOR BGA mode.

The BGA scales to fit the component's rectangle while preserving its aspect ratio. It stays behind the playfield
and remains visible when the gameplay HUD is hidden.

- **Fill screen** expands the component across the full HUD area and is enabled in the default layout. Moving, resizing,
  or rotating the component automatically disables this option and preserves the edited BGA window.

**BGA dim** in the BMS settings applies to every component layout.

### Stage

Contains the lanes, notes, measure lines, key area, judgement line, hit effects, and Stage artwork. They move together.

- **Judgement line offset** moves the line from its skin-defined position: positive moves up, negative moves down.
  The editor keeps it within the visible Stage. Judgement timing is unchanged.
- **Note height scale** scales the height of note heads and tails from 0.01x to 5x (default 1x). It does not move notes,
  alter long-note body length, or scale other Stage elements.

Stage resize handles have three functions:

- **Horizontal resize** (left/right handles) changes the Stage width, stretching lanes, notes, and Stage graphics
  horizontally without changing the visible lane length.
- **Vertical resize** (top/bottom handles) changes the visible lane length without vertically scaling notes or other
  Stage content.
- **Diagonal resize** (corner handles) scales the entire Stage uniformly, including lane width, note size, and Stage
  graphics, while preserving its proportions and the amount of lane content shown.

Stage is required and absent from the component toolbox. Deleting it creates a default instance, resetting position,
size, and settings, including judgement line offset. If a saved layout has duplicates, only the first is kept.

<details>
<summary>Example</summary>

https://github.com/user-attachments/assets/7d88d698-1e06-4488-9b45-c9aa462adb64

</details>

### Text

Shows a dark-backed banner for `Game Start`, channel `99` messages from `#TEXTxx`, and `#TEXT00` on POOR when defined.
Scroll-speed changes show the new multiplier with `>>` or `<<`. Messages fade after a short delay.

This component has no sidebar settings. The editor shows `Sample Text Event` for positioning and scaling because
the banner is transparent between messages. The placeholder does not appear during gameplay.

## Example skin.ini (7K)

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
