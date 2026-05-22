# BMS Parsing Guide for BmsRuleset

This guide is for implementing `BmsBeatmapDecoder` in this repository. It focuses on turning `.bms`, `.bme`, `.bml`, and `.pms` text charts into BmsRuleset's own beatmap model.

BmsRuleset keeps BMS semantics in BMS-owned types. Do not reintroduce osu!mania classes as parser or gameplay dependencies.

Primary reference: <https://hitkey.nekokan.dyndns.info/cmds.htm>

Related project documents:

- [BmsRuleset project design](bms-ruleset-design.md)
- [BmsRuleset development roadmap](bms-ruleset-roadmap.md)

The current project shape is:

```text
BmsBeatmapDecoder
  parses BMS text into Beatmap/BmsBeatmap with BmsHitObject instances

BmsHitObject
  StartTime
  Column
  IsLongNote
  Duration
  SamplePath
  TickInfo

BmsBeatmap / BMS gameplay components
  consume BmsHitObject and BMS-specific sample/timing/layout data directly
```

The decoder should therefore preserve all BMS-specific work in BMS-owned structures:

```text
BMS text
  -> raw command lines and preserved control-flow AST
  -> headers, definitions, channel data
  -> tick timeline and tick-to-time projection
  -> playable note events
  -> long-note pairing
  -> BmsHitObject objects with BMS tick as source of truth
  -> BmsBeatmap with BMS timing/layout/sample data
```

## Parser Goals

For the first complete BmsRuleset parser, prioritise:

- Correct visible note placement for BMS/BME/BML/PMS charts.
- Correct measure lengths from channel `02` as tick lengths, not just millisecond durations.
- Correct basic BPM (`#BPM`, channel `03`) and extended BPM (`#BPMxx`, channel `08`).
- Correct STOP timing (`#STOPxx`, channel `09`).
- Correct long notes for `#LNTYPE 1`, `#LNTYPE 2`, and `#LNOBJ`.
- Correct duplicate channel merge behavior for all mergeable channels, especially visible notes, long notes, BPM, and STOP.
- Robust handling of real-world files: mixed newlines, EOF without trailing newline, lowercase commands, lowercase base36 indexes, tabs as separators, and invalid channel payloads.

Defer these unless explicitly needed:

- Landmines (`D1-D9`, `E1-E9`).
- Hidden/invisible notes (`31-49`) as gameplay objects.
- Dynamic options (`#OPTION`, `#CHANGEOPTIONxx`, channel `A6`) as gameplay modifiers.
- BGA display (`#BMPxx`, channels `04`, `06`, `07`, `0A`, alpha channels, video) as rendered visuals.

## Text Input

BMS is plain text. The reference notes that real files may contain CRLF, LF, CR, and EOF without a final newline. `LineBufferedReader.ReadLine()` already hides most newline differences, but do not require a final newline.

Encoding is not specified by the BMS format. In practice:

- Japanese BMS files are often Shift_JIS.
- Modern files may be UTF-8, sometimes with `#CHARSET`.
- Paths and metadata can contain non-ASCII text.

Recommended implementation:

```text
1. Prefer BOM if present.
2. If a project-level importer has raw bytes, consider detecting #CHARSET early.
3. Otherwise support UTF-8 and Shift_JIS fallback.
4. Keep unknown bytes from breaking parse; metadata may be garbled, but notes should still parse.
```

If `LineBufferedReader` is already constructed by osu!'s decoding pipeline, document what encoding was used and keep parser logic independent from it.

## Line Classes

Only command lines start with `#` after optional indentation. Other lines are comments.

There are two primary command shapes:

```text
Header line:
#COMMAND value
#COMMANDxx value

Channel line:
#mmmCC:data
```

Where:

- `mmm` is a three-digit measure number, usually `000` through `999`.
- `CC` is a two-character channel identifier.
- `data` is a string of two-character indexes.

Examples:

```bms
#TITLE Example Song
#BPM 150
#WAV01 kick.wav
#00111:01000100
```

Parsing rules:

- Commands are case-insensitive for parser recognition.
- Header names and definition indexes should be normalised to uppercase.
- Header value starts after the first whitespace separator. Tabs should be accepted.
- Channel value starts after `:`. Do not split channel values by whitespace.
- A channel line has priority over a header line when it matches `#dddCC:`.

Suggested recogniser:

```csharp
private static bool TryParseChannelLine(string line, out int measure, out string channel, out string data)
{
    measure = 0;
    channel = string.Empty;
    data = string.Empty;

    string trimmed = line.TrimStart(' ', '\t', '\u3000');
    if (!trimmed.StartsWith('#'))
        return false;

    int colon = trimmed.IndexOf(':');
    if (colon < 0 || colon < 6)
        return false;

    string key = trimmed.Substring(1, colon - 1);
    if (key.Length != 5)
        return false;

    if (!int.TryParse(key.Substring(0, 3), out measure))
        return false;

    channel = key.Substring(3, 2).ToUpperInvariant();
    data = trimmed[(colon + 1)..].Trim();
    return true;
}
```

## Base36 Indexes

Most modern BMS uses two-character base36 indexes:

```text
00: rest / empty
01-ZZ: definition slot or object value
```

Base36 digit values:

```text
0-9 => 0-9
A-Z => 10-35
```

Use uppercase before decoding.

```csharp
private static int ParseBase36(string value)
{
    int result = 0;

    foreach (char c in value.ToUpperInvariant())
    {
        int digit = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'A' and <= 'Z' => c - 'A' + 10,
            _ => -1,
        };

        if (digit < 0)
            throw new FormatException($"Invalid base36 digit '{c}'.");

        result = result * 36 + digit;
    }

    return result;
}
```

For note data, split payload into pairs:

```text
#00111:00112233
        00 11 22 33
```

If `data.Length` is odd, ignore the trailing single character and log a warning. Do not throw unless strict import mode is added.

## Header Commands Needed by BmsRuleset

### Metadata

