# BMS Format Support

[Back to README](../README.md)

## Features

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
| Judge rank              | `#RANK` (0–4), `#DEFEXRANK`, `#EXRANK` / `#EXRANKxx`                       | Affects hit windows; `#RANK` is mapped to `OD`                                                    |
| Gauge total             | `#TOTAL`                                                                   | Gauge recovery coefficient, mapped to `AR`                                                        |
| Base BPM (visual)       | `#BASEBPM`                                                                 | Scroll speed reference BPM, does not affect note timing; overrides the Reference BPM setting      |
| Initial BPM             | `#BPM`                                                                     | Default 130                                                                                       |
| Extended BPM table      | `#BPMxx`                                                                   | Real-number BPM (beyond 0–255 from channel `03`)                                                  |
| STOP table              | `#STOPxx`                                                                  | Stop sequence durations (1 unit = 1/192 of a 4/4 measure)                                         |
| Sample definitions      | `#WAVxx`                                                                   | Audio file paths (WAV/OGG/MP3/FLAC)                                                               |
| BGA image/video slots   | `#BMPxx`                                                                   | Image or video file paths for BGA layers (see Background Animation below)                         |
| BGA crop definitions    | `#BGAxx`                                                                   | Cropped BGA: `<bmp> <x1> <y1> <x2> <y2> <dx> <dy>` (7-field; w=x2−x1, h=y2−y1)                    |
| Poor BGA mode           | `#POORBGA 0/1/2`                                                           | 0=Replace (hide other layers on miss), 1=Add (overlay), 2=Off                                     |
| Long-note type          | `#LNTYPE 1` / `#LNTYPE 2`                                                  | LN notation: 1=RDM (default), 2=MGQ                                                               |
| Long-note marker        | `#LNOBJ`                                                                   | LN end-point marker value (accumulated in HashSet)                                                |
| global audio volumn     | `#VOLWAV`                                                                  | Global volume scalar (0–100) for WAV playback                                                     |
| Long-note lock mode     | `#LNMODE 1` / `#LNMODE 2` / `#LNMODE 3`                                    | Locks LN type: 1=LN, 2=CN (Charge Note), 3=HCN (Hell Charge Note)                                 |
| Text events             | `#TEXTxx`, `#SONGxx`                                                       | Displayed during gameplay on channel `99`                                                         |
| Base 62 extension       | `#BASE 62`                                                                 | Case-sensitive base-62 encoding for commands and channels                                         |
| Random blocks           | `#RANDOM` / `#RONDAM` (typo tolerance), `#ENDRANDOM`, `#SETRANDOM`         | Random branch with conditional sub-blocks; `#SETRANDOM` fixes the value                           |
|                         | `#IF`, `#ELSEIF`, `#ELSE`, `#ENDIF` / `#END` / `#IFEND` / `#END IF`        |                                                                                                   |
| Switch blocks           | `#SWITCH`, `#ENDSW` / `#ENDSWITCH`, `#SETSWITCH`, `#CASE`, `#DEF`, `#SKIP` | Switch control flow with cases; `#SETSWITCH` fixes the value                                      |
| Scroll speed            | `#SCROLLxx`                                                                | Per-segment display multiplier on scroll coordinate                                               |
| Spacing change          | `#SPEEDxx`                                                                 | Per-segment multiplier on `ScrollSpeedMultiplier`                                                 |

**Not parsed:** `#EXWAVxx`,
`#WAVCMD`, `#EXBPMxx`, `#STP`, `#PATH_WAV` / `#PATH_BMP`, `#OPTION`,
`#CHANGEOPTIONxx`, `#SWBGAxx`, `#@BGAxx`, `#ARGBxx`, `#CHARFILE`,
`#ExtChr`, `#OCT/FP`, `#MATERIALS`

dynamic option channel (`A6`).

> [!NOTE]
> The resolved preview source is exposed so callers can tell whether a declared single-file (`#PREVIEW` / `preview.*`)
> or BGM/keysound playback is active. BGM/keysound playback often gives a more representative preview; declared files
> may not reflect the chart's full audio content. The **Use dedicated preview audio** setting can disable single-file
> previews and avoid their loading cost.

**Header ignored:**

| Command       | Description                                           | Why                                           |
|---------------|-------------------------------------------------------|-----------------------------------------------|
| `#PLAYER`     | Player layout: 1=Single, 2=Couple, 3=Double, 4=Battle | Layout is inferred from channel usage instead |
| `#DIFFICULTY` | Difficulty classification index (1–5)                 | Star Rating can reference difficulty instead  |

