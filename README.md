
# osu! BMS Ruleset

Native osu! ruleset plugin for BMS-family charts (`.bms`, `.bme`, `.bml`, `.pms`).

---

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
3. Navigate to and select your BMS folder. Each chart file (`.bms`/`.bme`/`.bml`/`.pms`) in the folder becomes a separate beatmap; the whole folder becomes one beatmap set.
4. Sibling audio/image resources are automatically included.

To remove all imported BMS content, use the **"Delete all imported BMS files"** button in the same settings section.

---

## Implemented BMS Features

### Parser

**Header fields decoded:**

| Field | Command |
|---|---|
| Title, Artist, Genre | `#TITLE`, `#ARTIST`, `#GENRE` |
| Play level | `#PLAYLEVEL` (appended to difficulty name as display label) |
| Initial BPM | `#BPM` |
| Extended BPM table | `#BPMxx` |
| STOP table | `#STOPxx` |
| Sample definitions | `#WAVxx` |
| Long-note type | `#LNTYPE 1` / `#LNTYPE 2` / `#LNOBJ` |
| Judge rank | `#RANK` (0–4, affects hit windows) |
| Gauge total | `#TOTAL` |
| Player mode | `#PLAYER` | **ignored** — layout is inferred from channel presence and file extension only |
| Measure length | channel `02` |

**Channels parsed:**

| Channel | Meaning |
|---|---|
| `01` | BGM autoplay samples |
| `02` | Measure length (time signature changes) |
| `03` | Inline hex BPM change |
| `08` | Extended BPM change (`#BPMxx` lookup) |
| `09` | STOP event (`#STOPxx` lookup) |
| `1x` / `2x` | Playable notes — P1 / P2 |
| `5x` / `6x` | Long note channels — P1 / P2 |
| `Dx` / `Ex` | Landmine channels — P1 / P2 |

**Not parsed / not functional:** `#RANDOM` / `#IF` branching (only `#RANDOM 1` accidentally works), `#EXRANK`, `#SUBTITLE`, `#STAGEFILE`, `#BANNER`, BGA/image channels (`04`, `06`, `07`, …).

---

### Layout and Lanes

Layout is inferred from channel presence and file extension (`.pms` → PMS variants; P2 channels present → Double Play; channels `18`/`19` present → 7K). `#PLAYER` is ignored.

| Layout | Format | Total lanes | Scratch |
|---|---|---|---|
| 5K | `.bms` SP | 6 | column 0 |
| 7K | `.bme` SP | 8 | column 0 |
| 9K | `.pms` SP | 9 | none |
| 10K | `.bms` DP | 12 | columns 0 and 11 |
| 14K | `.bme` DP | 16 | columns 0 and 15 |
| 18K | `.pms` DP | 18 | none |

The scratch column is visually narrower and darker than regular key columns.

---

### Note Types

| Type | Behaviour |
|---|---|
| **Normal note** | Press the key as it reaches the hit line |
| **Long note** | Press and hold until the tail passes the hit line; dropping early results in POOR |
| **Landmine** | Do **not** press — holding the column key when it crosses the hit line drains the gauge and plays the `#WAV00` explosion sample |

---

### Input and Key Bindings

Default bindings (all rebindable in **Settings → Key Bindings → osu!BMS**):

**ingame controls:** `Up` / `Down` increase/decrease scroll speed temporarily

**5K SP:** `LShift` Scratch · `Z` `S` `X` `D` `C` Keys 1–5

**7K SP:** same as 5K plus `F` Key 6 · `V` Key 7

**9K PMS:** `A` `S` `D` `F` Keys 1–4 · `Space` Key 5 · `J` `K` `L` `;` Keys 6–9

**DP:** P1 uses the same keys as SP; P2 uses `RShift` scratch and Numpad 1–5 / 1–7 / 1–9 for keys.

Key sound of the next upcoming note in a column plays on every key press regardless of judgement result.

---

### Judgements and Scoring

**Judgement tiers (LR2 timing windows, from `#RANK`):**

| Name | EX pts | Combo | RANK 2 (Normal) window |
|---|---|---|---|
| **PGREAT** | 2 | kept | ±18 ms |
| **GREAT** | 1 | kept | ±40 ms |
| **GOOD** | 0 | kept | ±100 ms |
| **BAD** | 0 | reset | ±200 ms |
| **POOR** | 0 | reset | −200 to −1000 ms (early only); note consumed |
| **E-POOR** | 0 | **no break** | keypress with no note in range |

`#RANK` 0 = Very Hard (±8/24/40/200 ms) → 4 = Very Easy (±21/60/200/200 ms).

**Score:** `total EX score / max EX score × 1,000,000`

