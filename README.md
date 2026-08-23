<h1 align="center">osu! BMS Ruleset</h1>

<p align="center"><strong>Play BMS-family charts natively in osu!lazer</strong></p>

<p align="center">
  <a href="https://github.com/QingQiz/BmsRuleset/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/QingQiz/BmsRuleset?label=release&color=ff66aa"></a>
  <a href="./LICENSE"><img alt="License: AGPL-3.0" src="https://img.shields.io/github/license/QingQiz/BmsRuleset?color=5c7cfa"></a>
</p>

<p align="center">
  <a href="https://github.com/QingQiz/BmsRuleset/releases"><strong>Download</strong></a>
  · <a href="./docs/gameplay.md">Gameplay guide</a>
  · <a href="./docs/skin-system.md">Skin guide</a>
  · <a href="./README.zh-CN.md">中文</a>
</p>

Bring traditional BMS gameplay into the familiar osu! interface: keysound-based audio, BGA, authentic judgements,
six gauge types, difficulty tables, clear lamps, and detailed performance analysis.

<p align="center">
  <img src="https://github.com/user-attachments/assets/816fb844-e684-48d0-8c37-0217fdd2d9b2" width="900" alt="BMS gameplay running natively in osu!lazer">
  <br>
  <sub>Native BMS gameplay in osu!lazer, shown with the developer's customizable skin</sub>
</p>

## Installation