| Command | Use |
|---|---|
| `#TITLE string` | Beatmap title. |
| `#SUBTITLE string` | Optional subtitle. Keep separate if model supports it; otherwise append or store as tag. |
| `#ARTIST string` | Artist. |
| `#SUBARTIST string` | Extra artist names. May appear multiple times. |
| `#GENRE string` | Genre. |
| `#COMMENT string` | Optional description. May appear multiple times in some charts. |
| `#PLAYLEVEL value` | Display difficulty. Can be numeric or string. |
| `#DIFFICULTY 1-5` | Difficulty category. |
| `#PLAYER 1-4` | Do not trust blindly; infer columns from used channels. |
| `#RANK 0-4` | Judgement difficulty. Store for BMS hit-window/gauge design. |
| `#TOTAL n` | Gauge total. BmsRuleset currently has custom health; store only if custom gauge is later implemented. |

### Extended Header and Tag Reference

The decoder should not silently discard known BMS tags. Unsupported tags should be preserved in a raw header/tag collection so later native BMS systems can consume them without requiring re-import.

| Command/tag | Meaning | BmsRuleset import behavior |
|---|---|---|
| `#PLAYER n` | Play mode hint: SP, couple, DP, battle. | Store, but infer layout from channels. |
| `#GENRE string` | Genre text. | Store in metadata/tags. |
| `#TITLE string` | Main title. | Store as title; preserve raw. |
| `#SUBTITLE string` | Explicit subtitle. | Store list; may be repeated. |
| `#ARTIST string` | Main artist. | Store as artist. |
| `#SUBARTIST string` | Extra artist/video/noter credits. | Store list; may be repeated. |
| `#MAKER string` | Chart author/producer. | Store as creator if no better author tag exists, otherwise preserve. |
| `#COMMENT string` | Song-select or chart comment. | Store list; may be repeated. |
| `%URL string` | External URL. | Preserve as metadata link/tag. |
| `%EMAIL string` | Contact email. | Preserve as metadata tag. |
| `#PLAYLEVEL value` | Display difficulty level; may be non-numeric. | Store raw string and numeric value when parseable. |
| `#DIFFICULTY 1-5` | Difficulty category. | Store category. |
| `#RANK n` | Judgement preset. | Store for hit-window design. |
| `#DEFEXRANK n` | Default extended judgement rank. | Preserve for future judgement support. |
| `#EXRANKxx n` | Dynamic judgement rank slot. | Store definitions for channel `A0`. |
| `#TOTAL n` | Gauge total. | Store for BMS gauge. |
| `#VOLWAV n` | Global audio volume. | Store for sample mixer. |
| `#STAGEFILE filename` | Loading/splash image. | Import only if referenced by this chart/set metadata display. |
| `#BANNER filename` | Song-select banner. | Import only if used by UI metadata. |
| `#BACKBMP filename` | Background image. | Import only if used by BGA/background rendering. |
| `#CHARFILE filename` | Character/skin file used by some PMS players. | Preserve and optionally import if native character display is implemented. |
| `#PATH_WAV path` | Alternate audio resource directory. | Use during resource resolution; do not import unrelated files in that directory. |
| `#WAVxx filename` | Audio slot definition. | Store slot; import only if slot is referenced by notes, BGM, LN ends that sound, landmines, or reachable runtime branches. |
| `#WAV00 filename` | Landmine explosion sound in nanasi-style charts. | Store for landmines; not a normal note sample. |
| `#EXWAVxx ...` | Extended audio definition used by some players. | Preserve raw and import referenced filename when parseable. |
| `#WAVCMD ...` | Audio command/modifier for MacBeat-style MOD use. | Preserve raw; defer execution. |
| `#BPM n` | Initial BPM. | Store initial BPM. |
| `#BPMxx n` | Extended BPM slot for channel `08`. | Store definition. |
| `#EXBPMxx n` | Alternate extended BPM spelling in some players. | Treat like `#BPMxx` if unambiguous; preserve raw. |
| `#BASEBPM n` | Visual/base BPM hint used by some clients. | Store for scroll/display design; do not replace actual timing. |
| `#STOPxx n` | STOP slot for channel `09`. | Store definition. |
| `#STP ...` | Absolute STOP sequence extension. | Preserve and support later after channel `09`; import must not drop it. |
| `#LNTYPE 1` | Alternating LN start/end in LN channels. | Support. |
| `#LNTYPE 2` | MGQ continuous LN notation in LN channels. | Support. |
| `#LNOBJ xx` | Visible-channel LN terminator WAV index. | Support; may be repeated. |
| `#OCT/FP` | Octave/foot-pedal extension flag. | Preserve for layout interpretation. |
| `#OPTION value` | Forced option/modifier. | Preserve as runtime option event seed. |
| `#CHANGEOPTIONxx value` | Dynamic option definition for channel `A6`. | Store definition and preserve runtime event. |
| `#TEXTxx string` | Text display slot for channel `99`. | Store definitions for future UI events. |
| `#SONGxx filename` | Alternate song/audio slot in some PMS players. | Preserve and import referenced file only if used. |
| `#BMPxx filename` | Image/video slot. | Store; import only if referenced by BGA/background/stage events or metadata. |
| `#EXBMPxx ...` | Extended bitmap definition. | Preserve raw and import referenced file when parseable. |
| `#BGAxx ...` | Cropped image sequence definition. | Store for BGA renderer. |
| `#@BGAxx ...` | Extended cropped image sequence definition. | Store for BGA renderer. |
| `#POORBGAxx ...` | Poor/miss BGA definition. | Store for BGA renderer. |
| `#SWBGAxx ...` | Switchable BGA extension. | Preserve raw until BGA support is complete. |
| `#ARGBxx ...` | Alpha/color extension for images. | Store for BGA renderer. |
| `#VIDEOFILE filename` | Video file definition. | Import only if referenced/used by BGA playback. |
| `#VIDEOf/s n` | Video framerate. | Store with video metadata. |
| `#VIDEOCOLORS n` | Video color metadata. | Preserve. |
| `#VIDEODLY n` | Video delay. | Store with video metadata. |
| `#MOVIE filename` | Movie file definition used by some players. | Import only if used by visual playback. |
| `#SEEKxx n` | Video seek slot. | Store for BGA/video renderer. |
| `#MATERIALS...` | Material/resource group declarations. | Preserve; do not bulk import all listed files unless referenced. |
| `#MATERIALSWAV...` | Audio material declaration. | Use as resource search hint only. |
| `#MATERIALSBMP...` | Image material declaration. | Use as resource search hint only. |
| `#DIVIDEPROP ...` | Resource subdivision/proportion metadata. | Preserve raw. |
| `#CHARSET value` | Text encoding hint. | Use before decoding when raw bytes are available; otherwise preserve. |

