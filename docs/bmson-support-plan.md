# BMSON Support Plan

[Back to Development](development.md)

## Objective

Add native BMSON import and playback through a shared runtime chart model, preserving BMS behaviour and performance.
Both parsers should produce this model directly; BMSON must not be translated into synthetic BMS text.

Target the official BMSON 1.0 schema first. Detect and report legacy 0.21 files and player-specific extensions explicitly.

## Initial Scope

Initial support:

- BMSON 1.0 version detection and schema validation.
- Metadata, BPM changes, stops, bar lines, playable notes, long notes, background audio, and BGA.
- `beat-5k`, `beat-7k`, `beat-10k`, `beat-14k`, and `popn-9k` mode hints.
- Sound-channel slicing, continuation, restart, layered notes, and slice polyphony.
- Import summaries, star rating calculation, previews, gameplay seeking, and external resource loading.
- The same path traversal protections and global-volume routing rules as BMS resources.

Require explicit compatibility handling before accepting:

- BMSON 0.21 files without an explicit compatibility path.
- Unknown or extended `mode_hint` values.
- `generic-nkeys`, `popn-5k`, or layouts that cannot be represented by the current playfield and input model.
- Player-specific fields such as non-standard mine channels until their schema and compatibility behaviour are defined.

Unknown JSON properties may be ignored. Unsupported versions or layouts must produce an import diagnostic.

## Architectural Direction

Keep BMS measure/channel syntax and two-character `#WAVxx` identifiers inside the BMS parser:

```text
BMS text parser  ──┐
                   ├──> normalised chart data ──> decoded beatmap ──> playable beatmap
BMSON JSON parser ─┘
```

Timing, hit objects, audio, previews, BGA, difficulty calculation, and gameplay should consume normalised data.

## BMSON 1.0 Format Reference