**Syntax ignored:**

| Syntax                          | Description               | Why                                       |
|---------------------------------|---------------------------|-------------------------------------------|
| `//`, `;`, `/* */`, `\` escapes | IIDXv-style comment forms | beatoraja does not implement BMS comments |

**Channels parsed:**

| Channel   | Meaning                                         |
|-----------|-------------------------------------------------|
| `01`      | BGM autoplay samples                            |
| `02`      | Measure length (time signature changes)         |
| `03`      | Inline hex BPM change (0–255)                   |
| `08`      | Extended BPM change (`#BPMxx` lookup)           |
| `09`      | STOP event (`#STOPxx` lookup)                   |
| `99`      | TEXT event (`#TEXTxx`/`#SONGxx` lookup)         |
| `A0`      | Dynamic rank change (`#EXRANKxx` lookup)        |
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

**Not parsed:** invisible note channels (`31`–`39`, `41`–`49`), dynamic option change (`A6`).

> Channel `02` controls per-measure length (time signature changes), defined by `#xxx02`. A value of `1` means standard
> length (4/4), `0.5` half length, `2` double length.
> Measure duration (ms) = `#xxx02 × 240000 / BPM` (at a fixed BPM).
> `1/1024` is the smallest value that can be accurately represented. Smaller values may round to 0 ticks, collapsing all
> events in that measure to the same position.

**Channels ignored:**

| Channel | Meaning                           | Why      |
|---------|-----------------------------------|----------|
| `97`    | Dynamic BGM volume change channel | Rare use |
| `98`    | Dynamic KEY volume change channel | Rare use |

## Comprehensive BMS Command Reference

This section catalogs **all known BMS commands** (header, channel, and control flow) across the
original BM98 specification, community players (LR2, beatoraja), and extended format proposals.
Commands are grouped by origin and listed with their status in this ruleset.

> Legend: ✓ = implemented · ◐ = partial · ✗ = not implemented · — = N/A

### 1. Header Commands

#### 1.1 BM98 Original (1998)

| Command         | Status | Notes                                                                         |
|-----------------|--------|-------------------------------------------------------------------------------|
| `#PLAYER [1-4]` | -      | 1=Single, 2=Couple, 3=Double, 4=Battle; ignored here, layout inferred instead |
| `#GENRE`        | ✓      | Also accepts `#GENLE` typo                                                    |
| `#TITLE`        | ✓      |                                                                               |
| `#ARTIST`       | ✓      |                                                                               |
| `#BPM`          | ✓      | Initial BPM (default 130)                                                     |
| `#MIDIFILE`     | ✓      | Background audio file, played from the start of the chart                     |
| `#PLAYLEVEL`    | ✓      | Difficulty level displayed as difficulty name                                 |
| `#RANK [0-3]`   | ✓      | Judgment: 0=Very Hard, 1=Hard, 2=Normal, 3=Easy; also accepts 4 (Very Easy)   |
| `#VOLWAV`       | ✓      | Global volume scalar (0–100) for WAV playback                                 |
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
| `#WAVxx` (flac) | ✓      | FLAC support, including same-name fallback from `.wav`   |

When a declared audio file is missing, same-name alternatives are tried in descending quality order: WAV, FLAC, OGG, then MP3.

#### 1.5 nanasigroove Extensions

| Command                                   | Status | Notes                                                     |
|-------------------------------------------|--------|-----------------------------------------------------------|
| `#SUBTITLE`                               | ✓      | Explicit subtitle                                         |
| `#DIFFICULTY [1-5]`                       | -      | Difficulty classification index                           |
| `#BANNER`                                 | ✓      | Banner image for music selection                          |
| `#RANK 4`                                 | ✓      | VERY EASY judgment; 1.2× wider than EASY                  |
| `#DEFEXRANK`                              | ✓      | Fine-grained judgment width percentage (100 = Normal)     |
| `#EXRANKxx`                               | ✓      | Extended rank values for dynamic rank change (channel A0) |
| `#WAV00`                                  | ✓      | Explosion sound for landmine objects (Dx/Ex channels)     |
| `#SWITCH` / `#ENDSW`                      | ✓      | Switch control-flow block                                 |
| `#CASE` / `#SKIP` / `#DEF` / `#SETSWITCH` | ✓      | Sub-commands within switch block                          |
| `#OPTION`                                 | ✗      | Forced gameplay option                                    |
| `#CHANGEOPTIONxx`                         | ✗      | Dynamic option change during play (channel A6)            |
| `#BMPxx` (base-36/base-62)                | ✓      | Extended BMP index range using the active value encoding  |

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
| `#DEFEXRANK` (redef) | ✓      | Overrides `#RANK`; value 100 = Normal baseline                                 |

