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
- `OnPressed` now selects the earliest unjudged in-window non-mine note in the column (by `StartTime`), ensuring strict sequential ordering and preventing a later note from being hit before an earlier one.
- `OnReleased` similarly selects the earliest held LN in the release window (by `EndTime`).
- Landmine channels `D1-D9`/`E1-E9` are parsed. Mines have no timing windows: they detonate only if the mapped column is held as the mine crosses the judgement line; otherwise they are silently ignored. A detonation applies POOR display/stat semantics, key-up for that column, `#WAV00` explosion playback when defined, and gauge damage from the mine value.
- `CheckForResult` now anchors the LN release-miss to `EndTime` (not `StartTime`), fixing a bug where long notes would be passively failed during their body duration. LN drop (held but never released past `EndTime + missWindow`) correctly applies `HitResult.Meh` (POOR). Passive misses on normal notes also apply `HitResult.Meh` (POOR). `HitResult.Miss` is not used for note results; it is the Empty POOR counter in `Statistics`.
- Key sound on press plays the note whose keysound is most relevant: `findNextSoundHitObject` skips only notes whose `StartTime < Time.Current − BmsHitWindows.BadWindow` (200 ms), so a late keypress within the BAD window still triggers the correct note's keysound rather than the next note.
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
- LN head/body/tail render through native BMS skin components, with mania-style body/tail overlap and basic masking while held. Rendering is still projected-time based, not native tick-scroll based.
- BGA, layer, poor-layer, movie, stagefile, banner, and background rendering do not exist.
- `#WAVxx` key sounds and BGM channel `01` autoplay samples are parsed and played through BMS sample lookup. They ignore osu! global Effect volume while following universal volume. BGA sync polish is still incomplete.

TODO:

- Keep `BmsTimingMap` / native tick-time projection data available for parser semantics.
- Decide future native scroll model; current gameplay intentionally renders by projected time.
- Implement STOP freeze and BPM/scroll semantics from BMS timing.
- Continue expanding selected BMS layout metadata, including special spacing, scratch side variations, PMS, and DP stage separation.
- Move LN head/body/tail rendering from projected time to native `Tick`/`EndTick` scroll projection.
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
- `BmsResultFor` implements asymmetric early/late windows: POOR zone (−1000 to −200 ms) returns `Meh` (note consumed as POOR); beyond −1000 ms returns `None` (Empty POOR territory, no note consumed); beyond +200 ms returns `None` (passive miss handled by `CheckForResult`).
- `BmsScoreProcessor` implements EX-score: Perfect=2, Great=1, all else=0; no combo multiplier. `ComputeTotalScore = accuracy × 1,000,000`.
- DJ LEVEL rank mapping: X (all Perfect), S≥8/9 EX, A≥7/9, B≥6/9, C≥5/9, D otherwise.
- `GetValidHitResults()` returns all 6: Perfect/Great/Good/Ok/Meh/Miss mapping to BMS PGREAT/GREAT/GOOD/BAD/POOR/E-POOR. `HitResult.Miss` is repurposed as the **Empty POOR counter** in `Statistics`; it is not a note judgement result (`IsHitResultAllowed` returns false for it, `WindowFor(Miss)` returns 0).
- Empty POOR (press outside all note windows) breaks combo and drains gauge via `RegisterEmptyPoor` on both processors. `BmsPlayfield.registerEmptyPoor()` also displays the POOR image in `JudgementArea` by looking up `SkinComponentLookup<HitResult>(HitResult.Miss)`.
- Landmine detonations use `HitResult.Meh` (POOR); `HitResult.Miss` remains reserved for Empty POOR. Ignored mines produce no judgement and do not affect score, accuracy, combo, gauge, or Empty POOR statistics.
- `BmsRuleset.HIT_RESULT_LABELS` is the single source of truth for BMS judgement label strings (PGREAT/GREAT/GOOD/BAD/POOR/E-POOR). Both `GetDisplayNameForHitResult` and `BmsDefaultJudgementPiece` read from it.
- Judgement drawables are pre-built once per result type at `LoadComplete` (one `SkinnableDrawable` per `HitResult` in `judgementDrawableCache`) and reused on every hit by moving them between a hidden pool container and `JudgementArea`. No `SkinnableDrawable` is allocated during gameplay.

Known combo-break mismatches vs native BMS (osu! framework constraint):

