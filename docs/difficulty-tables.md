# Difficulty Tables

[Back to README](../README.md) | [中文](./difficulty-tables.zh-CN.md)

Import LR2/beatoraja difficulty tables from a JSON file or URL. Charts are matched by MD5 and receive table-level
markers and collections.

## Preset Tables

Select a preset from the autocomplete dropdown:

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

The importer accepts three formats:

- **Separate files** — `header.json` + `data.json` linked by `data_url`
- **Combined file** — a single JSON with both header fields (name, symbol, level_order) and `"charts": [...]`
- **HTML page** — a web page with `<meta name="bmstable" content="URL">` pointing to the JSON

## Missing Charts

Charts listed by an imported table but missing from the local library still appear in song select.
An orange warning indicates an available download link; a red warning means the table provides no usable link.
Attempting to play a missing chart opens its download page when available.

## Courses

The table header's `course` array defines courses. Each requires a `name` and an ordered list of stage hashes,
matched to local charts by MD5 or SHA-256. Titles, artists, and levels come from the table's chart data.
Missing stages remain unavailable until their charts are imported.

All stages must be installed to start a course. Otherwise, attempting to start opens download pages for all missing
stages if each has a valid URL. If any lacks a URL, no pages open and a missing-stage notification appears.

Courses cannot be paused. Between stages, the next song starts automatically after 99 seconds.

The selected gauge mod determines the course gauge tier (Class, EX Class, or EX Hard Class), as in beatoraja;
courses do not specify a tier. The `constraint` array defines rules shown in course select and enforced during play.

| Constraint    | Effect                                                  |
|---------------|---------------------------------------------------------|
| `grade`       | Only the default layout; no mirror or random            |
| `grade_mirror`| Mirror or default layout                                |
| `grade_random`| —                                                       |
| `no_speed`    | Locks in-play scroll speed and disables the Constant mod |
| `no_good`     | Removes the GOOD judgement window                       |
| `no_great`    | Removes the GREAT and GOOD judgement windows            |
| `gauge_lr2`   | Forces the LR2 gauge profile                            |
| `gauge_5k`    | Forces the 5-key gauge profile                          |
| `gauge_7k`    | Forces the 7-key gauge profile                          |
| `gauge_9k`    | Forces the PMS gauge profile                            |
| `gauge_24k`   | Forces the 24-key gauge profile                         |
| `ln`          | Forces classic long-note (LN) mode                      |
| `cn`          | Forces charge-note (CN) mode                            |
| `hcn`         | Forces hell-charge-note (HCN) mode                      |

## How Markers Work

After import, every beatmap whose MD5 matches a table entry gets a **marker** appended to its difficulty name:

```
DP ☆NOTHER [TT★1 TT★2]
```

Markers contain the table symbol and level, and update when tables are added or removed.

> [!IMPORTANT]
> Switch to the **main menu** before importing or deleting a table. Deleting one rebuilds all BMS markers;
> on the song select screen, this conflicts with the carousel's Realm reads and freezes the UI.

## Collections

Each table creates a collection named `[BMS] {table.Name}` for browsing matched charts in song select.
Invisible characters in the name prevent collisions with your own collections.

## Table Row Display

Hover a table in the BMS settings to see chart counts per level. Click it to expand its actions:

- **Subdivide/Unsubdivide** splits the collection by level or merges it back. Split collections have ordering indices,
  such as `[BMS] Table [00] ★1` and `[BMS] Table [01] ★2`. Indices use 1 digit for &lt;10 levels, 2 for &lt;100, and so on.
- **Update** re-imports a remote table from its source URL. Local tables do not offer this action.
- **Delete table** removes the table, its markers, and its collections after confirmation.

The row stays expanded after an operation, including when the table list is rebuilt.
