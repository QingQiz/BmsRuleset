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

Supports `.bms`, `.bme`, `.bml`, and `.pms` charts in single-play and double-play layouts.

<p align="center">
  <img src="https://github.com/user-attachments/assets/816fb844-e684-48d0-8c37-0217fdd2d9b2" width="900" alt="BMS gameplay running natively in osu!lazer">
  <br>
  <sub>Gameplay with the developer's custom skin</sub>
</p>

## Installation

1. Download `osu.Game.Rulesets.BmsRuleset.dll` from the
   [latest release](https://github.com/QingQiz/BmsRuleset/releases). Choose a release whose `xxxx.yyy` version
   matches your installed osu!lazer version.
2. In osu!, open **Settings → General → Open osu! folder**, then place the DLL in the `rulesets` folder. Create the
   folder if it does not exist.
3. Restart osu!. **BMS** will appear in the ruleset selector.

## Importing BMS Charts

1. Open **Settings → BMS → Import BMS files**.
2. Navigate to your BMS folder and import a chart, the current folder, or an entire directory recursively.
3. Imported charts appear in song select.

<p align="center">
  <img src="https://github.com/user-attachments/assets/62ff2f6d-a74a-4114-a90f-9b71d13713ca" width="900" alt="BMS chart import screen">
  <br>
  <sub>BMS chart importer</sub>
</p>

> [!IMPORTANT]
> Only chart data is imported into osu!'s database. Audio, images, and videos are loaded from the original BMS folder.
> Do not move or delete that folder after importing.

Use **Clean up orphaned BMS sets** in the BMS settings to remove entries whose source files are missing.
You can also remove all imported BMS metadata without deleting the original files.

## Highlights

Supports BPM/STOP changes, long notes, mines, random branches, scroll changes, BGA, and keysounds.
Judgements use beatoraja-compatible timing windows and
[four selectable algorithms](./docs/gameplay.md#judgement-selection-algorithms), with six gauge types and
traditional EX score alongside osu!'s normalized score.

### osu!mania Skin Support

Use an existing osu!mania skin, add a dedicated `[BMS]` configuration, or use the built-in skin.
Supports 5-key, 7-key, and 9-key charts and their double-play layouts.

> [!NOTE]
> BMS and osu!mania skin components may overlap, such as two hit error meters appearing together.
> Use the skin editor to rearrange or remove them.

See the [skin system guide](./docs/skin-system.md) for configuration priority and the `skin.ini` reference.

### Visually Editable Skin Components

Use osu!'s visual skin editor to arrange, resize, and configure the playfield, BGA, gauge, judgement display,
and other components. Layouts are saved per skin without modifying the chart or its `skin.ini`.

<p align="center">
  <img src="https://github.com/user-attachments/assets/c3690c49-3a00-4970-abd8-0e93220d6f11" width="900" alt="BMS components in the osu! visual skin editor">
  <br>
  <sub>BMS components in the visual skin editor</sub>
</p>

See [Skin Components](./docs/skin-system.md#skin-components) for all components and options.

### Song Preview

Song-select previews use `#PREVIEW`, then `preview.*`, or synthesize audio from the chart's BGM and keysounds
when neither is available.

Disable **Use dedicated preview audio** in the BMS settings to always synthesize previews.

### Visual Offset Calibration

Adjust when notes reach the judgement line without changing audio, judgements, or scoring. Apply suggested offsets
from local plays manually or automatically in the BMS settings.

**LN tail visual offset** makes long notes appear shorter by advancing their visual tails.
See [calibration and settings](./docs/gameplay.md#settings) for details.

### Uncapped BMS Frame Rate

Enable **Unlock frame rate limit** in the BMS settings to remove the 1000 Hz cap on rendering, updates, and input
polling during BMS gameplay. Higher GC and GPU pressure may cause stutters; disable the option if this happens.

### Clear Lamps

Song select shows your best local clear lamp matching the selected mods, from No Play to Max.
See [lamp types and filtering rules](./docs/gameplay.md#lamps).

### Difficulty Tables

Import LR2/beatoraja difficulty tables from a URL or local JSON file to add level markers and collections.
Presets include Satellite, Stella, Insane BMS, Overjoy, Scramble, and Luminous.

Table entries missing from your library also appear in song select. Attempting to play one opens its download page
if the table provides a link.

<p align="center">
  <img src="https://github.com/user-attachments/assets/29b2c98c-0293-4b39-8c08-7341108476d9" width="900" alt="Clear lamps and difficulty table markers in song select">
  <br>
  <sub>Clear lamps and difficulty-table levels in song select</sub>
</p>

Manage tables in the BMS settings. See the [difficulty table guide](./docs/difficulty-tables.md) for import,
update, and collection options.

### Course Mode

Play courses imported from difficulty tables, with course gauges and table-defined constraints.
Results are saved locally and shown on course cards in song select.

Courses require all stages to be installed and cannot be paused. The next song starts automatically after
99 seconds between stages. See [course rules and missing charts](./docs/difficulty-tables.md#courses).

### Result Screen

Open any saved score to view detailed analysis without watching its replay. This includes gauge history, note and
judgement timelines, Fast/Slow distribution, hit scatter and offset graphs, and per-key timing.

<p align="center">
  <img src="https://github.com/user-attachments/assets/54c03242-a091-4824-9b7e-a6c8ffaa44b0" width="900" alt="BMS result screen with detailed performance analysis">
  <br>
  <sub>Gauge history and timing analysis</sub>
</p>

<p align="center">
  <img src="https://github.com/user-attachments/assets/33c14f89-29a9-42dc-a09f-1224563274d2" width="900" alt="BMS course mode result screen">
  <br>
  <sub>View course totals or select a song card for per-song statistics</sub>
</p>

### osu!mania 7K Conversion

Play osu!mania 7K charts as BMS: the seven lanes map to BME key columns, with scratch left empty.
Note timing, holds, BPM, time signatures, offsets, and scroll-velocity changes are preserved; other key counts
are not supported.

Select BMS and enable **Show converts** in the song select filter panel.

## Documentation

| Guide                                              | Contents                                                               |
|----------------------------------------------------|------------------------------------------------------------------------|
| [Gameplay and settings](./docs/gameplay.md)        | Judgements, scoring, gauges, mods, key bindings, and settings          |
| [Difficulty tables](./docs/difficulty-tables.md)   | Importing and managing LR2/beatoraja difficulty tables                 |
| [Skin system](./docs/skin-system.md)               | `skin.ini` reference, osu!mania compatibility, and editable components |
| [BMS format support](./docs/bms-format-support.md) | Parser behavior, supported commands and channels, and reference links  |
| [Development](./docs/development.md)               | Building from source, missing features, and known issues               |
