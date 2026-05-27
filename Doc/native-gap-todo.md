# Native BMS Gap TODO

This document tracks code that is still transitional, non-native-BMS, or intentionally simplified only to keep the ruleset buildable/playable while osu!mania dependency removal proceeds.

## Current Status

- The project no longer references `ppy.osu.Game.Rulesets.Mania` in source or project files.
- No default gameplay path currently instantiates mania ruleset, mania beatmap, mania hit objects, or mania drawable ruleset types.
- Several systems are native only in name/shell. They compile and allow parser/import tests to run, but are not yet correct BMS gameplay implementations.

## Critical Gameplay Gaps

### Input Judgement Is Basic

Files:

- `BmsInputManager.cs`
- `BmsAction.cs`
- `UI/BmsPlayfield.cs`
- `Objects/Drawables/DrawableBmsHitObject.cs`

Current state:

- `BmsInputManager` provides native action binding infrastructure.
- `BmsPlayfield` routes layout-specific actions for 5K, 7K, 5K DP, 7K DP, PMS 9K, and PMS DP to columns and can judge top-level visible tap notes.
- `DrawableBmsHitObject.TryHit()` applies user-triggered timing-window results; passive misses still apply after the miss window.
- `OnPressed` now selects the earliest unjudged in-window note in the column (by `StartTime`), ensuring strict sequential ordering and preventing a later note from being hit before an earlier one.
- `OnReleased` similarly selects the earliest held LN in the release window (by `EndTime`).
- `CheckForResult` now anchors the LN release-miss to `EndTime` (not `StartTime`), fixing a bug where long notes would be passively failed during their body duration. LN drop (held but never released past `EndTime + missWindow`) correctly applies `HitResult.Miss`.
- There is no full key state handling or key beams. Scratch/turntable semantics are column-routing only.

TODO:

- Add scratch turntable semantics beyond column routing.
- Extend replay/autoplay for LN releases and future branch decisions.

### Renderer Is Time-Based Placeholder, Not Tick-Based BMS

Files:

- `UI/BmsPlayfield.cs`
- `UI/BmsDrawableRuleset.cs`
- `Objects/Drawables/DrawableBmsHitObject.cs`

Current state:

- Playfield lane backgrounds use `BmsBeatmap.TotalColumns` and `BmsLayoutVariant`.
- Stage columns support scratch widths and centre the non-scratch key area on the playfield.
- Drawable notes fill the full lane width and anchor at lane left-bottom.
- Drawable objects use the containing `BmsPlayfield.TotalColumns` and clamp invalid columns, so DP columns no longer wrap into the first bank.
- Object Y-position is based on `HitObject.StartTime - Time.Current`, configurable mania-style `ScrollSpeed`, and fixed `travel_distance = 560`.
- STOP/soflan visual behaviour is not native. STOP affects decoded `StartTime`, but rendering remains projected-time based.
- `BmsDrawableRuleset.CreateDrawableRepresentation()` returns `null`; rendering currently relies on pool registration rather than explicit representation creation.
- LN body rendering does not exist. Long notes only change note colour to cyan.
- BGA, layer, poor-layer, movie, stagefile, banner, and background rendering do not exist.
- `#WAVxx` key sounds and BGM channel `01` autoplay samples are parsed and played through BMS sample lookup. They ignore osu! global Effect volume while following universal volume. BGA sync polish is still incomplete.

TODO:

- Keep `BmsTimingMap` / native tick-time projection data available for parser semantics.
- Decide future native scroll model; current gameplay intentionally renders by projected time.
- Implement STOP freeze and BPM/scroll semantics from BMS timing.
- Continue expanding selected BMS layout metadata, including special spacing, scratch side variations, PMS, and DP stage separation.
- Implement LN head/body/tail rendering from `Tick` and `EndTick`.
- Add BGA/movie/stagefile layers and resource lookup.

### Judgement/Scoring Is Generic osu!-Style, Not BMS-Specific

Files:

- `Scoring/BmsHitWindows.cs`
- `Scoring/BmsScoreProcessor.cs`
- `Scoring/BmsJudgement.cs`
- `BmsRuleset.cs`

Current state:

- `BmsHitWindows` uses LR2 hit windows driven by `#RANK` (0–4). Beatoraja windows are fully documented in comments as an alternative. `SetDifficulty` ignores OD.
- `#RANK` is parsed and stamped per-object (`BmsHitObject.BmsRank`); each object creates its own `BmsHitWindows(BmsRank)`.
- `BmsResultFor` implements asymmetric early/late windows: Early POOR zone (−1000 to −200 ms) returns `Miss`; beyond −1000 ms returns `None` (Empty POOR territory).
- `BmsScoreProcessor` implements EX-score: Perfect=2, Great=1, all else=0; no combo multiplier. `ComputeTotalScore = accuracy × 1,000,000`.
- DJ LEVEL rank mapping: X (all Perfect), S≥8/9 EX, A≥7/9, B≥6/9, C≥5/9, D otherwise.
- `GetValidHitResults()` returns all 6: Perfect/Great/Good/Ok/Meh/Miss mapping to BMS PGREAT/GREAT/GOOD/BAD/POOR/EARLY-POOR.
- Empty POOR (press outside all note windows) breaks combo and drains gauge via `RegisterEmptyPoor` on both processors.

Known combo-break mismatches vs native BMS (osu! framework constraint):

- **BAD → `Ok`**: `HitResult.Ok.IsHit()` returns `true` in osu!, so `Ok` increases combo, not breaks it. BMS BAD must break combo.
- **POOR → `Meh`**: `HitResult.Meh.IsHit()` returns `true` in osu!, so `Meh` increases combo, not breaks it. BMS POOR must break combo.
- **EARLY POOR → `Miss`**: `Miss` correctly breaks combo. However, native BMS EARLY POOR does not break combo. Documented accepted mismatch.

Pass/fail mismatch:

- **`ScoreRank.F`** is assigned by the base `ScoreProcessor` when accuracy drops below a threshold. BMS pass/fail is gauge-only (Normal gauge: ≥ 80% at song end). The accuracy-based F rank is currently incorrect.

TODO:

- Force-break combo on `Ok` (BAD) and `Meh` (POOR) results in `BmsScoreProcessor` by overriding `ApplyResultInternal`.
- Override `RankFromScore` to never return `ScoreRank.F`; fail state is gauge-only.
- Implement clear lamp logic driven by `BmsHealthProcessor.Health` at song end (≥ 80% = clear, < 80% = fail/no-clear).
- Parse and apply `#EXRANK`.
- Add display name aliases for BMS judgement labels (PGREAT, GREAT, etc.) in HUD.

### Gauge/Health

Files:

- `Scoring/BmsHealthProcessor.cs`

Current state:

- Passive drain is disabled (`ComputeDrainRate()` returns 0).
- `BmsHealthProcessor` implements the BMS Normal gauge: starts at 20%, discrete hit deltas driven by `#TOTAL`. PGREAT +`total/100/N`, GREAT ×0.5 of that, GOOD ×0.2, BAD (Ok) −3.2%, POOR/MISS (Meh/Miss) −4.8%.
- Default `#TOTAL` formula `max(7.605×N/(0.01×N+6.5), 160)` used when `#TOTAL` is absent from chart.
- `#TOTAL` parsing pipeline is complete: BmsParser → BmsParseResult → IBmsBeatmap → BmsBeatmap → BmsDecodedBeatmap.
- Empty POOR gauge drain implemented via `RegisterEmptyPoor` (−4.8%, no note consumed).
- `CheckDefaultFailCondition` from the base `HealthProcessor` triggers failure at `Health ≤ 0`. This is the Hazard gauge rule, not Normal gauge. For Normal gauge, the player may drop below 80% mid-song and recover; failure only occurs if gauge reaches 0.
- Long-note drop records `HitResult.Miss` instead of `HitResult.Meh`; both map to −4.8% so the delta is correct but the result type is semantically wrong (drop = POOR, not EARLY POOR).
- Easy, hard, ex-hard, hazard, course, PMS, and DP gauge behaviours are not implemented.
- Normal gauge clear condition (≥ 80% at final note) is not enforced; a player finishing at 5% health is not failed.

TODO:

- Enforce Normal gauge clear condition: trigger failure if `Health < 0.8` at song end (after the last note is judged).
- Fix LN drop result type: use `HitResult.Meh` instead of `HitResult.Miss` in `DrawableBmsHitObject.CheckForResult` for the drop path.
- Model LN-specific gauge events (head miss vs. drop vs. tail miss).
- Implement easy/hard/ex-hard/hazard gauge variants and gauge-selection mods.
- Add course gauge continuity.