#### 1.15 Generalized / Modern Extensions

| Command         | Status | Notes                                                                                           |
|-----------------|--------|-------------------------------------------------------------------------------------------------|
| `#EXBPMxx`      | ✗      | `#BPMxx` alias (BMSC parser bug workaround)                                                     |
| `#BASEBPM`      | ✓      | Visual scroll speed reference BPM (does not affect timing); overrides the Reference BPM setting |
| `#SONGxx`       | ✓      | Song-related text (merged with `#TEXTxx`)                                                       |
| `#MAKER`        | ✓      | Charter/noter name                                                                              |
| `#EXWAVxx`      | ✗      | Extended WAV with pan/volume/frequency (nanasi)                                                 |
| `#EXBMPxx`      | ✗      | Extended BMP definition slot                                                                    |
| `#EXRANK`       | ✓      | Bare `#EXRANK` sets the initial judge-window percentage; indexed `#EXRANKxx` feeds channel A0   |
| `#POORBGA`      | ✓      | POOR BGA display mode (0=Replace, 1=Add, 2=Off)                                                 |
| `#SWBGAxx`      | ✗      | Switchable BGA definition                                                                       |
| `#@BGAxx`       | ✗      | Extended BGA crop with dest w/h (9 fields); only 7-field `#BGAxx` parsed                        |
| `#ARGBxx`       | ✗      | ARGB color/alpha definition for BGA elements                                                    |
| `#POORBGAxx`    | ✗      | Per-slot POOR BGA crop definition (distinct from scalar `#POORBGA` mode)                        |
| `#BGAEXPAND`    | ✗      | Global BGA scaling: 0=stretch, 1=keep aspect, 2=no expand                                       |
| `#BGAOFF`       | ✗      | Disable BGA for the chart                                                                       |
| `#SCROLLxx`     | ✓      | Scroll speed change definitions; per-segment visual multiplier                                  |
| `#SPEEDxx`      | ✓      | Spacing change definitions via ChartSpeedFactor`                                                |
| `#VIDEOFILE`    | ✗      | Video file path                                                                                 |
| `#MOVIE`        | ✗      | Movie file path                                                                                 |
| `#SEEKxx`       | ✗      | Seek position for video                                                                         |
| `#VIDEOf/s`     | ✗      | Video frame rate setting                                                                        |
| `#VIDEOCOLORS`  | ✗      | Video color configuration                                                                       |
| `#VIDEODLY`     | ✗      | Video delay setting                                                                             |
| `#OCT/FP`       | ✗      | Octave/FootPedal play mode flag                                                                 |
| `MATERIALS`     | ✗      | Materials section marker                                                                        |
| `#MATERIALSWAV` | ✗      | Materials audio definition                                                                      |
| `#MATERIALSBMP` | ✗      | Materials image definition                                                                      |
| `#DIVIDEPROP`   | ✗      | Divide property configuration                                                                   |
| `#CHARSET`      | ✗      | Character encoding specification                                                                |
| `#CDDA`         | ✗      | CD audio track reference                                                                        |
| `#ExtChr`       | ✗      | BM98 proprietary: extended character sprite display                                             |

### 2. Channel Identifiers

#### 2.1 Audio and Timing

| Channel | Name               | Status | Description                                           |
|---------|--------------------|--------|-------------------------------------------------------|
| `01`    | BGM                | ✓      | Auto-played audio samples (not merged across lines)   |
| `02`    | Measure Length     | ✓      | Time signature: 1 = 4/4, float supported              |
| `03`    | BPM (hex)          | ✓      | Integer BPM 0–255 via hex values                      |
| `08`    | Extended BPM       | ✓      | Real-number BPM via `#BPMxx` lookup                   |
| `09`    | STOP sequence      | ✓      | Freeze duration via `#STOPxx` lookup                  |
| `97`    | Dynamic BGM volume | -      | BGM volume change mid-chart (fgt)                     |
| `98`    | Dynamic KEY volume | -      | Key sound volume change mid-chart (counterpart to 97) |

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
> LR2 uses hex-byte vs base-36 encoding is a pending spec verification. See
> [Not Yet Implemented](./development.md#not-yet-implemented) for related BGA parser and rendering gaps.

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
| `A0`    | RANK Change   | ✓      | Dynamic rank change via `#EXRANKxx` (nanasigroove)   |
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