Channel-side tags/events that need preservation even before full rendering:

| Channel | Meaning | BmsRuleset import behavior |
|---|---|---|
| `04` | BGA base. | Store BGA event, import referenced `#BMPxx`. |
| `06` | Poor BGA. | Store BGA event, import referenced `#BMPxx`. |
| `07` | BGA layer. | Store BGA event, import referenced `#BMPxx`. |
| `0A` | BGA layer 2. | Store BGA event, import referenced `#BMPxx`. |
| `0B-0E` | BGA opacity/alpha channels. | Store visual events. |
| `99` | Text event via `#TEXTxx`. | Store text event. |
| `A0` | EXRANK event via `#EXRANKxx`. | Store judgement-change event. |
| `A6` | Option change via `#CHANGEOPTIONxx`. | Store runtime option event. |

Duplicate header behavior:

- For ordinary scalar headers, the last line wins.
- `#SUBTITLE`, `#SUBARTIST`, `#COMMENT`, and `#LNOBJ` may be repeated. For BmsRuleset, keep lists for repeated values, and choose the last value only when a single value is required.

Example:

```bms
#TITLE First Title
#TITLE Final Title
#ARTIST Composer
#SUBARTIST Vocalist
#SUBARTIST Guitarist
```

Recommended parse result:

```text
Title = "Final Title"
Artist = "Composer"
SubArtists = ["Vocalist", "Guitarist"]
```

### Audio Definitions

```text
#WAVxx filename
```

`xx` is normally `01-ZZ`. Some landmine usage refers to `#WAV00`, but normal playable notes treat `00` as rest.

Store definitions in:

```csharp
Dictionary<string, string> wavDefinitions;
```

Normalise key to uppercase two characters:

```bms
#wav0a clap.ogg
#WAV0A clap2.ogg
```

Last definition wins for the same slot:

```text
WAV["0A"] = "clap2.ogg"
```

The same filename may be intentionally assigned to multiple slots to allow overlapping playback. Do not deduplicate slots during parse.

```bms
#WAV01 crash.wav
#WAV02 crash.wav
#00101:0102
```

For BmsRuleset gameplay, visible objects should carry the sample referenced by their own index. BGM channel `01` should be stored separately; it is not a playable note.

### Timing Definitions

```text
#BPM n
#BPMxx n
#STOPxx n
```

Store:

```csharp
double initialBpm = 130;
Dictionary<string, double> bpmDefinitions;
Dictionary<string, double> stopDefinitions;
```

Rules:

- `#BPM n` sets initial BPM. Decimal values should be accepted.
- Channel `03` changes BPM using hexadecimal `01-FF` directly; parse this channel as hex, not base36.
- `#BPMxx n` defines an extended BPM slot used by channel `08`. Decimal values should be accepted.
- `#STOPxx n` defines a stop slot used by channel `09`. Fractional values exist in real charts; accept double even if some players integerise.
- STOP duration is based on a normal 4/4 measure at the active BPM, not on the current channel `02` measure length.
- Negative BPM and negative STOP exist in gimmick charts. Phase 1 may reject or clamp them, but should log a clear warning.

Example:

```bms
#BPM 150
#BPM01 75.5
#STOP01 96
#00108:0001
#00209:01
```

## Channel Commands Needed by BmsRuleset

### Core Channels

| Channel | Meaning | BmsRuleset action |
|---|---|---|
| `01` | BGM objects using `#WAVxx` | Store as autoplay/background samples, not `BmsHitObject`. Multiple lines are independent. |
| `02` | Measure length multiplier | Store measure length. Last line wins for same measure. |
| `03` | Basic BPM change, hex `01-FF` | Add BPM event. |
| `08` | Extended BPM change via `#BPMxx` | Add BPM event. |
| `09` | STOP via `#STOPxx` | Add stop event. |
| `11-16`, `18-19` | Standard 1P visible notes | Create playable note candidates when the selected layout maps the lane. |
| `17` | Classic free-zone / extended 9K lane | Ignore in 5K/7K unless the selected layout explicitly maps it, such as BME-type PMS. |
| `21-26`, `28-29` | Standard 2P visible notes | Create playable note candidates when the selected layout maps the lane. |
| `27` | Classic 2P free-zone / extended lane | Ignore in standard DP unless an explicit extended layout maps it. |
| `31-36`, `38-39` | 1P invisible notes | Ignore for gameplay initially. Keep for future key-sound assist if needed. |
| `37` | Invisible counterpart of `17` | Ignore initially. |
| `41-46`, `48-49` | 2P invisible notes | Ignore for gameplay initially. |
| `47` | Invisible counterpart of `27` | Ignore initially. |
| `51-56`, `58-59` | Standard 1P long-note channel | Pair into hold notes for `#LNTYPE 1` when the selected layout maps the lane. |
| `57` | Long-note counterpart of `17` | Ignore unless the selected layout maps `17`. |
| `61-66`, `68-69` | Standard 2P long-note channel | Pair into hold notes for `#LNTYPE 1` when the selected layout maps the lane. |
| `67` | Long-note counterpart of `27` | Ignore unless the selected layout maps `27`. |
| `D1-D9` | 1P landmine | Ignore initially. |
| `E1-E9` | 2P landmine | Ignore initially. |

BGA and visual channels can be parsed into a future structure but should not block gameplay import.

The broad ranges are not always contiguous gameplay lanes. Decide the key layout before filtering note channels; otherwise classic free-zone channels `17`/`27` can accidentally become playable columns in ordinary 5K/7K charts.

### Channel Data Positions

Each channel payload divides the measure into equal slices.

```bms
#00111:00112233
```

This has four pairs:

| Pair index | Pair | Fraction in measure |
|---:|---|---:|
| 0 | `00` | `0/4` |
| 1 | `11` | `1/4` |
| 2 | `22` | `2/4` |
| 3 | `33` | `3/4` |

`00` means no object at that slice.

Convert channel line to events using rational positions, not floating point keys:

```csharp
record BmsGridPosition(int Measure, int Numerator, int Denominator);
record BmsRawEvent(int Measure, int Numerator, int Denominator, string Channel, string Value, int LineNumber);
```