1. Download `osu.Game.Rulesets.BmsRuleset.dll` from the
   [latest release](https://github.com/QingQiz/BmsRuleset/releases). Choose a release whose `xxxx.yyy` version
   matches your installed osu!lazer version.
2. In osu!, open **Settings → General → Open osu! folder**, then place the DLL in the `rulesets` folder. Create the
   folder if it does not exist.
3. Restart osu!. **BMS** will appear in the ruleset selector.

## Importing BMS Charts

1. Open osu!'s settings and find the **BMS** section.
2. Select **Import BMS files**.
3. Navigate to a folder containing BMS charts.
4. Import the selected chart, every chart in the current folder, or an entire directory recursively.
5. Wait for the imported charts to appear in song select.

<p align="center">
  <img src="https://github.com/user-attachments/assets/62ff2f6d-a74a-4114-a90f-9b71d13713ca" width="900" alt="BMS chart import screen">
  <br>
  <sub>Select a chart, import the current folder, or scan a directory recursively.</sub>
</p>

> [!IMPORTANT]
> Only chart data is imported into osu!'s database. Audio, images, and videos remain in the original BMS folder and
> are loaded from there during gameplay. Do not move or delete the source folder after importing it.

If a source folder has been moved or removed, use **Clean up orphaned BMS sets** in the BMS settings. You can also
remove all imported BMS metadata without affecting the original files.

## Highlights

From BPM and STOP changes to long notes, mines, random branches, scroll changes, BGA, and keysounds, the ruleset is
built around the parts of BMS that shape how a chart feels to play. Judgements use beatoraja-compatible timing windows,
with traditional EX score alongside osu!'s normalized score display.

It also adds BMS-focused workflows that standard osu! does not provide: previews synthesized from chart audio,
difficulty-table collections and markers, mod-aware clear lamps, and detailed analysis of saved scores without first
watching their replays.

### osu!mania Skin Support

Start with the osu!mania skin you already use, add a dedicated `[BMS]` configuration when you want finer control, or
rely on the built-in fallback skin. Common single-play and double-play layouts are supported, including 5-key, 7-key,
and 9-key charts.

For example, BMS 7K (seven keys plus scratch) selects its skin configuration in this order:

1. `[BMS]` with `Layout: 7K`
2. `[Mania]` with `Keys: 8` and `SpecialStyle: 1`
3. `[Mania]` with `Keys: 8`
4. `[Mania]` with `Keys: 7`
5. The ruleset's built-in fallback skin

> [!NOTE]
> The initial layout may look cluttered because BMS-specific components are shown alongside components from the
> osu!mania skin. For example, two hit error meters may be visible. Open the skin editor to arrange or remove the
> overlapping components.

See the [skin system guide](./docs/skin-system.md) for the complete `skin.ini` reference and layout-specific behavior.

### Visually Editable Skin Components

Arrange, resize, and configure the playfield, combo, judgement display, hit error meter, gauge, song progress, BGA,
chart messages, and live score comparison directly through osu!'s visual skin editor.

<p align="center">
  <img src="https://github.com/user-attachments/assets/c3690c49-3a00-4970-abd8-0e93220d6f11" width="900" alt="BMS components in the osu! visual skin editor">
  <br>
  <sub>Build a gameplay layout visually instead of editing component positions by hand.</sub>
</p>

Layouts are saved per skin without modifying the chart or its `skin.ini`. See
[Skin Components](./docs/skin-system.md#skin-components) for every editable component and option.

### Song Preview

**Charts do not need a pre-rendered audio file to have a song-select preview.** The ruleset tries `#PREVIEW`, then
`preview.*`, and finally synthesizes a preview directly from the chart's BGM and keysound timeline. Charts that would
otherwise be silent in song select can still be heard before playing.

Disable **Use dedicated preview audio** in the BMS settings to always use the BGM and keysound timeline.

### Visual Offset Calibration

Adjust when notes reach the judgement line without shifting audio, judgement timing, scoring, or keysound playback.
Positive visual offsets display notes earlier and suit players who receive more Slow judgements. Negative values
display notes later and suit players who receive more Fast judgements.

Valid local plays contribute visual-offset suggestions based on their median hit error. The BMS settings show recent
suggestions and can apply their average manually, or automatically apply each new suggestion after a play. Replays,
automatic play, and plays with fewer than 50 timed hits are excluded from calibration.

### Uncapped BMS Frame Rate

Enable **Unlock frame rate limit** in the BMS settings to remove osu!'s 1000 Hz cap from BMS rendering, updates, and
input polling during BMS gameplay.

**Side effects:** Higher GC and GPU pressure may cause extra stutters. Disable this option if that happens.

### Clear Lamps

Song select shows your best matching local clear status, from No Play and Failed through Assist Easy, Easy, Normal,
Hard, and EX Hard clears to Full Combo, Perfect, and Max. Lowering the difficulty keeps lamps earned under harder
conditions visible, while raising it hides lamps earned under easier conditions. Only Double Time is treated as a
difficulty increase for lamp filtering.

### Difficulty Tables

**Use the LR2/beatoraja table-based discovery workflow directly inside osu!** Import a table from a URL or local JSON
file; charts are matched by MD5, marked with their table levels, and collected automatically for browsing. Well-known
tables including Satellite, Stella, Insane BMS, Overjoy, Scramble, and Luminous are available as presets.

<p align="center">
  <img src="https://github.com/user-attachments/assets/29b2c98c-0293-4b39-8c08-7341108476d9" width="900" alt="Clear lamps and difficulty table markers in song select">
  <br>
  <sub>Clear lamps and imported difficulty-table markers appear directly in song select.</sub>
</p>

Remote tables can be updated from their source. Any table can be subdivided into per-level collections, merged again,
or removed from the BMS settings. See the [difficulty table guide](./docs/difficulty-tables.md) for the complete workflow.

### Course Mode

Difficulty tables can also provide BMS courses through their `course` definitions. Imported courses appear in the BMS
course mode with their stages in the declared order, using the course gauge and constraints from the table. Course
results are saved locally and shown alongside the course cards in song select.

Pausing is prohibited in course mode. The maximum wait between two songs is 99 seconds; if exceeded, the next song starts automatically.

### Result Screen

**Open a saved score and inspect its detailed result analysis immediately, without watching the replay first.** This is
available directly for previously saved scores, not only for the play session that just ended.

The result screen goes beyond the standard osu! score summary with gauge history, note and judgement timelines,
fast/slow distribution, hit scatter and offset graphs, and per-key timing breakdowns.

<p align="center">
  <img src="https://github.com/user-attachments/assets/38f62e99-8977-47eb-a0bc-d87837714905" width="900" alt="BMS result screen with detailed performance analysis">
  <br>
  <sub>Inspect where a run was lost, down to timing direction and individual keys.</sub>
</p>

### osu!mania 7K Conversion

Official osu!mania 7K charts can be played as BMS. The seven lanes map to the BME key columns with scratch left empty;
note timing, holds, BPM, time signatures, offsets, and scroll-velocity changes are preserved. Other key counts are not supported yet.
To enable conversion, select the BMS ruleset and turn on **Show converts** in the song select filter panel.

## Documentation

The README stays focused on getting started. Detailed behavior and technical references live in:

| Guide                                              | Contents                                                               |
|----------------------------------------------------|------------------------------------------------------------------------|
| [Gameplay and settings](./docs/gameplay.md)        | Judgements, scoring, gauges, mods, key bindings, and settings          |
| [Difficulty tables](./docs/difficulty-tables.md)   | Importing and managing LR2/beatoraja difficulty tables                 |
| [Skin system](./docs/skin-system.md)               | `skin.ini` reference, osu!mania compatibility, and editable components |
| [BMS format support](./docs/bms-format-support.md) | Parser behavior, supported commands and channels, and reference links  |
| [Development](./docs/development.md)               | Building from source, missing features, and known issues               |