- **BAD → `Ok`**: `HitResult.Ok.IsHit()` returns `true` in osu!, so `Ok` increases combo, not breaks it. BMS BAD must break combo. `BmsScoreProcessor.ApplyScoreChange` overrides this by resetting `Combo.Value = 0` after any `Ok` or `Meh` result.
- **POOR → `Meh`**: Same override applies; combo is reset to 0.
- **Empty POOR → `Miss`**: `Miss` is not a note result; `RegisterEmptyPoor` breaks combo and drains gauge directly on both processors without going through the judgement pipeline.

Pass/fail:

- **`ScoreRank.F`**: `BmsScoreProcessor.RankFromScore` never returns `ScoreRank.F`; fail state is gauge-only (Normal gauge: < 80% at final note or gauge hits 0).
- **Normal gauge clear condition**: `BmsHealthProcessor.CheckDefaultFailCondition` triggers failure only when `JudgedHits >= MaxHits && Health < 0.8` (end-of-song check) or when `Health <= 0` (gauge bottomed out).

TODO:

- Parse and apply `#EXRANK`.
- Add `BmsResultsScreen` showing EX score, DJ LEVEL, gauge end %, PGREAT/GREAT/GOOD/BAD/POOR/E-POOR counts, clear type.

### Gauge/Health

Files:

- `Scoring/BmsHealthProcessor.cs`

Current state:

- Passive drain is disabled (`ComputeDrainRate()` returns 0).
- `BmsHealthProcessor` implements the BMS Normal gauge: starts at 20%, discrete hit deltas driven by `#TOTAL`. PGREAT +`total/100/N`, GREAT same as PGREAT (×1.0), GOOD ×0.5 of PGREAT, BAD (Ok) −4%, POOR (Meh) −6%.
- Default `#TOTAL` formula `max(7.605×N/(0.01×N+6.5), 160)` used when `#TOTAL` is absent from chart.
- `#TOTAL` parsing pipeline is complete: BmsParser → BmsParseResult → IBmsBeatmap → BmsBeatmap → BmsDecodedBeatmap.
- Empty POOR gauge drain implemented via `RegisterEmptyPoor` (−2%, no note consumed).
- Landmine gauge damage is implemented as base36 channel value / 2 percentage points. `ZZ` produces 647.5% damage, clamping gauge to 0 and immediately failing.
- `CheckDefaultFailCondition` triggers failure when gauge hits 0 mid-song, or when `JudgedHits >= MaxHits && Health < 0.8` at song end (Normal gauge clear condition).
- Long-note drop records `HitResult.Meh` (POOR) and passive normal-note misses also record `HitResult.Meh` (POOR). `HitResult.Miss` is the Empty POOR counter; it is not emitted as a note judgement result.

TODO:

- ~~Enforce Normal gauge clear condition.~~ ✓ Done.
- ~~Fix LN drop result type.~~ ✓ Done.
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
- Add visual tests for all LN styles.

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

## Failed Score Saving

Files:

- `UI/BmsDrawableRuleset.cs`
- `UI/BmsPlayer.cs`

Current state:

- BMS convention: every play (including failed ones) is saved to the local score DB so players can track gauge improvement.
- `osu.Game.Screens.Play.SoloSongSelect` hard-codes `new SoloPlayer()` and there is no `Ruleset.CreatePlayer()` hook in the current framework version.
- `BmsDrawableRuleset.LoadComplete` subscribes to `HealthProcessor.Failed` (resolved from Player's DI cache). When the fail event fires, a 500 ms deferred import is scheduled via `ScoreManager.Import(gameplayState.Score.ScoreInfo.DeepClone())`. The delay allows `Player.ConcludeFailedScore` (which stamps `Rank = F`) to run first.
- Import is fire-and-forget with error logging via `Task.ContinueWith(OnlyOnFaulted)`.
- Replay scores are excluded from the import (checked via `ReplayScore != null`).
- `BmsPlayer.cs` was deleted — it was never instantiated at runtime. `TestSceneBmsPlayer.cs` was also deleted.

Known limitations:

- `ScoreManager` is null in test environments without a full game DI context; guarded with `CanBeNull = true`.
- The 500 ms delay is a heuristic. If `ConcludeFailedScore` is not yet called by then, the imported score will lack `Rank = F`.

TODO:

- When the framework adds `Ruleset.CreatePlayer()`, switch to a proper `BmsPlayer` subclass and remove the `BmsDrawableRuleset` hack.
- Consider a more robust synchronisation mechanism (e.g. subscribing to a `Player.OnConcludeFailedScore` event) rather than fixed delay.

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
- `UI/BmsStage.cs`
- `Objects/Drawables/DrawableBmsHitObject.cs`
- `Skinning/BmsEmbeddedSkin.cs`
- `Skinning/BmsEmbeddedSkinSource.cs`
- `Skinning/BmsEmbeddedSkinDefinition.cs`
- `Skinning/BmsLegacySkinTransformer.cs`
- `Skinning/BmsBuiltInSkinTransformer.cs`
- `Skinning/BmsSkinConfigurationDecoder.cs`
- `Skinning/BmsSkinConfiguration.cs`
- `Skinning/BmsSkinConfigurationLookup.cs`
- `Skinning/BmsSkinComponentLookup.cs`

Current state:

- Ruleset icon still uses `OsuIcon.RulesetMania` as a placeholder.
- `BmsEmbeddedSkin` implements `ISkin + IDisposable` directly (not inheriting `Skin`); exposes `internal IResourceStore<byte[]> Resources` for decoder use. Two built-in skin kinds: `LegacyOld` (original legacy style) and `LegacyModern` (modernised but still legacy format).
- `BmsEmbeddedSkinSource` provides three-tier priority routing: beatmap skin → user skin → built-in embedded skin.
- `BmsLegacySkinTransformer` inherits `LegacySkinTransformer`; `IsProvidingLegacyResources` is overridden via `Lazy<bool> hasBmsResources` (checks `#BMS` skin.ini section, `mania-keyS` animation, and `mania-key1` animation as a weak fallback).
- `BmsBuiltInSkinTransformer` adapts built-in skins; passes through global HUD wrapped in `HealthFilteredHudContainer`; returns `null` for BMS-specific components, hit results, and ruleset HUD to let the built-in skin handle them.
- `CreateSkinTransformer()` uses an explicit switch: built-in skins → `BmsBuiltInSkinTransformer`; other `Skin` instances → `BmsLegacySkinTransformer`; pure `ISkin` (including `BmsEmbeddedSkin`) → `null`.
- `BmsSkinConfigurationDecoder` fully bypasses `LegacySkinDecoder` (no official `skin.ini` extension point); new `Decode(IResourceStore<byte[]>)` overload added as the preferred path; reflection-based `Decode(ISkin)` retains a TODO.
- `BmsSkinConfigurationLookup` has two column index paths: `ComponentLookup` (carries both BMS and mania column indices) and bare `ColumnIndex` (no column context).
- `BmsSkinConfiguration.TryGet` branches on lookup type: `[BMS]` section uses BMS column index; `[Mania]` section uses mania column index via `ManiaColumnIndex`.
- `BmsSkinComponentLookup` provides `GetManiaKeyCount`, `MapToManiaColumn`, `IsScratchColumn`, and `ManiaColumnIndex` helpers.
- Legacy mania skin compatibility exists for columns, notes, key areas, key-down pieces, hit target, hit explosions, judgements, and HUD pieces.
- ~~Bug: `JudgementArea.X` not aligned to non-scratch column centre~~ ✓ Fixed. `BmsStage.Update()` now sets `JudgementArea.X = nonScratchCentre - DrawWidth / 2` each frame.
- ~~Bug: `LeftLineWidth`/`RightLineWidth` query caused `IndexOutOfRangeException` on BME 7K / BMS 5K~~ ✓ Fixed. `BmsStage.updateFromSkin()` now queries `BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, column)` and uses `ManiaColumnIndex`, preventing OOB on `ColumnLineWidth[]`.
- ~~Bug: Judgement drawable anchored `TopLeft/TopLeft`, image appeared at stage left edge~~ ✓ Fixed. `BmsPlayfield` now sets `Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre` on all pre-cached judgement drawables so they centre inside `JudgementArea`.
- No native BMS skin format, keybeam, or BGA skin support exists.

TODO:

- Add BMS-specific icon.
- Define native BMS skin format and defaults.
- Add keybeam/BGA skin components and richer native note/LN skin support.
- Implement Argon-native skin transformer once a native (non-legacy) skin format is defined.
- Remove reflection from `BmsSkinConfigurationDecoder.Decode(ISkin)`; use `IResourceStore<byte[]>` path exclusively once all callers are migrated.

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