Reduce or compare fractions by cross multiplication.

BmsRuleset should convert positions to BMS ticks before gameplay objects are created. Use `192` as the default base resolution for a normal 4/4 measure, but do not force the internal timeline to stay at `192`.

```text
resolution = dynamic ticks per normal measure, initially 192
measureTickLength = resolution * measureLength
eventTick = measureStartTick + measureTickLength * pairIndex / pairCount
```

Most charts land on integer ticks at resolution `192`. Some real charts use payload divisions or measure lengths that do not divide `192`. In that case, dynamically extend resolution, usually by `lcm(192, pairCount, measureLength denominators...)`, so event ticks remain integer. Store rational/fixed-point ticks only if the required resolution becomes too large.

## Duplicate Channel Lines

The reference describes important real-world behavior:

- Same ordinary channel in same measure is merged.
- Later non-`00` objects overwrite earlier objects at the same grid position.
- `00` never overwrites an existing object.
- Channel `01` is not merged; multiple BGM lines remain independent.
- Channel `02` is not merged; last line wins.
- Channel `A6` is not relevant for phase 1.

Example:

```bms
#00113:11111111
#00113:0022332255224400
#00113:0066
```

Merge result conceptually:

```text
#00113:1122332266224400
```

Implementation approach:

```text
For mergeable channels:
  key = (measure, channel, rational position)
  if value != "00": dictionary[key] = event

For BGM channel 01:
  append every non-00 event; do not overwrite

For measure length channel 02:
  store the latest parsed value per measure
```

Use line number for warnings and deterministic tie-breaking.

Apply this merge before timing construction. Channels `03`, `08`, and `09` are mergeable ordinary channels, so duplicate BPM/STOP channel lines in the same measure should be resolved with the same "later non-`00` overwrites" rule before building the timing map.

## Measure Length

Channel `02` changes measure tick length. Value `1` means one normal 4/4 measure. At default resolution this is `192` ticks, but at dynamically expanded resolution it is `resolution` ticks. Value `0.5` means `resolution / 2`; value `2` means `resolution * 2`.

```bms
#00002:1
#00102:0.5
#00202:2
```

If channel `02` is omitted for a measure, length is `1`.

Duplicate channel `02` in the same measure should use the last line.

Measure length affects the tick span of that measure and the placement of all fractions inside it. Millisecond duration is a projection from ticks through the active BPM map.

Tick and time formulas at constant BPM:

```text
resolution = dynamic ticks per normal measure, default 192
measure ticks = resolution * measureLength
one tick ms = (4 * 60000 / bpm) / resolution
normal 4/4 measure ms = 4 * 60000 / bpm
measure ms = normal measure ms * measureLength
```

Example at 120 BPM:

```text
beat duration = 500ms
normal measure = 2000ms
length 0.5 measure = 1000ms
length 2 measure = 4000ms
```

## Tick Timeline and Time Projection

BMS timing depends on measure lengths, BPM changes, and STOPs. BmsRuleset should bind notes, BGM, BGA, text, options, and control events to BMS ticks. Only the timing map binds ticks to milliseconds.

`HitObject.StartTime` is still required by osu!'s object lifetime and scoring infrastructure, but for native BMS it should be treated as a projection from `BmsTick`, not as the authoritative chart position.

Recommended tick model:

```csharp
readonly record struct BmsTick(long Value);

sealed class BmsTickResolution
{
    public const int DefaultTicksPerMeasure = 192;
    public required long TicksPerMeasure { get; init; }
}

record BmsTimingSegment(
    BmsTick StartTick,
    double StartTime,
    double Bpm,
    double MsPerTick);
```

For integer ticks:

```text
msPerTick = (4 * 60000 / bpm) / resolution
timeAtTick = segment.StartTime + (tick - segment.StartTick) * segment.MsPerTick
```

For fallback rational ticks, perform the same calculation with rational subtraction converted to double only at projection boundaries.

Build a sorted timing-event list before projecting `BmsHitObject.StartTime`.

Recommended event model:

```csharp
enum TimingEventType
{
    Bpm,
    Stop,
}

record TimingEvent(
    int Measure,
    int Numerator,
    int Denominator,
    TimingEventType Type,
    double Value,
    int LineNumber);
```

Sort by:

```text
1. measure
2. fraction within measure
3. event order at same fraction: BPM before STOP
4. line number if needed
```

If more than one BPM event remains at the same fraction after duplicate-channel merging, apply them in deterministic order and let the last applied BPM become active before STOP duration is computed. Line order is the safest tie-breaker; logging a warning is useful because charts should not rely on ambiguous same-tick BPM conflicts.

Important ordering from the reference:

- Objects at a tick are played/judged at that tick before the STOP delay takes effect.
- BPM changes at the same tick should be applied before STOP duration is computed.
- STOP duration uses the BPM active after same-tick BPM changes.

For object start times, compute the projected time at the event tick before applying stop delay at that tick.

### Basic BPM Channel `03`

Channel `03` stores direct BPM as hexadecimal `01-FF`.

```bms
#BPM 120
#00103:00FE
```

`FE` is hex `254`, so BPM changes to 254 at measure `001`, fraction `1/2`.

### Extended BPM Channel `08`

Channel `08` references `#BPMxx` definitions.

```bms
#BPM 150
#BPM01 75.5
#BPM02 300
#00408:0102
```

This creates:

```text
measure 4, fraction 0/2: BPM 75.5
measure 4, fraction 1/2: BPM 300
```

### STOP Channel `09`

Channel `09` references `#STOPxx` definitions. A STOP value is counted in units of `1/192` of a whole note at the active BPM.

```text
stopMs = stopValue * (4 * 60000 / currentBpm) / 192
       = stopValue * 60000 / (currentBpm * 48)
```

Example:

```bms
#BPM 120
#STOP01 96
#00109:01
```

At 120 BPM:

```text
one STOP unit = 60000 / (120 * 48) = 10.416666ms
96 units = 1000ms
```

If a note and a STOP occur at the same fraction, the note time is before the stop delay.

```bms
#BPM 120
#STOP01 96
#00111:01
#00109:01
#00211:01
```

Result:

```text
note at #001 start: 2000ms if measure 0 is normal and no prior objects are considered
STOP adds 1000ms after that tick
note at #002 start occurs 1000ms later than it would without the STOP
```

## Timing Projection Algorithm

One robust strategy is to convert every relevant grid position into `BmsTick`, then build a tick-to-time projection by walking timing changes.

Pseudo-code:

```text
resolution = ComputeDynamicResolution(default 192, channel payloads, measure lengths)
measureStartTick = 0
for measure in ascending measures:
  measureLength = measureLengths.GetValueOrDefault(measure, 1)
  measureTickLength = resolution * measureLength
  convert every channel pair position in this measure to BmsTick
  measureStartTick += measureTickLength

currentTime = 0
currentTick = 0
currentBpm = initialBpm
msPerTick = (4 * 60000 / currentBpm) / resolution

for tickPosition in all event ticks sorted ascending:
  currentTime += (tickPosition - currentTick) * msPerTick
  assign projected currentTime to all note/BGM/BGA objects at this tick
  apply all BPM changes at this tick in deterministic order
  msPerTick = (4 * 60000 / currentBpm) / resolution
  apply all STOPs at this tick in deterministic order, adding stopMs to currentTime
  currentTick = tickPosition
```

This handles BPM changes inside a measure because each tick segment uses the BPM active for that tick range. The decoder stores native ticks and projects them to `StartTime`; current gameplay rendering consumes the projected times.

Include positions from note channels, BGM channel, BGA/text/option channels, BPM channels, and STOP channel so all object times are projected from the same tick map.

## Column Mapping

BmsRuleset needs a zero-based `Column` for each playable BMS object so its own playfield can route objects to lanes consistently.

Do not rely only on `#PLAYER`; infer the layout from used channels and file type. Build the selected layout's allowed lane set first, then ignore or warn for visible/LN channels outside that set.

### Recommended 7K/BME SP Mapping

For standard BME 7-key plus scratch, use scratch as the leftmost BMS column:

| BMS channel | Meaning | Bms column |
|---|---|---:|
| `16` | Scratch | 0 |
| `11` | Key 1 | 1 |
| `12` | Key 2 | 2 |
| `13` | Key 3 | 3 |
| `14` | Key 4 | 4 |
| `15` | Key 5 | 5 |
| `18` | Key 6 | 6 |
| `19` | Key 7 | 7 |

For BMS 5-key, use:

| BMS channel | Meaning | Bms column |
|---|---|---:|
| `16` | Scratch | 0 |
| `11` | Key 1 | 1 |
| `12` | Key 2 | 2 |
| `13` | Key 3 | 3 |
| `14` | Key 4 | 4 |
| `15` | Key 5 | 5 |

Channel `17` is a free zone in classic BMS and should not become a normal gameplay column unless an explicit foot-pedal mode is implemented.

### Recommended 14K/DP Mapping

For double play BME, use 16 columns:

| BMS channel | Meaning | Bms column |
|---|---|---:|
| `16` | 1P scratch | 0 |
| `11` | 1P key 1 | 1 |
| `12` | 1P key 2 | 2 |
| `13` | 1P key 3 | 3 |
| `14` | 1P key 4 | 4 |
| `15` | 1P key 5 | 5 |
| `18` | 1P key 6 | 6 |
| `19` | 1P key 7 | 7 |
| `21` | 2P key 1 | 8 |
| `22` | 2P key 2 | 9 |
| `23` | 2P key 3 | 10 |
| `24` | 2P key 4 | 11 |
| `25` | 2P key 5 | 12 |
| `28` | 2P key 6 | 13 |
| `29` | 2P key 7 | 14 |
| `26` | 2P scratch | 15 |

If only 1P channels are used, create an SP beatmap. If any 2P visible or LN channel is used, create a DP beatmap.

### PMS Mapping

For `.pms`, use 9 columns:

| PMS channel | Column |
|---|---:|
| `11` | 0 |
| `12` | 1 |
| `13` | 2 |
| `14` | 3 |
| `15` | 4 |
| `22` | 5 |
| `23` | 6 |
| `24` | 7 |
| `25` | 8 |

PMS can also appear in BME-like channel layouts. Use extension and observed channels to choose mapping; log ambiguous charts.

For BME-type PMS 9K, use this alternate mapping:

| BMS channel | Column |
|---|---:|
| `11` | 0 |
| `12` | 1 |
| `13` | 2 |
| `14` | 3 |
| `15` | 4 |
| `18` | 5 |
| `19` | 6 |
| `16` | 7 |
| `17` | 8 |

### Layout Inference

Recommended inference:

```text
if extension is .pms and standard PMS channels 22-25 are present:
  use PMS 9K
else if extension is .pms and BME-type PMS channels 16-19 are present:
  use BME-type PMS 9K
else if any 2P visible/LN channel exists:
  use DP mapping
else if any 7K-only channel 18/19 exists:
  use BME SP mapping
else:
  use BMS 5K mapping
```

The current implementation uses explicit `BmsLayout` / `BmsLayoutVariant` metadata and `TotalColumns`, so sparse charts still get the right lane count. Do not fall back to `maxColumn + 1` except as a temporary recovery path for malformed or non-BMS sources.

## Visible Notes

Visible note channels create normal `BmsHitObject` candidates.

Example:

```bms
#TITLE Notes Example
#BPM 120
#WAV01 kick.wav
#WAV02 snare.wav
#00111:0100
#00112:0002
```

At 120 BPM, if measure `000` is empty and normal length:

```text
#00111 pair 0/2 -> start of measure 001 -> key 1 -> column 1 -> WAV01
#00112 pair 1/2 -> middle of measure 001 -> key 2 -> column 2 -> WAV02
```

Create:

```csharp
new BmsHitObject
{
    BmsTick = computedTick,
    StartTime = computedTime,
    Column = mappedColumn,
    IsLongNote = false,
    Samples = CreateSamples("01"),
}
```

## Long Notes

BMS has multiple long-note styles. BmsRuleset should support `#LNTYPE 1`, `#LNTYPE 2`, and `#LNOBJ` directly.

### `#LNTYPE 1` with Channels `51-69`

For `#LNTYPE 1`, non-`00` objects in LN channels alternate start and end per lane. LN channels mirror the selected visible-channel layout; treat `57`/`67` like `17`/`27` and only import them when the selected layout maps those lanes.