**DJ LEVEL rank:** X (all PGREAT) · S ≥ 8/9 · A ≥ 7/9 · B ≥ 6/9 · C ≥ 5/9 · D otherwise

---

### Gauge (Normal Gauge only)

- Starts at **20%**. No passive drain.
- **Clear condition:** finish the chart at ≥ 80%.
- `#TOTAL` controls the maximum gain rate. Default formula: `max(7.605 × N / (0.01 × N + 6.5), 160)` (LR2 formula, where N = total playable notes).
- Gain/loss per judgement: PGREAT/GREAT +`TOTAL/100/N` · GOOD +half · BAD −4% · POOR −6% · E-POOR −2%.
- Landmine damage: base-36 value ÷ 2 percent (e.g., `ZZ` = 647.5% → instant wipe).

Easy / Hard / Ex-Hard / Hazard gauge variants are not yet implemented.

---

### Audio

- **BGM channel 01:** played in sync with the chart clock; handles seek, pause/resume.
- **Key sounds (`#WAVxx`):** played on every key press from the note's declared sample. `.wav` declarations automatically resolve to `.ogg` siblings if present.
- **Background samples and key sounds are not affected by the osu! Effect volume slider** — they follow master and music volume only.
- LN tail samples are parsed and stored but not yet played during gameplay.

---

### Mods

| Mod | Status |
|---|---|
| Autoplay | Working |
| Double Time / Half Time | Working (rate change) |
| No Fail | Working |
| Cinema | Working |
| Random / Mirror / gauge mods | Not implemented |

---

### Settings

| Setting | Default | Range | Description |
|---|---|---|---|
| Scroll Speed | 8.0 | 1.0–60.0 | Note fall speed. In-game: `Up`/`Down` keys adjust temporarily. Keys are rebindable under Settings → Key Bindings → osu!BMS.

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

| Key | Description | Example |
|---|---|---|
| `Layout` | Layout identifier (required) | `7K` |
| `HitPosition` | Hit target Y from bottom (480-height space) | `440` |
| `LightPosition` | Column key-light Y | `440` |
| `ScorePosition` | Judgement popup Y | `250` |
| `JudgementLine` | Show white line at hit position (`1`/`0`) | `1` |

**Column geometry:**

| Key | Description | Example |
|---|---|---|
| `ColumnWidth` | Comma-separated widths for all N columns | `45,45,45,45,45,45,45,45` |
| `ColumnLineWidth` | Separator line widths — N+1 values | `0,1,1,1,1,1,1,1,0` |
| `ColumnSpacing` | Spacing between columns | `2,2,2,2,2,2,2` |
| `WidthForNoteHeightScale` | Reference width for note height scaling | `50` |
| `BarlineHeight` | Bar line height multiplier | `1.2` |

**Colors:**

| Key | Description | Example |
|---|---|---|
| `ColourColumnLine` | Column separator color (R,G,B,A) | `255,255,255,50` |
| `ColourJudgementLine` | Hit target line color | `255,255,255,255` |
| `ColourBarline` | Measure bar line color | `0,255,0,255` |
| `Colour1`–`ColourN` | Per-column background color | `0,0,0,0` |
| `ColourLight1`–`ColourLightN` | Per-column key-light glow color | `255,200,0` |
| `Colour` | All-column background shorthand | `0,0,0,0` |
| `ColourLight` | All-column light shorthand | `0,0,0` |

**Note images:**

| Key | Description |
|---|---|
| `NoteImage0`–`NoteImageN` | Per-column normal note image |
| `NoteImage0L`–`NoteImageNL` (or `NoteImageL`) | Per-column LN body image |
| `NoteImage0T`–`NoteImageNT` (or `NoteImageT`) | Per-column LN tail image |
| `NoteImage0H`–`NoteImageNH` | Per-column LN head (falls back to `NoteImage`) |
| `MineImage` / `MineImage0`–`MineImageN` | Landmine image |

**Key images:**

| Key | Description |
|---|---|
| `KeyImage0`–`KeyImageN` | Per-column key (not pressed) |
| `KeyImage0D`–`KeyImageND` | Per-column key (pressed) |

**Stage and effects:**

| Key | Description |
|---|---|
| `StageHint` | Hit target image |
| `StageLeft` / `StageRight` | Left/right stage border images |
| `StageBottom` | Bottom stage foreground image |
| `StageLight` / `LightImage` | Column light/glow image |
| `LightingN` | Normal hit explosion image |
| `LightingL` | LN hit explosion image |
| `LightFramePerSecond` | Column light animation FPS |

**Judgement images:**

| Key | BMS judgement |
|---|---|
| `HitPGreat` | PGREAT |
| `HitGreat` | GREAT |
| `HitGood` | GOOD |
| `HitBad` | BAD |
| `HitPoor` | POOR / E-POOR |

