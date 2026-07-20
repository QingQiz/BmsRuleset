# Difficulty Tables

[Back to README](../README.md) | [中文](./difficulty-tables.zh-CN.md)

Difficulty tables (LR2/beatoraja format) provide difficulty ratings and markers for BMS charts. The ruleset supports
importing tables from a JSON file or URL, and automatically matches charts by MD5 hash.

## Preset Tables

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

## Importing a Table

1. Open **Settings → BMS** → scroll to **Difficulty Tables**.
2. Paste a URL (e.g. `http://zris.work/bmstable/turbow/header.json`) or a local file path into the text box.
3. Click **Import** (or press Enter).

The importer supports three JSON formats:

- **Separate files** — `header.json` + `data.json` linked by `data_url`
- **Combined file** — a single JSON with both header fields (name, symbol, level_order) and `"charts": [...]`
- **HTML page** — a web page with `<meta name="bmstable" content="URL">` pointing to the JSON

## How Markers Work

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

## Collections

Each table also creates a **BeatmapCollection** named `[BMS] {table.Name}` containing all matched charts. This lets you
browse the table's songs directly from the song select collection list.
(The collection name uses invisible characters under the hood, so you don't need to worry about it colliding with your
own collections.)

## Table Row Display

Each imported table appears as an expandable item in the settings list. Hover it to see a tooltip with the chart count
per level.

Click a table to show its available actions. Remote tables provide **Subdivide/Unsubdivide**, **Update**, and
**Delete table**; local tables omit **Update**. The actions stay visible after an operation, including when the table
list is rebuilt.

Expand the table row and click **Subdivide** to split its collection into per-level collections with ordering indices
(e.g., `[BMS] Table [00] ★1`, `[BMS] Table [01] ★2`). The index width adapts to the number of levels
(1 digit for &lt;10, 2 for &lt;100, etc.). Click **Unsubdivide** to merge them back into one.

Remote tables provide an **Update** action that re-imports the table from its original source URL.

Click **Delete table** and confirm the dialog to remove a table, its markers, and its generated collections.