```bms
#LNTYPE 1
#WAV22 hold_start.wav
#WAV33 ignored_end.wav
#00151:00220000
#06451:000000000033
```

Interpretation:

```text
channel 51 maps like visible channel 11
first non-00 object opens hold
next non-00 object closes hold
start sample is usually used
end sample is usually ignored for phase 1
```

Algorithm:

```text
openHoldByColumn = Dictionary<int, RawNoteEvent>

for each LN event sorted by tick:
  column = MapLnChannelToColumn(channel)
  if no open hold in column:
    openHoldByColumn[column] = event
  else:
    start = openHoldByColumn[column]
    durationTicks = event.Tick - start.Tick
    create BmsHitObject { BmsTick = start.Tick, EndTick = event.Tick, IsLongNote = true }
    remove open hold
```

If an open hold has no end, ignore it and warn. Do not create a zero-length hold.

### `#LNOBJ` with Visible Channels

`#LNOBJ xx` declares one or more WAV indexes as visible-channel LN terminators.

```bms
#LNOBJ ZZ
#WAV22 hold_start.wav
#WAVZZ empty.wav
#00111:00220000
#06411:0000000000ZZ
```

Interpretation:

- Visible object `22` opens the hold.
- The next object in the same visible channel whose value matches `LNOBJ` closes the hold.
- The terminator should not become a normal note.
- The terminator sample may sound in some players, but BmsRuleset phase 1 should ignore it.

Algorithm:

```text
lnObjValues = HashSet<string> from all #LNOBJ lines
pendingVisibleByColumn = Dictionary<int, RawNoteEvent>

for each visible event sorted by time:
  if event.Value is in lnObjValues:
    if pendingVisibleByColumn has start for column:
      create hold from start to event
      remove pending start
    else:
      warn orphan LNOBJ terminator
    continue

  if #LNOBJ mode is active:
    if pendingVisibleByColumn already has start:
      flush previous start as normal note, then set current as pending
    else:
      set current as pending
  else:
    create normal note immediately

after all events:
  flush remaining pending visible events as normal notes
```

This conservative algorithm prevents every visible note from being delayed forever while still pairing common LNOBJ charts.

For stricter compatibility, a visible event should become a normal note only when it is not followed by an LNOBJ terminator in the same lane. That requires lookahead per channel/column.

### `#LNTYPE 2`

`#LNTYPE 2` is obsolete MGQ-style notation, but it appears in real BMS-family content and must be supported by BmsRuleset. It uses continuous non-`00` values in long-note channels. A run starts at the first non-`00` cell and ends at the first following `00` cell or at the end of the explicit run.

Important parser requirement: for `#LNTYPE 2`, `00` cells are semantically meaningful because they close holds. Do not discard all `00` cells before LN resolution. Keep expanded channel cells, or keep enough run-boundary information to know where each continuous run ends.

Example:

```bms
#LNTYPE 2
#00151:11110000
```

Interpretation at four equal cells:

```text
cell 0: 11 -> start hold
cell 1: 11 -> hold continues
cell 2: 00 -> close hold at fraction 2/4
cell 3: 00 -> no hold
```

Algorithm:

```text
for each selected LN channel independently:
  expand channel payloads into ordered cells, preserving non-00 and 00 cells
  merge duplicate LN2 cells with line-order rules, but keep zero cells as possible run terminators
  openRun = null

  for each cell in ascending tick order:
    if cell.Value != "00":
      if openRun is null:
        openRun = cell
      continue

    if openRun is not null:
      create hold from openRun.Tick to cell.Tick using openRun sample
      openRun = null

  if openRun is not null:
    close at the end tick of the last explicit LN2 cell/run if known; otherwise warn and ignore
```

Keep this separate from `#LNTYPE 1` pairing. `#LNTYPE 1` alternates start/end objects; `#LNTYPE 2` consumes sustained runs.

## BGM Samples

Channel `01` defines background audio events using `#WAVxx` definitions.

```bms
#WAV01 bgm_loop.ogg
#WAV02 cymbal.ogg
#00001:01
#00201:0002
```

These are not player notes. The current implementation stores them as `BmsSampleEvent` entries on `BmsBeatmap.BackgroundSampleEvents` and feeds them into gameplay sample playback.

Do not convert BGM events to playable notes.

## Sound Samples on Notes

For visible notes, `Value` references `#WAVxx`.

```bms
#WAV01 normal-hit.wav
#00111:01
```

BmsRuleset needs to attach the sound to the BMS note object. Current `BmsHitObject` stores the resolved BMS resource name in `SamplePath`; playback uses BMS-specific sample lookup rather than osu! default hit sounds.

The exact osu! sample lookup path should be designed alongside resource import. The parser guide-level model is:

```csharp
private static IList<HitSampleInfo> CreateSamples(string wavIndex, Dictionary<string, string> wavDefinitions)
{
    if (!wavDefinitions.TryGetValue(wavIndex, out string filename))
        return Array.Empty<HitSampleInfo>();

    return new[] { new HitSampleInfo(filename) };
}
```

Normal `HitSampleInfo` lookup is not sufficient for arbitrary BMS filenames. Current gameplay uses BMS-specific sample info and sound playback so key/BGM samples can resolve chart resource names and avoid osu! default hit-sound behaviour.

Key-sound behaviour is input-driven:

- A key press plays the next queued sample for that lane even if the press does not judge a note.
- Miss judgement does not play note hit sound.
- osu! automatic/default hit sounds are disabled for BMS notes.
- BMS key and BGM samples ignore osu! global Effect volume but still follow universal volume.

## BGA and Visual Commands

For gameplay import, these can be ignored at first:

| Command/channel | Meaning |
|---|---|
| `#BMPxx` | Image/video definition. |
| `#BGAxx`, `#@BGAxx` | Cropped image sequence definitions. |
| `#xxx04` | BGA base. |
| `#xxx06` | Poor BGA. |
| `#xxx07` | BGA layer. |
| `#xxx0A` | BGA layer 2. |
| `#xxx0B-0E` | BGA opacity. |
| `#ARGBxx`, `#EXBMPxx` | Alpha/color extensions. |

Do not fail a chart because these appear.

## Control Flow Runtime Model

BMS supports conditional command blocks:

```text
#RANDOM n / #SETRANDOM n
  #IF n
  #ELSEIF n
  #ELSE
  #ENDIF
#ENDRANDOM

#SWITCH n / #SETSWITCH n
  #CASE n
  #SKIP
  #DEF
#ENDSW
```

Control flow can affect any line, including headers, definitions, channels, metadata, samples, and BGA. BmsRuleset must preserve control flow at import time and choose branches at play time. Branch selection is a runtime event, not an import-time preprocessing step.

Import-time responsibilities:

```text
1. Parse control-flow blocks into a BMS command AST.
2. Preserve all branches in BmsBeatmap/BmsControlScript.
3. Extract metadata and resource references conservatively from every reachable branch.
4. Import assets referenced by any runtime branch that can be selected.
5. Do not select #RANDOM/#SWITCH branches permanently during import.
```

Play-time responsibilities:

```text
1. Create a BmsBranchState at gameplay load.
2. Evaluate #SETRANDOM/#SETSWITCH as fixed branch values.
3. Evaluate #RANDOM/#SWITCH from a replay-safe RNG seed.
4. Materialise the active command stream for the selected branch.
5. Build runtime tick events from that active stream.
6. Store branch decisions in replay data so replay playback uses the same branches.
```

Do not use non-deterministic `Random.Shared` for gameplay branch selection unless the selected branch values are stored in replay/score data. Recommended seed sources are beatmap hash, ruleset version, score start time when creating a new play, and replay-stored branch decisions when replaying.

Runtime AST sketch:

```csharp
abstract record BmsCommandNode(int LineNumber);
record BmsRawCommandNode(int LineNumber, string RawLine) : BmsCommandNode(LineNumber);
record BmsRandomBlockNode(int LineNumber, int MaxValue, IReadOnlyList<BmsIfBranch> Branches) : BmsCommandNode(LineNumber);
record BmsSwitchBlockNode(int LineNumber, int MaxValue, IReadOnlyList<BmsSwitchCase> Cases) : BmsCommandNode(LineNumber);
record BmsIfBranch(int MatchValue, IReadOnlyList<BmsCommandNode> Commands);
record BmsSwitchCase(int? MatchValue, IReadOnlyList<BmsCommandNode> Commands);
```

Example:

```bms
#RANDOM 2
#IF 1
#00111:01
#ENDIF
#IF 2
#00112:02
#ENDIF
#ENDRANDOM
```

If runtime value is `1`, only `#00111:01` is active for that play. If runtime value is `2`, only `#00112:02` is active. Both branches remain imported and available.

## Parser Data Structures

Suggested internal structures:

```csharp
sealed class BmsParseState
{
    public string Title = string.Empty;
    public string Artist = string.Empty;
    public string Genre = string.Empty;
    public string PlayLevel = string.Empty;
    public int? Difficulty;
    public int Player = 1;
    public int LnType = 1;

    public readonly Dictionary<string, string> Wav = new();
    public readonly Dictionary<string, double> Bpm = new();
    public readonly Dictionary<string, double> Stop = new();
    public readonly HashSet<string> LnObj = new();
    public readonly Dictionary<string, string> RawHeaders = new();
    public readonly List<BmsCommandNode> ControlScript = new();

    public readonly Dictionary<int, double> MeasureLengths = new();
    public readonly List<BmsRawEvent> BgmEvents = new();
    public readonly List<BmsRawEvent> TimingEvents = new();
    public readonly List<BmsRawEvent> VisibleEvents = new();
    public readonly List<BmsRawEvent> LongNoteEvents = new();
}
```

Parse in passes:

```text
1. Read all lines with line numbers.
2. Parse and preserve control flow as a command AST.
3. Parse import-safe headers and all possible branch resources into BmsParseState.
4. At gameplay load, evaluate the active control-flow branch.
5. Parse active headers and channel lines into runtime events.
6. Merge duplicate channel data where required.
7. Build tick-based timing map and project native ticks to osu! runtime times during decode.
8. Infer key layout and map channels to columns.
9. Resolve long notes, including LNTYPE 1, LNTYPE 2, and LNOBJ.
10. Create BmsHitObject instances keyed by BMS tick and sort by tick/time.
11. Populate BmsBeatmap.TotalColumns.
12. Apply metadata to Beatmap.Metadata and difficulty fields where available.
```

## Worked Example: Basic 7K

Input:

```bms
#TITLE Basic 7K
#ARTIST Example
#BPM 120
#WAV01 kick.wav
#WAV02 snare.wav
#WAV03 hihat.wav
#00116:0100
#00111:0002
#00119:00000003
```

Pairs:

```text
#00116: 01 00       -> measure 1, fraction 0/2, scratch
#00111: 00 02       -> measure 1, fraction 1/2, key 1
#00119: 00 00 00 03 -> measure 1, fraction 3/4, key 7
```

Column mapping:

```text
16 -> 0
11 -> 1
19 -> 7
```

At 120 BPM, measure `000` spans ticks `0-192` and projects to 2000ms, so measure `001` starts at tick `192` / 2000ms.

Result:

```text
BmsHitObject(BmsTick=192, StartTime=2000, Column=0, IsLongNote=false, WAV01)
BmsHitObject(BmsTick=288, StartTime=3000, Column=1, IsLongNote=false, WAV02)
BmsHitObject(BmsTick=336, StartTime=3500, Column=7, IsLongNote=false, WAV03)
TotalColumns = 8
```

## Worked Example: Measure Length and BPM

Input:

```bms
#BPM 120
#BPM01 240
#00102:0.5
#00111:0101
#00208:01
#00211:0101
```

Timeline:

```text
measure 000 length 1 -> ticks 0-192 -> 2000ms at 120 BPM
measure 001 length 0.5 -> ticks 192-288 -> 1000ms at 120 BPM
measure 002 starts at tick 288 / 3000ms
measure 002 fraction 0/1 changes BPM to 240 before later tick segments
```

Notes:

```text
#00111 first 01 at measure 001 fraction 0/2 -> tick 192 -> 2000ms
#00111 second 01 at measure 001 fraction 1/2 -> tick 240 -> 2500ms
#00211 first 01 at measure 002 fraction 0/2 -> tick 288 -> 3000ms
#00211 second 01 at measure 002 fraction 1/2 -> tick 384 -> 3500ms at 240 BPM? No.
```