#### osu!mania Skin Compatibility

You can also use `[Mania]` sections from a standard osu!mania skin. The ruleset will match on:
- `[Mania]` with `SpecialStyle: 1` and `Keys: 6` → 5K layout
- `[Mania]` with `SpecialStyle: 1` and `Keys: 8` → 7K layout
- `[Mania]` with the exact key count

In `[Mania]` sections, use mania standard judgement names: `Hit300g` (PGREAT), `Hit300` (GREAT), `Hit200` (GOOD), `Hit50` (BAD), `Hit0` (POOR).

### Example skin.ini (7K)

```ini
[General]
Name: My BMS Skin
Author: yourname

[BMS]
Layout: 7K

HitPosition: 440
LightPosition: 440
ScorePosition: 250
JudgementLine: 1

LightFramePerSecond: 40
ColumnWidth: 45,45,45,45,45,45,45,45
WidthForNoteHeightScale: 50
BarlineHeight: 1.2

ColourBarline: 0,255,0,255
ColourColumnLine: 255,255,255,50
ColourJudgementLine: 255,255,255,255
ColumnLineWidth: 0,1,1,1,1,1,1,1,0

Colour: 0,0,0,0
ColourLight: 0,0,0

; BMS 7K column order: scratch even odd even center even odd even
NoteImage0: note-scratch
NoteImage1: note-even
NoteImage2: note-odd
NoteImage3: note-even
NoteImage4: note-center
NoteImage5: note-even
NoteImage6: note-odd
NoteImage7: note-even

NoteImageL: note-ln-body
NoteImageT: note-ln-tail
MineImage: note-mine

KeyImage0: mania-keyS
KeyImage0D: mania-keySD
KeyImage1: mania-key1
KeyImage1D: mania-key1D
KeyImage2: mania-key2
KeyImage2D: mania-key2D
KeyImage3: mania-key1
KeyImage3D: mania-key1D
KeyImage4: mania-key2
KeyImage4D: mania-key2D
KeyImage5: mania-key1
KeyImage5D: mania-key1D
KeyImage6: mania-key2
KeyImage6D: mania-key2D
KeyImage7: mania-key1
KeyImage7D: mania-key1D

StageHint: mania-stage-hint
LightingN: lightingN
LightingL: lightingL

HitPGreat: j-pgreat
HitGreat: j-great
HitGood: j-good
HitBad: j-bad
HitPoor: j-poor
```

The full built-in skin (covering all 6 layouts) is at
`osu.Game.Rulesets.BmsRuleset/Resources/Skins/Modern/skin.ini` — use it as a reference.

---

## Not Yet Implemented

| Area | What is missing |
|---|---|
| **Parser** | `#RANDOM` / `#IF` branching — only `#RANDOM 1` accidentally works; general branching produces wrong charts |
| **Parser** | `#EXRANK` — extended rank definition, not parsed |
| **Parser** | `#SUBTITLE`, `#SUBARTIST`, `#STAGEFILE`, `#BANNER`, `#BACKBMP`, `#MOVIE` — silently ignored |
| **Parser** | `#PATH_WAV` / `#PATH_BMP` resource path prefixes |
| **Parser** | Channel `17` / `27` (free-zone keys) — notes on these channels are dropped |
| **Renderer** | BGA / movie / stagefile / background image |
| **Renderer** | Key beams (column light during hold) |
| **Scoring** | Results screen — EX score, DJ LEVEL, clear type, gauge end % are not shown |
| **Scoring** | LN tail sample playback — data is parsed and stored but not played during gameplay |
| **Gauge** | Easy / Hard / Ex-Hard / Hazard gauge variants |
| **Gauge** | LN-specific gauge events (head miss ≠ body drop ≠ tail miss) |
| **Mods** | Random / Mirror / S-Random / H-Random column shuffle |
| **Mods** | Gauge-selection mods |
| **Mods** | Auto-scratch, hide scratch, assist options |
| **Input** | Scratch turntable semantics — scratch is routed as a plain column key |
| **Difficulty** | Star rating is always 0; no performance calculator |
| **Import** | `#RANDOM` branch resources are not included in the import manifest |
| **Import** | Resource files in subdirectories — only the filename is used, relative paths are unresolved |

---

<details>
  <summary>dev road. Click to expand!</summary>

## FIXME