This summary of the [BMSON specification](https://github.com/bemusic/bmson-spec) separates format requirements from
ruleset policy where the specification leaves behaviour to the player.

### Complete Object Shape

Example BMSON 1.0 document:

```json
{
  "version": "1.0.0",
  "info": {
    "title": "Example Song",
    "subtitle": "",
    "artist": "Example Artist",
    "subartists": [
      "chart:Example Charter",
      "movie:Example Animator"
    ],
    "genre": "Example Genre",
    "mode_hint": "beat-7k",
    "chart_name": "ANOTHER",
    "level": 12,
    "init_bpm": 150,
    "judge_rank": 100,
    "total": 100,
    "back_image": "background.png",
    "eyecatch_image": "eyecatch.png",
    "title_image": "title.png",
    "banner_image": "banner.png",
    "preview_music": "preview.ogg",
    "resolution": 240
  },
  "lines": [
    { "y": 0 },
    { "y": 960 }
  ],
  "bpm_events": [
    { "y": 1920, "bpm": 180 }
  ],
  "stop_events": [
    { "y": 2880, "duration": 240 }
  ],
  "sound_channels": [
    {
      "name": "keysounds/piano.ogg",
      "notes": [
        { "x": 1, "y": 0, "l": 0, "c": false },
        { "x": 2, "y": 240, "l": 480, "c": true },
        { "x": 0, "y": 960, "l": 0, "c": false }
      ]
    }
  ],
  "bga": {
    "bga_header": [
      { "id": 1, "name": "bga/main.webm" }
    ],
    "bga_events": [
      { "y": 0, "id": 1 }
    ],
    "layer_events": [],
    "poor_events": []
  }
}
```

JSON property order has no meaning. Array order matters for precedence and same-pulse events.

### Top-Level Fields

| Field | Type | Required/default | Meaning |
|-------|------|------------------|---------|
| `version` | string | Required for 1.0 | Semantic version of the BMSON format. The defined 1.0 value is `1.0.0`. |
| `info` | object | Required | Song, chart, gameplay, image, preview, and timing-resolution metadata. |
| `lines` | array or null | Optional | Explicit visual bar-line positions in pulses. Missing and empty have different meanings. |
| `bpm_events` | array or null | Optional | Tempo changes after the initial BPM. |
| `stop_events` | array or null | Optional | Additive scroll/time stops measured in pulses. |
| `sound_channels` | array | Required | Audio tracks and every playable or background note that uses them. |
| `bga` | object | Schema field | BGA resource declarations and base, layer, and poor event timelines. |

Readers may treat missing `bga` or BGA arrays as an empty timeline for compatibility. Writers should still include
these schema fields.

### Version Handling

`version` follows Semantic Versioning rules.

- `"version": "1.0.0"` is the initial supported format.
- A missing `version` identifies a legacy BMSON file, conventionally BMSON 0.21.
- A null `version` is an error.
- A syntactically invalid version is an error.
- Unsupported major or pre-1.0 versions must not be interpreted using the 1.0 field names or units.

Reject legacy and unsupported versions with a diagnostic identifying the version or its absence. Keep version
comparison in one parser component so compatibility policy can change independently of gameplay.

### Information Object

| Field | Type | Default | Ruleset mapping and notes |
|-------|------|---------|---------------------------|
| `title` | string | None stated | Song title. Do not infer chart difficulty by splitting title delimiters. |
| `subtitle` | string | Empty | Song subtitle. It is distinct from `chart_name` and may contain newlines. |
| `artist` | string | None stated | Primary displayed artist. Multiple names may already be combined in the string. |
| `subartists` | string array | Empty | Structured credits written as `role:name`; missing role means `other`. |
| `genre` | string | None stated | Song genre. |
| `mode_hint` | string | `beat-7k` | Canonical layout identifier. It is authoritative even when lane counts appear compatible. |
| `chart_name` | string | Empty | Difficulty/chart name such as `HYPER`, `ANOTHER`, or `INSANE`. |
| `level` | unsigned integer | None stated | Author-provided chart level; values are normally based on the target mode's scale. |
| `init_bpm` | number | None | Initial BPM. Its absence is a fatal format error. |
| `judge_rank` | number | `100` | Relative judgement-window width; interpretation is player-dependent. |
| `total` | number | `100` | Relative lifebar gain compared with the player's default. |
| `back_image` | string or null | Empty/null | Static gameplay background image. |
| `eyecatch_image` | string or null | Empty/null | Image shown while the song loads. |
| `title_image` | string or null | Empty/null | Image shown before gameplay starts. |
| `banner_image` | string or null | Empty/null | Song-select or result-screen banner, conventionally 15:4. |
| `preview_music` | string or null | Empty/null | Dedicated preview audio. If absent, preview may be synthesised from sound channels. |
| `resolution` | unsigned integer | `240` | Pulses per quarter note. Zero, null, or missing selects the default. |

Credit parsing rules:

- Split a `subartists` entry on its first `:` only.
- Trim whitespace around role and name.
- Recognised roles include `music`, `vocal`, `chart`, `image`, `movie`, and `other`.
- An entry without a role becomes `other`.
- Preserve unknown roles as data rather than discarding the credit.

Numeric normalisation from the specification:

- `level` is expected to be non-negative.
- `total == 0` means successful judgements do not increase the lifebar.
- A negative `total` is interpreted using its absolute value.
- `resolution == 0`, null, or missing uses `240`.
- Use the absolute value of a negative resolution, as specified, and warn because the schema declares it unsigned.

The ruleset should also reject non-finite numbers and non-positive BPM. BMSON 1.0 does not define useful behaviour
for non-positive BPM; do not apply BMS reverse-scroll semantics.

### Pulse Coordinates

BMSON uses absolute pulse positions rather than BMS measure/channel fractions.

- `resolution` is the number of pulses in one quarter note.
- With the default resolution of 240, `y = 240` is one quarter note after `y = 0`.
- A 4/4 bar has `4 * resolution` pulses, or 960 pulses at the default resolution.
- Note position `y`, long-note length `l`, STOP `duration`, bar lines, BPM events, and BGA events all use pulses.
- Positions and lengths are non-negative integral values in the 1.0 schema.

Ignoring STOP durations, a pulse delta converts to metric time as:

```text
milliseconds = pulse_delta × 60000 / bpm / resolution
```

Absolute chart time also includes every STOP before the target pulse. A note or BGA event at the same pulse as a STOP
uses the time at the beginning of that STOP.

### Bar Lines

Each `lines` entry has one field:

| Field | Type | Meaning |
|-------|------|---------|
| `y` | unsigned integer | Absolute pulse at which a bar line may be displayed. |

Distinguish three states:

- Missing or null `lines`: assume regular 4/4 bar lines every `4 * resolution` pulses.
- Empty `lines`: the chart has no bar lines.
- Populated `lines`: use the specified positions; irregular spacing simulates time-signature or measure-length changes.

The line at `y = 0` is optional, and readers may choose whether to display it. Bar lines affect only appearance,
not note timing, BPM, STOP duration, or pulse coordinates.

### BPM Events

Each `bpm_events` entry contains:

| Field | Type | Meaning |
|-------|------|---------|
| `y` | unsigned integer | Absolute pulse at which the BPM becomes active. |
| `bpm` | number | New tempo in beats per minute. |

Timing starts at `info.init_bpm`. For BPM events sharing `y`, the last array entry wins; preserve source order until
these ties are resolved.

Example:

```json
"bpm_events": [
  { "y": 240, "bpm": 100 },
  { "y": 240, "bpm": 120 }
]
```

The effective BPM after pulse 240 is 120.

### STOP Events

Each `stop_events` entry contains:

| Field | Type | Meaning |
|-------|------|---------|
| `y` | unsigned integer | Absolute pulse at which scrolling/time pauses. |
| `duration` | unsigned integer | Pause length expressed as a number of pulses. |

Unlike same-pulse BPM events, same-pulse STOP durations add together.

```json
"stop_events": [
  { "y": 240, "duration": 240 },
  { "y": 240, "duration": 960 }
]
```

This produces a 1200-pulse STOP, converted to milliseconds using the BPM after any changes at pulse 240.

### Same-Pulse Processing Order

Process events at the same pulse in this order:

1. Notes and BGA events activate.
2. BPM events apply, with the last same-pulse BPM becoming active.
3. STOP events apply, with same-pulse durations added together.

Consequences:

- A playable note at a STOP pulse is judged at the beginning of the pause.
- A BGM slice at a STOP pulse starts before the pause elapses.
- A STOP at the same pulse as a BPM event uses the new BPM when converting its duration to milliseconds.
- Events after that pulse include the STOP duration in their absolute metric time.

### Sound Channels

A sound channel is one logical audio track with all chart events that use it:

```json
{
  "name": "audio/vox.ogg",
  "notes": [
    { "x": 1, "y": 240, "l": 0, "c": false }
  ]
}
```

| Field | Type | Meaning |
|-------|------|---------|
| `name` | string | Audio resource path for this logical channel. |
| `notes` | array | Playable and background notes that also define the channel's slice boundaries. |

Sound channels referencing the same file remain independent and may overlap, like multiplex BMS WAV definitions.

Resource lookup rules from the specification:

- The filename extension may be omitted; the reader should try compatible audio extensions.
- If a named extension cannot be loaded, retry as though the extension were omitted.
- Paths may use `/` or `\` and may refer to subdirectories below the chart directory.
- Absolute paths, parent-directory traversal, null characters, and resolved paths outside the chart directory must be
  rejected.

Players are expected to support WAV and either OGG Vorbis or MP4 AAC/M4A. MP3 encoder/decoder delay can affect keysound
timing. Additional formats are allowed, but reference fixtures should use the expected lossless or gap-safe formats.

Share `BmsSampleInfo` fallback lookup and `BmsFileResourceStore` containment checks between BMS and BMSON.

### Notes

Each sound-channel note has:

| Field | Type | Default policy | Meaning |
|-------|------|----------------|---------|
| `x` | any | Null/BGM | Player lane. Numeric zero or null is background audio; positive values are playable lanes. |
| `y` | unsigned integer | Required for useful data | Absolute activation pulse. |
| `l` | unsigned integer | `0` | Length in pulses. Zero is a short note; positive values create a long note ending at `y + l`. |
| `c` | boolean | `false` | Continuation flag. False restarts source audio; true continues its source position. |

Initially accept only null or integral numeric `x`, despite the schema's `any` type. Other values require a mode
extension and should produce an unsupported-lane diagnostic.

`l` controls gameplay note duration. Audio slice boundaries come from note pulses across the entire sound channel.

### Sound Slicing

Each distinct note pulse defines a slice boundary. Calculate slices per sound-channel object, independently of filenames
and lanes.

For one sound channel:

1. Group notes by `y` while preserving their source order.
2. Sort the groups by pulse.
3. Convert each boundary pulse to absolute metric chart time, including intervening BPM changes and STOP durations.
4. Before opening the next slice, close the previous slice after the metric time elapsed between the two boundaries.
5. Start the new slice at source offset zero if any note in the group has `c == false`.
6. Otherwise start it at the previous slice's calculated end offset.
7. Assign every note in the group to that new slice; the final slice extends to end of file.

Mixed `c == true` and `c == false` at one pulse restart the source. All notes in that group share the resulting slice.

Example at BPM 120:

```json
{
  "name": "vox.wav",
  "notes": [
    { "x": 1, "y": 240,  "c": false },
    { "x": 3, "y": 360,  "c": true  },
    { "x": 2, "y": 720,  "c": false },
    { "x": 4, "y": 840,  "c": true  },
    { "x": 3, "y": 1200, "c": true  }
  ]
}
```

| Boundary pulse | Chart time | Restart | Source slice start | Source slice end |
|----------------|------------|---------|--------------------|------------------|
| 240 | 0.50 s | Yes | 0.00 s | 0.25 s |
| 360 | 0.75 s | No | 0.25 s | 1.00 s |
| 720 | 1.50 s | Yes | 0.00 s | 0.25 s |
| 840 | 1.75 s | No | 0.25 s | 1.00 s |
| 1200 | 2.50 s | No | 1.00 s | End of file |

The two slices starting at source offset zero have distinct identities because they start at different chart boundaries.
Their end offsets and playback lifetimes may differ.

### Slice Playback Rules

- Each slice has polyphony one.
- Simultaneous triggers of the same slice must not increase its volume.
- A later trigger of the same slice restarts or truncates that slice's existing voice.
- Different slices from the same sound channel may overlap.
- Different sound channels referring to the same file remain independent and may overlap.
- If one slice is assigned to both a playable note and a BGM note at the same pulse, discard the BGM use.

The specification permits merging consecutive BGM-only slices when the later slice continues the source. Defer this
optimisation until tests cover independent-slice playback.

### Layered Notes

Notes from different sound channels at the same `(x, y)` are fused into one gameplay note:

- The resulting hit object plays one slice from each contributing sound channel.
- It is not multiple judgement objects in the same lane.
- All contributing notes are expected to have equal `l` values.
- Unequal lengths are an error; a reader may warn and choose a deterministic recovery policy.

Initially, keep the first length in source order, attach all slices, and report conflicting lengths. Never create
duplicate judgement objects in one lane to recover from invalid input.

### Canonical Mode Hints

`mode_hint` defines `x`. Generic keyboard and beatmania modes may share a lane count but use different controls.

| `mode_hint` | `x = 1`-`5` | `x = 6`-`7` | `x = 8` | `x = 9`-`13` | `x = 14`-`15` | `x = 16` |
|-------------|-------------|-------------|---------|--------------|---------------|----------|
| `beat-5k` | P1 keys 1-5 | Unused | P1 scratch | Unused | Unused | Unused |
| `beat-7k` | P1 keys 1-5 | P1 keys 6-7 | P1 scratch | Unused | Unused | Unused |
| `beat-10k` | P1 keys 1-5 | Unused | P1 scratch | P2 keys 1-5 | Unused | P2 scratch |
| `beat-14k` | P1 keys 1-5 | P1 keys 6-7 | P1 scratch | P2 keys 1-5 | P2 keys 6-7 | P2 scratch |

Pop'n modes use consecutive lanes:

| `mode_hint` | Playable `x` values |
|-------------|---------------------|
| `popn-5k` | `1`-`5` |
| `popn-9k` | `1`-`9` |

Generic keyboard layouts use names such as `generic-8keys`, ordered left to right. They require a dynamic layout model
and are outside the first implementation scope.

Map beat-mode scratches to the runtime scratch columns; `x` is not a runtime column index.

### BGA Object

The BGA object contains four arrays:

| Field | Entry type | Meaning |
|-------|------------|---------|
| `bga_header` | `{ id, name }` | Declares image or video resources. |
| `bga_events` | `{ y, id }` | Base BGA timeline. |
| `layer_events` | `{ y, id }` | Overlay timeline above the base BGA. |
| `poor_events` | `{ y, id }` | Timeline displayed in response to misses. |

`BGAHeader` fields:

| Field | Type | Meaning |
|-------|------|---------|
| `id` | unsigned integer | Resource identifier referenced by events. |
| `name` | string | Image or video resource path. |

`BGAEvent` fields:

| Field | Type | Meaning |
|-------|------|---------|
| `y` | unsigned integer | Activation pulse. |
| `id` | unsigned integer | Resource identifier from `bga_header`. |

For duplicate header IDs, the last declaration wins and may produce a warning. Undefined event IDs should produce
a missing-resource diagnostic while allowing the chart to play.

The specification recommends PNG images and WebM video; video audio may be ignored. BMSON does not make black pixels
transparent. Transparency requires an alpha-capable resource such as PNG.

### Legacy BMSON 0.21 Differences

A document without `version` is legacy BMSON. Changes from 0.21 to 1.0 include:

| Legacy form | BMSON 1.0 form or change |
|-------------|--------------------------|
| Camel-case fields | Snake-case fields |
| `soundChannel` | `sound_channels` |
| `judgeRank` | `judge_rank` |
| `initBPM` | `init_bpm` |
| `bgaHeader` | `bga_header` |
| `bgaNotes` | `bga_events` |
| `layerNotes` | `layer_events` |
| `poorNotes` | `poor_events` |
| `ID` | `id` |
| `bpmNotes` | `bpm_events` |
| `stopEvents` | `stop_events` |
| Shared event value `v` | Separate `bpm` and `duration` fields |
| Legacy time units | 1.0 pulse-based positions and durations |
| Legacy `total` semantics | 1.0 relative percentage semantics |

Changed units and gauge semantics require more than field aliases. A future 0.21 reader needs separate DTOs,
a normalisation adapter, and dedicated fixtures.

### Parser Validation Summary

| Condition | Initial implementation action |
|-----------|-------------------------------|
| Missing, null, invalid, legacy, or unsupported `version` | Reject with version diagnostic. |
| Missing `info` or `sound_channels` | Reject as structurally invalid. |
| Missing `init_bpm` | Reject as required by the specification. |
| Non-finite or zero BPM | Reject; negative BPM remains unsupported in BMSON phase one. |
| Missing, null, or zero `resolution` | Use 240. |
| Negative `resolution` or `total` | Use absolute value and emit a warning. |
| Negative or non-integral pulse/lane/length/ID | Reject the affected structure or chart with a precise diagnostic. |
| Unsupported `mode_hint` | Reject instead of guessing a layout. |
| Unknown JSON property | Ignore, optionally record informational diagnostic. |
| Duplicate same-pulse BPM | Last array entry wins. |
| Duplicate same-pulse STOP | Add durations. |
| Duplicate BGA header ID | Last array entry wins and warn. |
| Layered-note length disagreement | Keep first source-ordered length and warn. |
| Missing audio or BGA resource | Continue with silent/missing resource and log the path. |
| Unsafe resource path | Reject the resource lookup and never access outside the chart directory. |

## Required Model Changes

### Sample Identity and Clip Definitions

Replace two-character BMS sample keys with runtime IDs for playable audio slices:

```csharp
public readonly record struct BmsSampleId(int Value);

public sealed record BmsSampleDefinition(
    string Path,
    double StartOffset = 0,
    double? EndOffset = null);

public readonly record struct BmsSampleTrigger(
    BmsSampleId SampleId,
    int Volume = 100);
```

BMS definitions start at zero and have no end offset. BMSON offsets come from each channel's restart/continuation sequence.

Reuse one `BmsSampleId` per slice to enforce [polyphony rules](#slice-playback-rules). Different slices need distinct IDs
so they can overlap, even within one source channel.

Update these existing models to use `BmsSampleId` and `BmsSampleDefinition`:

- `BmsParseResult.SampleDefinitions`
- `IBmsBeatmap.SampleDefinitions`
- `BmsSampleEvent`
- `BmsSampleUsage`
- `BmsPreviewTimelineEntry`
- `BmsBackgroundAudioPlayer.BgmEvent`
- `BmsPcmPlaybackController` dictionaries and command queues

### Layered Hit Object Samples

Replace the single `BmsHitObject.SampleKey` with a collection:

```csharp
public IReadOnlyList<BmsSampleTrigger> Samples { get; set; } = [];
```

Replace `BmsLongNote.TailSampleKey` with:

```csharp
public IReadOnlyList<BmsSampleTrigger> TailSamples { get; set; } = [];
```

Fuse notes at the same playable `(x, y)` into one hit object containing all slices in `Samples`.
Handle conflicting long-note lengths using the [layered-note policy](#layered-notes).

Background audio may remain one `BmsSampleEvent` per sample trigger. Multiple events at the same time are valid.

### Format-Neutral Columns

The converter should use parser-provided `TotalColumns`, `LayoutVariant`, and `Column`. Layout inference and remapping
must no longer depend on `SourceChannel` after parsing. Retain BMS source information only as optional metadata for
diagnostics and tests.

### Timing Resolution and Bar Lines

Adopt pulses per quarter note as the public timing resolution:

```csharp
public int PulsesPerQuarter { get; }
```

Timing conversion should use:

```csharp
pulses * 60000 / bpm / PulsesPerQuarter
```

`TickResolution` currently uses ticks per four-quarter-note measure and divides by four during conversion.
Replace this convention throughout the timing map or isolate it in a named legacy adapter. Convert `info.resolution`
before passing it to the existing property.

Separate visual bar lines from timing projection:

```csharp
public IReadOnlyList<long> BarLines { get; }
```

Preserve the [distinct meanings of missing, empty, and populated `lines`](#bar-lines).
`BmsMeasureInfo` may remain inside the BMS parser during migration; gameplay should render the normalised collection.

### Normalised Judgement and Gauge Values

Add format-neutral gameplay values to the decoded beatmap:

```csharp
public double DefaultJudgementRate { get; set; }

public double GaugeTotal { get; set; }
```

Consumers should use normalised values; BMS `#RANK`, `#DEFEXRANK`, and `#TOTAL` may remain as source metadata.
Per-object judgement-rate events still override the chart default.

BMSON values should be converted as follows:

```text
default judgement rate = normal layout rate × judge_rank / 100
gauge total             = default BMS total for note count × total / 100
```

This avoids treating BMSON's relative `total: 100` as an absolute BMS `#TOTAL 100`.

### Metadata

Keep song subtitle and chart name separate. Add at least:

```csharp
public string? ChartName { get; init; }

public string? FormatVersion { get; init; }

public string? ModeHint { get; init; }

public IReadOnlyList<BmsCredit> Credits { get; init; } = [];
```

```csharp
public sealed record BmsCredit(string Role, string Name);
```

Use `info.subtitle` for the song subtitle and `info.chart_name` for difficulty. Preserve `subartists` as role/name pairs
until writing osu! metadata.

Use format-neutral image names in the normalised model where practical:

- `back_image` -> background image
- `eyecatch_image` -> panel or stage image
- `title_image` -> pre-game title image
- `banner_image` -> banner image
- `preview_music` -> dedicated preview audio

### BGA Identifiers

Widen BGA identifiers from `ushort` to a 32-bit type or opaque `BmsBgaId` to accommodate BMSON `bga_header.id`.

The existing layer model is sufficient for the first implementation:

- `bga_events` -> `BmsBgaLayer.Base`
- `layer_events` -> `BmsBgaLayer.Layer1`
- `poor_events` -> `BmsBgaLayer.Poor`

### Source Format and Materialisation

Add a source-format discriminator:

```csharp
public enum BmsChartFormat
{
    Bms,
    Bmson,
}
```

Restrict `RawLines` to `SourceFormat == Bms`, preferably renaming it `RawBmsLines`. Only BMS materialises runtime
branches; BMSON reuses deterministic decoded data.

## Implementation Phases

### Phase 0: Compatibility Fixtures

Before changing runtime types:

1. Add small, redistributable BMSON 1.0 fixtures covering each supported mode hint.
2. Add focused fixtures for slicing, continuation, restart, layered notes, long notes, BPM, STOP, and bar lines.
3. Add invalid fixtures for missing `init_bpm`, unsupported versions, unsupported modes, path traversal, and malformed
   note data.
4. Record expected object counts, timings, sample slices, metadata, and BGA events.

Keep fixtures small enough for unit tests, using generated tones or existing test audio.

### Phase 1: Generalise the Runtime Model

1. Introduce sample IDs, clip definitions, and sample-trigger collections.
2. Migrate the BMS parser to populate the new types without changing behaviour.
3. Remove converter dependence on `SourceChannel` for already-parsed charts.
4. Add normalised judgement, gauge, bar-line, metadata, and source-format fields.
5. Widen BGA IDs.
6. Update copy methods, mods, preview construction, and test beatmap builders.

Acceptance criteria:

- Existing BMS decoder tests retain the same expected behaviour.
- Existing keysound, BGM, preview, seek, BGA, judgement, and gauge tests pass.
- No BMSON parser is required yet.

### Phase 2: Add Clip-Aware Audio Playback

1. Load tracks by `BmsSampleId` and resolve their `BmsSampleDefinition`.
2. Start playback at `StartOffset` plus any seek-resume offset.
3. Stop playback at `EndOffset` without allowing the following slice to leak through.
4. Report clip-relative track length to BGM and preview seek reconstruction.
5. Preserve the existing rule that equal sample IDs truncate/restart while different IDs can overlap.
6. Apply background and keysound volume through the existing music-volume route, not global effect volume.
7. Update prefetch lifetimes and sample usage collection for multiple triggers per hit object.

Acceptance criteria:

- A clip never plays outside its source range.
- Seeking into a background clip resumes at the correct source offset.
- Two different clips from the same file can overlap.
- Two simultaneous triggers of the same clip do not double its volume.

### Phase 3: Implement the BMSON Reader

Place BMSON JSON DTOs in a dedicated namespace or parser folder, separate from the normalised model.

1. Parse JSON with explicit numeric validation and cancellation where import paths require it.
2. Validate `version` using semantic-version rules and initially accept compatible 1.0 files only.
3. Require `info.init_bpm`; default a missing, null, or zero `info.resolution` to 240 as specified.
4. Preserve array order for events at the same pulse.
5. Parse metadata and resource names without resolving files during the pure parse phase.
6. Ignore unknown properties but collect diagnostics for unsupported known extensions.
7. Reuse `BmsFileResourceStore` containment checks for every chart-controlled resource path.

The JSON DTOs should not escape into gameplay, preview, or rendering code.

### Phase 4: Map Timing and Layout

1. Create the initial BPM event at pulse zero from `info.init_bpm`.
2. Apply same-pulse BPM events in array order, with the last BPM becoming active.
3. Add same-pulse STOP durations and calculate them using the BPM active at that pulse.
4. Preserve BMSON processing order: note/BGA, then BPM, then STOP.
5. Build the explicit, omitted, or empty bar-line timeline correctly.
6. Map supported `mode_hint` lane numbers into internal columns, including scratch relocation.

Initial mode mappings:

| `mode_hint` | BMSON lanes | Runtime layout |
|-------------|-------------|----------------|
| `beat-5k` | `1`-`5`, scratch `8` | `Bms5K` |
| `beat-7k` | `1`-`7`, scratch `8` | `Bme7K` |
| `beat-10k` | keys `1`-`5` and `9`-`13`, scratches `8` and `16` | `Bms5KDouble` |
| `beat-14k` | keys `1`-`7` and `9`-`15`, scratches `8` and `16` | `Bme7KDouble` |
| `popn-9k` | `1`-`9` | `Pms9K` |

Reject unsupported modes with a diagnostic; never fall back to BMS 5K.

### Phase 5: Build Sound Slices and Hit Objects

For each sound channel:

1. Sort notes by pulse while preserving source order for ties.
2. Collect distinct pulse positions as slice boundaries.
3. Convert boundary pulses to metric chart time.
4. Reset the source audio position when a boundary contains any note with `c == false`.
5. Continue source audio position when all notes at that boundary use `c == true`.
6. Create one sample definition per resulting slice.
7. Assign the same sample ID to every note using that slice.

After slicing all channels:

1. Group playable notes by internal `(column, pulse)`.
2. Fuse their sample triggers into one short or long hit object.
3. Warn and follow the documented policy when fused long-note lengths disagree.
4. Emit `x == 0` or null notes as background sample events.
5. Discard a background use when the same slice is also assigned to a playable note at that pulse.
6. Sort the final objects and background events deterministically.

Build BMSON long notes from `l > 0`, without applying BMS `LNOBJ` rules.

### Phase 6: Import and Decoder Integration

1. Add `.bmson` to chart discovery without changing BMS grouping behaviour.
2. Dispatch by extension plus validated content rather than registering every JSON document through a broad `{` magic
   header.
3. Add a BMSON import-summary path that reuses the normalised note/timing output.
4. Preserve raw-byte MD5 hashing for duplicate detection and difficulty-table compatibility.
5. Store the original source directory using the existing external-resource workflow.
6. Decode BMSON from the external chart path in `BmsWorkingBeatmap`.
7. Ensure BMS runtime random branches continue to re-materialise, while BMSON remains deterministic.
8. Report unsupported version, mode, and schema errors through localised import notifications or detailed logs.

Add new user-facing diagnostics to the English resource and every supported `.resx` translation together.

### Phase 7: Metadata, BGA, and Preview Completion

1. Map `chart_name` to difficulty name without combining it with song subtitle.
2. Map credits, genre, level, images, banner, and preview audio.
3. Convert BMSON BGA headers and events to the existing BGA timeline.
4. Verify PNG alpha is preserved for layer events; do not apply legacy BMS black-pixel transparency rules to BMSON.
5. Generate previews from all playable and background sample triggers when dedicated preview audio is absent.
6. Verify background selection order for background, eyecatch, and banner images.

### Phase 8: Validation and Documentation

1. Add unit tests for every compatibility fixture and failure mode.
2. Add audio tests for clip endpoints, overlap, polyphony, pause, rate changes, and seek reconstruction.
3. Add decoder tests for layered notes, long notes, metadata, layouts, timing order, and BGA.
4. Add importer tests for `.bmson` discovery, mixed BMS/BMSON directories, duplicate MD5s, and invalid charts.
5. Add a visual smoke test for each supported layout and a chart containing layered audio plus BGA.
6. Document supported BMSON version, modes, fields, and known extensions in the format-support documentation.
7. Remove BMSON from the "Not Yet Implemented" table only after all acceptance criteria are satisfied.

## Focused Test Commands

Use filtered tests as each phase lands; do not run the unfiltered suite or benchmarks. For example:

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests --filter "FullyQualifiedName~Bmson"
dotnet test osu.Game.Rulesets.BmsRuleset.Tests --filter "FullyQualifiedName~BmsSamplePlayback"
dotnet test osu.Game.Rulesets.BmsRuleset.Tests --filter "FullyQualifiedName~BmsBeatmapDecoder"
```

Also run filtered timing, preview, BGA, gauge, and import tests affected by the model migration before completion.

## Expected Change Areas

- `BmsParser` for the normalised chart model, timing conventions, and BMS adapter.
- A new BMSON parser namespace for JSON DTOs, validation, mapping, and diagnostics.
- `Beatmaps` for decoded/playable model changes and format dispatch.
- `Media/Audio/Samples` for clip-aware sample definitions and playback.
- `Media/Audio/Playback` and `Media/Audio/Preview` for multiple triggers and clip-relative seeking.
- `IO/Import` and `Constant.cs` for `.bmson` discovery and import summaries.
- `UI/Components/BmsColumnKeySound.cs` for layered keysound playback.
- BGA models for wider identifiers.
- Mods that copy or synthesize hit objects and sample events.
- Normal and visual tests plus small BMSON fixtures.

## Risks and Mitigations

### Regressing Existing BMS Audio

Migrate the runtime model before adding the BMSON parser. Keep BMS samples as full-file clips and pass the existing
audio tests before adding slicing.

### Excessive Track Count

A few source files can produce many slices. Keep lazy loading and usage-based prefetching, measure real charts before
adding cache complexity, and avoid eagerly decoding slices into separate files.

### Ambiguous Community Extensions

Validate versions and modes strictly. Require explicit detection and dedicated fixtures for each extension.

### Layout Assumptions

Reject unsupported modes initially. Future generic layouts need descriptors for lane role, player side, scratch status,
input action, and skin lookup; `TotalColumns` alone is insufficient.

### Timing Unit Confusion

Use `Pulse` and `PulsesPerQuarter` in new APIs and tests. Isolate conversion to legacy ticks-per-measure in one adapter,
then remove it after migration.

## Definition of Done

BMSON support is complete when:

- Official BMSON 1.0 charts in every supported mode import and play without conversion to BMS text.
- Sound slicing, continuation, restart, layered notes, polyphony, preview, and seek behaviour match the specification.
- BPM, STOP, note, BGA, and bar-line timing match pulse-level fixture expectations.
- `judge_rank` and `total` produce the intended relative judgement and gauge behaviour.
- Unsupported versions, layouts, extensions, and unsafe resource paths fail predictably with useful diagnostics.
- Existing BMS behaviour and focused regression tests remain unchanged.
- Format-support documentation states the supported version, modes, fields, and limitations.