Explanation for the last note:

```text
At 240 BPM, a normal measure is 1000ms.
Fraction 1/2 of measure 002 is 96 ticks after measure start. At 240 BPM this projects to 500ms.
StartTime = 3500ms.
```

## Worked Example: STOP

Input:

```bms
#BPM 120
#STOP01 96
#00111:01
#00109:01
#00211:01
```

Without STOP:

```text
measure 001 start = 2000ms
measure 002 start = 4000ms
```

With STOP:

```text
#00111 note at 2000ms
#00109 stop at 2000ms adds 1000ms after same-tick objects
#00211 note at 5000ms
```

## Worked Example: `#LNTYPE 1`

Input:

```bms
#BPM 120
#LNTYPE 1
#WAV01 hold.wav
#00151:01
#00351:01
```

Channel `51` maps like visible channel `11`.

```text
measure 001 start = tick 192 / 2000ms
measure 003 start = tick 576 / 6000ms
duration = 384 ticks / 4000ms at 120 BPM
```

Result:

```text
BmsHitObject(BmsTick=192, EndTick=576, StartTime=2000, Column=1, IsLongNote=true, Duration=4000)
```

## Worked Example: `#LNOBJ`

Input:

```bms
#BPM 120
#LNOBJ ZZ
#WAV01 hold.wav
#WAVZZ empty.wav
#00111:01
#00311:ZZ
#00411:01
```

Result:

```text
#00111:01 opens hold
#00311:ZZ closes hold
#00411:01 is normal note unless followed by another ZZ in same lane
```

Create:

```text
BmsHitObject(BmsTick=192, EndTick=576, StartTime=2000, Column=1, IsLongNote=true, Duration=4000)
BmsHitObject(BmsTick=768, StartTime=8000, Column=1, IsLongNote=false)
```

## Error Handling Policy

Use tolerant parsing by default. A malformed line should not prevent importing the rest of a chart unless continuing would create corrupt timing.

Recommended warnings:

| Case | Action |
|---|---|
| Unknown header | Ignore. |
| Unknown channel | Ignore. |
| Visible/LN channel outside selected layout | Ignore and warn. |
| Invalid measure number | Ignore line. |
| Odd channel data length | Ignore trailing char. |
| Invalid base36 pair | Ignore that pair. |
| Undefined `#WAVxx` on note | Create note with no sample. |
| Undefined `#BPMxx` | Ignore BPM event and warn. |
| Undefined `#STOPxx` | Ignore STOP event and warn. |
| Zero or negative BPM | Clamp, reject chart, or warn and ignore event; choose one explicitly. |
| Unclosed long note | Ignore open hold and warn. |
| End before start due to timing issue | Ignore hold and warn. |

For tests, expose warnings or at least make them loggable.

## Test Cases to Add

### Minimal Note

```bms
#BPM 120
#WAV01 a.wav
#00111:01
```

Expected:

```text
one note, column 1, start 2000ms for normal measure 000
```

### Duplicate Merge

```bms
#00111:11111111
#00111:0022332255224400
#00111:0066
```

Expected object values by merged fraction:

```text
11 22 33 22 66 22 44 00
```

Do not create an object for final `00`.

### BGM No Merge

```bms
#WAV01 a.wav
#WAV02 b.wav
#00101:01
#00101:02
```

Expected:

```text
two BGM sample events at same time, zero playable notes
```

### Extended BPM

```bms
#BPM 120
#BPM01 240
#00108:01
#00111:0101
```

Expected:

```text
first note at measure 001 start
second note at half measure using 240 BPM segment
```

### STOP Same Tick

```bms
#BPM 120
#STOP01 96
#00111:01
#00109:01
#00211:01
```

Expected:

```text
first note unaffected by same-tick STOP
second note delayed by 1000ms
```

### `#LNTYPE 1`

```bms
#LNTYPE 1
#00151:01
#00251:01
```

Expected:

```text
one hold in column mapped from channel 51
```

### `#LNOBJ`

```bms
#LNOBJ ZZ
#00111:01
#00211:ZZ
```

Expected:

```text
one hold in column mapped from channel 11
terminator is not a normal note
```

### PMS

```bms
#00111:01
#00125:02
```

With `.pms` extension, expected:

```text
channel 11 -> column 0
channel 25 -> column 8
TotalColumns = 9
```

## Implementation Checklist

Use this order when replacing the placeholder decoder:

1. Add parser data structures for headers, definitions, raw channel events, warnings, and measure lengths.
2. Parse lines into headers/channel records without creating hit objects yet.
3. Implement base36 parsing and channel pair splitting.
4. Implement duplicate channel merge for all mergeable channels, including visible, LN, BPM, and STOP channels.
5. Implement `#BPM`, `#BPMxx`, channel `03`, channel `08`.
6. Implement `#STOPxx`, channel `09`.
7. Implement measure length channel `02`.
8. Build the tick timeline and tick-to-time projection for all raw events.
9. Implement SP/DP/PMS column mapping.
10. Create normal `BmsHitObject` instances from visible channels.
11. Implement `#LNTYPE 1` hold pairing.
12. Implement `#LNOBJ` hold pairing.
13. Implement `#LNTYPE 2` run-based hold resolution.
14. Preserve control-flow AST at import and select branches at runtime.
15. Preserve known tags/headers even when gameplay support is deferred.
16. Populate `BmsBeatmap.TotalColumns` from the inferred BMS layout.
17. Add tests for every case above.
18. Add warnings for unsupported but known features rather than silently failing.

## Notes Specific to This Repository

- Current code no longer extends mania through NuGet. Keep BMS-owned gameplay, objects, timing, gauge, samples, and BGA layers.
- Do not copy mania source or add an osu!mania implementation dependency. Reuse API ideas deliberately, but implement BMS behavior in BmsRuleset types.
- Keep `BmsHitObject` as the BMS-specific object. Add fields when the decoder needs to preserve BMS semantics for native BMS gameplay.
- BGM playback is implemented as a BmsRuleset sample event layer; do not fake BGM as playable notes.
- Arbitrary BMS audio filenames use a focused sample-resolution layer.
- `BmsBeatmapConverter.CreateBeatmap()` should preserve `BmsBeatmap.TotalColumns` and inferred layout directly.