- [ ] **高BPM段帧率骤降 (~1000fps → ~200fps)** — 通过分别屏蔽key-sound和BGM定位：仅key-sound时，加速段帧率缓慢平滑下降至较低值后突然恢复1000fps；仅BGM时，加速段某时刻突然暴跌至低帧率后迅速回升。详见 `BmsBackgroundAudioPlayer.cs` 和 `BmsChartSampleSound.cs`。
- [ ] autoplay random，到后半会卡死
- [ ] test 步骤里的 import real bms 步骤总是会失败
    ```
      [runtime] 2026-05-27 18:19:42 [error]: Step "import real bms" SingleStepButton triggered error
      [runtime] 2026-05-27 18:19:42 [error]: System.InvalidOperationException: TestContext.WorkDirectory must not be accessed before DefaultTestAssemblyBuilder.Build runs.
      [runtime] 2026-05-27 18:19:42 [error]: at NUnit.Framework.TestContext.get_WorkDirectory()
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.candidateTestSongRoots() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 43
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.get_testSongsRoot() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 31
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.<TestImportedRealBmsAutoplayWithBeatmapSkin>b__13_0() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 90
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Framework.Testing.Drawables.Steps.SingleStepButton.clickAction()
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Framework.Testing.Drawables.Steps.StepButton.PerformStep(Boolean userTriggered)
      [runtime] 2026-05-27 18:19:42 [error]: at osu.Framework.Testing.TestScene.runNextStep(Action onCompletion, Action`2 onError, Func`2 stopCondition)
      [runtime] 2026-05-27 18:19:44 [error]: Step "import real bms" SingleStepButton triggered an error
      [runtime] 2026-05-27 18:19:44 [error]: System.InvalidOperationException: TestContext.WorkDirectory must not be accessed before DefaultTestAssemblyBuilder.Build runs.
      [runtime] 2026-05-27 18:19:44 [error]: at NUnit.Framework.TestContext.get_WorkDirectory()
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.candidateTestSongRoots() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 43
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.get_testSongsRoot() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 31
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Game.Rulesets.BmsRuleset.Tests.TestSceneBmsVisualPlayer.<TestImportedRealBmsAutoplayWithBeatmapSkin>b__13_0() in C:\Users\kali\RiderProjects\ruleset-dev\BmsRuleset\osu.Game.Rulesets.BmsRuleset.Tests\TestSceneBmsVisualPlayer.cs:line 90
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Framework.Testing.Drawables.Steps.SingleStepButton.clickAction()
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Framework.Testing.Drawables.Steps.StepButton.PerformStep(Boolean userTriggered)
      [runtime] 2026-05-27 18:19:44 [error]: at osu.Framework.Testing.Drawables.Steps.StepButton.OnClick(ClickEvent e)
    ```

- skin
  - [ ] morden 皮肤里 有一个奇怪的组件，并且缺少了很多默认的组件
  - [ ] 血条的位置不对，而且需要调整一下风格
  - [ ] legacy 的 mod 部分和分数显示重叠了

## TODO


- [ ] random/switch
- [ ] correct metadata display (title, artist, etc, rank, hp, ...)

- skin
  - [ ] column start : value or enum(leftN, rightN, center)
  - [ ] bga position/size
  - [ ] bms skin in none-legacy way, full configurable via skin editor
  - [ ] hitGreat -> hitGreatLate/hitGreatEarly, ... (`HitGreat: imgearly,imglate` or `HitGreatLate: imglate\nHitGreatEarly:imgearly`)

- mod
  - [ ] auto scratch
  - [ ] hide scratch
  - [ ] mirror
  - [ ] 2p mod
  - [ ] different health bar
  - [ ] random
  - [ ] remember the last used mod combination

- ask
  - [ ] ask how to impl a new HUD element (e.g. combo, score, ...), and how to customize their position/size/skin

- importer
  - [ ] use a reference/symbolic link to the original bms file instead of copying it to the realm, to speed up the import.
  
- [ ] result screen

- [ ] bga


- audio
  - [ ] #WAVCMD (MacBeat) — Sets pitch (00), volume (01), or playback time (02) per WAV slot. Format: #WAVCMD <commandID> <WAV-index> <value>. Default pitch=60 (C6), volume=100%.
  - [ ] #EXWAVxx (nanasi) — Defines a WAV file with pan (-10000 to 10000), volume (-10000 to 0), and frequency/pitch (100–100000 Hz). Format: #EXWAVxx <flags> <pan> <volume> <freq> <filename>.
  - [ ] #VOLWAV n (BM98) — Global volume scalar for all sounds as a percentage. #VOLWAV 100 = original, #VOLWAV 200 = 200%.
  - [ ] #xxx97 (fgt) — Dynamic BGM volume change channel. Range [01-FF] (hex), e.g. #00197:003C sets volume to 60 at measure 1.

- [ ] Exrank

- [ ] Delete all imported BMS files confirm window
- [ ] ln tail 的打击音
- [ ] 把我们的血条注册为 两个内置 皮肤的默认血条，其余用户自定义皮肤仍可使用 mania 风格的血条
  - [ ] 把我们的血条注册为可配置的HUD
</details>
