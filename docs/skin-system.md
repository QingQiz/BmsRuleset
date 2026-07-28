# Skin System

[Back to README](../README.md) | [中文](./skin-system.zh-CN.md)

The skin configuration selection order is described in [osu!mania Skin Support](../README.md#osumania-skin-support).

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

### Frame Animations (N-suffix)

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

### osu!mania Skin Compatibility

You can also use `[Mania]` sections from a standard osu!mania skin. The ruleset will match on:

- `[Mania]` with `SpecialStyle: 1` and scratch-inclusive keys (`6`, `8`, `12`, `16`)
- `[Mania]` with scratch-inclusive keys (`6`, `8`, `12`, `16`), before exact key-only fallback
- `[Mania]` with the exact key count

In `[Mania]` sections, use mania standard judgement names: `Hit300g` (PGREAT), `Hit300` (GREAT), `Hit200` (GOOD),
`Hit50` (BAD), `Hit0` (POOR).

## Skin Components

Open the **Skin Editor** during gameplay to add, remove, reposition, and resize components. Selecting a component also
opens its component-specific settings in the sidebar. Saving the editor layout stores both the transforms and these
settings in the active skin's BMS-specific gameplay layout. The layout is loaded again whenever that skin is used for
BMS; chart files and `skin.ini` are not modified.

### Combo

Displays the current combo using the skin's combo digit textures (`ComboPrefix`, or `score` by default). The number
animates on each increment and flashes with `ColourBreak` when the combo is broken. If the required digit textures are
missing, the counter has nothing to draw and remains hidden.

- **Auto-hide delay** controls how many seconds the counter remains visible after the combo stops increasing. Each
  increment restarts the timer. The allowed range is -1 to 100 seconds, the default is 3 seconds, and -1 disables
  automatic hiding.
- **Min visible combo** controls the first combo value at which the counter appears. Values below this threshold are
  hidden immediately; the allowed range is 0 to 100 and the default is 10.

Moving or scaling the component changes where and how large the digits are drawn; it does not change either threshold.

### Judgement

Displays the result of the most recently judged note. A new PGREAT, GREAT, GOOD, BAD, or POOR/E-POOR immediately
replaces the previous result and restarts that judgement image's animation. Images come from the `HitPGreat` through
`HitPoor` entries in a `[BMS]` skin, or the corresponding mania judgement images described above.

There are no component-specific sidebar settings. Use the editor controls to set the popup's position and scale, and
use the skin image files to change its artwork or animation.

### Hit Error Meter

Shows timing errors on a horizontal axis with Fast on the left and Slow on the right. The white 0 ms marker remains
at the visual centre. Both sides reserve the same amount of space, so asymmetric BMS timing windows leave unused space
on the shorter side instead of shifting the centre.

The coloured judgement windows form one continuous bar. E-POOR adds a grey segment beyond the Fast BAD window without
shortening the other sections. POOR has no finite late edge and is therefore drawn at the final Slow position.

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

Tracks the current EX score against two references throughout the chart:

- **Personal best** is the saved play with the highest EX score among scores achieved with the currently selected mods
  or with more difficult mods. Its live progression is reconstructed from the saved play when replay judgement data is
  available.
- **Target** is the minimum EX score for the rank immediately above the personal best (C, B, A, S, then X). Once the
  personal best is X, X remains the target.

The graph can show rank threshold lines, three live score bars, the current difference from the personal best and
target, and a PGREAT-through-E-POOR judgement-count comparison. The personal-best judgement column shows an unavailable
marker when the saved score does not contain the required replay judgement data.

- **Current score colour** changes the live EX-score bar and current-score accents.
- **Personal best colour** changes the personal-best bar, its final-score ghost, and personal-best accents.
- **Target colour** changes the target bar, its final-score ghost, and target accents.
- **Show score bars**, **Show score differences**, and **Show judgement comparison** independently control the three
  sections. At least one section must remain enabled, so disabling the last visible section is rejected.

The component enforces a minimum width and enough height for the enabled sections. Resizing it beyond those limits gives
the score plot more room without changing any score calculations.

<details>
<summary>Example</summary>

![](https://github.com/user-attachments/assets/ab14a6e8-e853-42ed-ae08-61cc4427e667)

</details>

### Health Bar

Shows the currently selected BMS gauge as a bottom-to-top fill. For Assist Easy, Easy, and Normal, a clear line marks
the gauge's clear threshold and the fill changes colour as it passes the red-zone and clear thresholds. The thresholds
come from the selected gauge rules: the red zone ends at 20%, Assist Easy clears at 60%, and Easy and Normal clear at
80%.

- **Groove low health colour** is used below the red-zone threshold.
- **Groove mid health colour** is used from the red-zone threshold up to the clear threshold.
- **Groove high health colour** is used at or above the clear threshold.
- **Hard**, **ExHard**, and **Hazard gauge fill colour** each set the single fill colour used by that survival gauge.

The Class gauge variants use their own fixed profile colours. Resizing the component changes the gauge's visible width
and height only; it does not change health values, thresholds, or gauge behaviour.

### Song Progress

Shows playback position along a vertical track: the glowing marker starts at the top, remains there during the intro,
and travels towards the bottom as the playable portion of the chart advances.

**Indicator colour** changes both the sharp marker and its surrounding glow. Moving the component places the progress
track elsewhere; changing its height changes the marker's travel distance.

The default layout attaches it to the left edge of the Stage.

---

### BGA

Displays the chart-authored BGA timeline, including the base, layer 1, layer 2, and POOR layers. The component applies
chart-defined crop and opacity events and shows the POOR layer briefly after a miss according to the chart's POOR BGA
mode.

The component's rectangle defines the BGA viewport. Content always preserves its aspect ratio with aspect-fit sizing,
scaling up or down as needed to fit inside the viewport. It is rendered behind the playfield and remains there when
the gameplay HUD is hidden.

- **Fill screen** expands the component across the full HUD area and is enabled in the default layout. Moving, resizing,
  or rotating the component automatically disables this option and preserves the edited BGA window.

**BGA dim** is a ruleset-wide gameplay setting rather than a component setting, and affects the BGA regardless of
which saved component layout is active.

### Stage

Represents the complete playable Stage: its lanes, notes, measure lines, key area, judgement line, hit explosions, and
Stage artwork all move together when this component is repositioned.

- **Judgement line offset** moves the judgement line relative to the position supplied by the skin. Positive values
  move it upward and negative values move it downward. The editor limits the value so the line remains within the
  current visible Stage; changing the offset does not change judgement timing.
- **Note height scale** scales the height of note heads and tails from 0.01x to 5x (default 1x). It does not move notes,
  alter long-note body length, or scale other Stage elements.

The Stage resize handles deliberately perform different operations:

- **Horizontal resize** (left/right handles) changes the Stage width, stretching lanes, notes, and Stage graphics
  horizontally without changing the visible lane length.
- **Vertical resize** (top/bottom handles) changes the visible lane length without vertically scaling notes or other
  Stage content.
- **Diagonal resize** (corner handles) scales the entire Stage uniformly, including lane width, note size, and Stage
  graphics, while preserving its proportions and the amount of lane content shown.

The Stage skin component is required and is not offered in the component toolbox. Deleting it automatically creates
a new default instance, resetting its position, size, and settings, including the judgement line offset. If a saved
layout contains duplicates, only the first instance is retained.

<details>
<summary>Example</summary>

https://github.com/user-attachments/assets/7d88d698-1e06-4488-9b45-c9aa462adb64

</details>

### Text

Shows short gameplay messages in a dark-backed text banner. At chart start it briefly displays `Game Start`. During
play, channel `99` events display the corresponding `#TEXTxx` value; if `#TEXT00` is defined, a POOR judgement displays
it as the mistake message. An in-game scroll-speed change displays the new multiplier with `>>` or `<<` direction
markers. Each message fades out automatically after a short delay.

There are no component-specific sidebar settings. Because the component is normally transparent between messages, the
skin editor replaces its contents with a fully visible `Sample Text Event` placeholder. Use that placeholder to place
and scale the banner; it is never shown during normal gameplay.

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