## Parser And Timing Gaps

### Native Timing Is Still Stored Mostly As osu! `StartTime`

Files:

- `Beatmaps/BmsBeatmapDecoder.cs`
- `Objects/BmsHitObject.cs`
- `Beatmaps/BmsBeatmap.cs`

Current state:

- `BmsHitObject.TickInfo` stores `Tick`, `EndTick`, and `TickResolution`; `BmsHitObject` itself only keeps projected osu! time fields.
- The decoder still projects ticks into `StartTime`/`Duration` immediately for osu! compatibility.
- `BmsBeatmap.TimingMap` preserves measure lengths, BPM events, STOP events, tick resolution, and tick-to-time projection data.
- `TimingControlPoint` cannot faithfully expose extreme BMS BPM such as Aleph-0's `#BPM01 0.2441406` because osu! control points clamp beat length.
- STOP timing is applied to projected object times and preserved in `BmsTimingMap`; gameplay rendering consumes the projected `StartTime` only.

TODO:

- Keep `BmsTimingMap` as parser/projection data; gameplay currently uses projected object times.
- Extend `BmsTimingMap` with raw tick/time segments as needed for reverse projection and soflan rendering.
- Use osu! `ControlPointInfo` only as compatibility metadata, not the source of BMS timing truth.
- Add tests for sub-1 BPM, huge BPM, STOP-at-same-tick ordering, and BPM/STOP interactions.

### Control Flow Is Not Truly Runtime-Resolved

Files:

- `Beatmaps/BmsBeatmapDecoder.cs`

Current state:

- The parser currently treats `#random`, `#if`, `#else`, and `#endif` lines as normal unknown commands.
- Because Aleph-0 uses `#random 1` / `#if 1`, current parser happens to include the active block and tests pass.
- General BMS random branches are not represented as an AST and are not resolved per play/replay.
- Inactive branches are not separated from active branches.

TODO:

- Implement Control Flow AST import for `#RANDOM`, `#IF`, `#ELSEIF`, `#ELSE`, `#ENDIF`, `#ENDRANDOM` variants.
- Resolve branches at runtime/play start with replay-stored RNG decisions.
- Include resources from all possible branches in import manifests.
- Add tests with multiple branches where only one branch is active per replay.

### Layout Inference Is Simplified

Files:

- `Beatmaps/BmsBeatmapConverter.cs`
- `Beatmaps/BmsBeatmapDecoder.cs`
- `BmsAction.cs`

Current state:

- Column mapping is hardcoded in the decoder.
- `BmsBeatmapConverter` restores native layout/column count from decoder sidecar data.
- 5K, 7K, 5K DP, 7K DP, PMS 9K, and PMS DP layout variants exist.
- BME-type PMS 9K is documented but not fully inferred.
- Channel `17` / `27` free-zone handling is not implemented.
- Layout metadata is stored as `BmsLayout` / `BmsLayoutVariant` plus `TotalColumns`.

TODO:

- Add remaining special native layout models such as BME-type PMS and future variants.
- Preserve source channel to lane mapping in beatmap metadata.
- Use layout model for parser, input, renderer, replay, and difficulty.

### Long Notes Are Only Top-Level Duration Objects

Files:

- `Beatmaps/BmsBeatmapDecoder.cs`
- `Objects/BmsHitObject.cs`
- `Objects/Drawables/DrawableBmsHitObject.cs`
- `Scoring/BmsHealthProcessor.cs`

Current state:

- `#LNTYPE 1`, `#LNTYPE 2`, and `#LNOBJ` create top-level `BmsHitObject` with `Duration`.
- No head/tail/nested native hit objects exist.
- Release judgement, dropped LN behaviour, tail miss behaviour, and LN body scoring are not implemented.
- `#LNTYPE 2` run-end handling is basic and needs broader real-chart validation.

TODO:

- Model native LN head/body/tail and release semantics.
- Implement LN-specific judgement, score, gauge, and replay events.
- Add visual LN body rendering and tests for all LN styles.

## Import Gaps

### Import Is Better But Still Not Full BMS Set Planning

Files:

- `Beatmaps/BmsFileImporter.cs`

Current state:

- Directory import groups charts by folder into one `BeatmapSetInfo`.
- Single chart import produces one beatmap set with one beatmap.
- Referenced resources from chart definitions are imported.
- Declared `.wav` resources can resolve to existing sibling files with other extensions, such as `.ogg`.
- Resource parsing is regex-based and only covers common `#WAVxx`, `#BMPxx`, `#BGAxx`, `#STAGEFILE`, `#BANNER`, `#BACKBMP`, and `#MOVIE` lines.
- Resources are stored by `Path.GetFileName(path)`, so relative subdirectories and duplicate basenames are not preserved.
- `Import(ImportTask[] tasks, ImportParameters parameters)` ignores streams and parameters, using only `ImportTask.Path`.
- Duplicate detection skips an entire set if any chart hash already exists.
- `BeatmapSetInfo.Hash` is a concatenated MD5 string, not a robust set hash.

TODO:

- Build a real import planner that distinguishes beatmapset, charts, and resource manifest.
- Preserve relative paths for resources and chart files.
- Support `#PATH_WAV`, `#PATH_BMP`, extended resource directives, and branch resources.
- Support stream/archive import tasks correctly.
- Decide duplicate handling per chart vs per set.
- Compute deterministic set hash independent of chart ordering details.

## Mods And Automation Gaps

Files:

- `Mods/BmsModDoubleTime.cs`
- `Mods/BmsModHalfTime.cs`
- `Mods/BmsModNoFail.cs`
- `Mods/BmsModCinema.cs`
- `BmsRuleset.cs`

Current state:

- DT/HT/NF/Cinema are thin wrappers around osu! generic mods.
- Native autoplay is exposed through `BmsModAutoplay` and BMS replay frames.
- Random/Mirror/S-Random/H-Random, gauge mods, assist options, and BMS-specific options are absent.
- Mod interactions with BMS timing, sample playback, BGA, replay, and random branches are undefined.

TODO:

- Implement native BMS mods and compatibility rules.
- Extend native autoplay/replay generation for LN release semantics and branch decisions.
- Implement BMS randomisation mods with replay reproducibility.

## Difficulty And Performance Gaps

Files:

- `Difficulty/BmsDifficultyCalculator.cs`
- `BmsRuleset.cs`

Current state:

- Difficulty attributes always report star rating `0`.
- `MaxCombo` is only hit object count.
- Skills array is empty.
- Performance calculator is not provided.

TODO:

- Add native difficulty hit objects with tick distance, column, scratch, LN, BPM, STOP, and soflan context.
- Implement BMS strain/scratch/LN/soflan skills.
- Add performance calculator or intentionally document no-performance state.
- Add tests using real charts to prevent regression in difficulty attributes.

## UI, Skin, And Icon Gaps

Files:

- `BmsRuleset.cs`
- `UI/BmsPlayfield.cs`
- `Objects/Drawables/DrawableBmsHitObject.cs`

Current state:

- Ruleset icon still uses `OsuIcon.RulesetMania` as a placeholder.
- `CreateSkinTransformer()` returns a BMS legacy skin transformer for `LegacySkin`.
- Legacy mania skin compatibility exists for columns, notes, key areas, key-down pieces, hit target, hit explosions, judgements, and HUD pieces.
- No native BMS skin format, keybeam, or BGA skin support exists.

TODO:

- Add BMS-specific icon.
- Define native BMS skin format and defaults.
- Add keybeam/BGA skin components and richer native note/LN skin support.

## Tests That Still Need Coverage

Current tests cover:

- Full Aleph-0 payload embedding.
- Real filesystem BMS decode.
- Semantic object/timing/STOP expectation for `_7NORMAL.bms`.
- Directory and single-file import using Aleph-0.
- Basic native factory types and no mania dependency references.
- Native autoplay/replay frame plumbing.
- Simulated playfield layout checks for lane-width notes and scratch-excluded centering.

Missing tests:

- Real charts with `#LNTYPE 1`, `#LNTYPE 2`, and `#LNOBJ` gameplay semantics.
- Control Flow with inactive branches and replay reproducibility.
- BME-type PMS layout inference.
- DP gameplay/rendering beyond basic column layout and action routing.
- Resource paths with subdirectories and duplicate basenames.
- `#PATH_WAV` / `#PATH_BMP`.
- BGA/movie playback resources.
- Gauge, scoring, judgement windows, and clear lamps.
- Full user-triggered LN judgement and replay.
