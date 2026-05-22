# osu.Game.Rulesets.BmsRuleset

Native osu! ruleset plugin for BMS-family charts. Do not reintroduce `ppy.osu.Game.Rulesets.Mania`; use osu!mania source only as API/reference material, never as an implementation dependency.

## Local References

- Use sibling checkouts for API/source inspection: `..\osu`, `..\osu-framework`, `..\rulesets`.
- If missing, clone `https://github.com/ppy/osu.git` and `https://github.com/ppy/osu-framework.git`; community rulesets are linked from `https://github.com/ppy/osu/discussions/13096`.
- Trust csproj/sln/tests over docs when they disagree.

## Commands

- Build plugin: `dotnet build "osu.Game.Rulesets.BmsRuleset"`
- Run all tests: `dotnet test "osu.Game.Rulesets.BmsRuleset.Tests"`
- Focus parser/import tests: `dotnet test "osu.Game.Rulesets.BmsRuleset.Tests" --filter "FullyQualifiedName~BmsEmbeddedSongDecoderTest|FullyQualifiedName~BmsFileImporterTest"`
- Visual runner entrypoint is `osu.Game.Rulesets.BmsRuleset.Tests.VisualTestRunner`; use it for manual player/settings scenes, not normal CI verification.
- Expected output DLL: `osu.Game.Rulesets.BmsRuleset\bin\Debug\net8.0\osu.Game.Rulesets.BmsRuleset.dll`.
- Known warning: `TestSceneBmsSettings.cs` emits `OFSG001`; do not treat it as a regression unless new warnings appear.

## Architecture Boundaries

- `BmsRuleset` extends `Ruleset` directly; gameplay path should stay native: `BmsDrawableRuleset`, `BmsPlayfield`, `BmsHitObject`, `BmsDifficultyCalculator`, BMS mod wrappers.
- `BmsBeatmapConverter` is intentionally pass-through for decoded `BmsHitObject`s; do not convert BMS through mania objects.
- `BmsHitObject.TickInfo` carries native tick data (`Tick`, `EndTick`, `TickResolution`), while `BmsHitObject` itself keeps projected osu! `StartTime`/`Duration`. Keep new parser/render logic tick-first.
- `CreateSkinTransformer()` returns `BmsLegacySkinTransformer` for `LegacySkin`; native BMS skinning is not implemented yet.
- `BmsRuleset.CreateIcon()` still uses `OsuIcon.RulesetMania` as a placeholder. This is tracked debt, not a dependency.

## BMS Parser Rules

- `192` is only the base/default tick resolution. Expand resolution for payload divisions and measure lengths that would otherwise create fractional ticks.
- STOP values are in BMS base `1/192` whole-note units even when internal tick resolution expands.
- Project ticks to milliseconds at timing boundaries only; preserve native timing data where possible.
- Aleph-0 contains extreme BPMs such as `#BPM01 0.2441406`; osu! `TimingControlPoint` clamps beat lengths, so it cannot be the source of truth for native BMS timing.
- `#RANDOM/#IF` control flow must eventually be runtime/replay-resolved. Do not permanently flatten branches during import.
- `#LNTYPE 1`, `#LNTYPE 2`, and `#LNOBJ` are required native formats, not mania hold-note shims.
- Channel `01` is BGM/autoplay sample data, not playable notes.

## Real Chart Tests

- Full BMS payloads are embedded from `osu.Game.Rulesets.BmsRuleset.Tests\bms_test_songs\**\*` via the test csproj. Keep media files embedded; importer tests need sibling resources.
- Current local resource folders include `Aleph-0 (by LeaF)`, `[Clue]Random`, and `Destr0yer (by 削除 feat. Nikki Simmons)`.
- `BmsEmbeddedSongDecoderTest` decodes every embedded `.bms/.bme/.bml/.pms` resource. Avoid hardcoding only Aleph-0 assumptions in broad resource tests.
- `TestSevenNormalDecodedObjectsMatchBmsTextSemantics` independently scans Aleph-0 `_7NORMAL.bms` and checks decoded note count, first playable note, column distribution, raw BPM events, and STOP-inflated times. Parser changes should satisfy semantic tests, not just smoke tests.
- `BmsFileImporterTest` imports a real folder and a single real chart through `BmsFileImporter`; expect one beatmap set per folder, one beatmap per chart, and referenced sibling resources included.

## Import Gotchas

- `BeatmapInfo.MD5Hash` must be chart-file MD5. `BeatmapInfo.Hash` must match the chart `RealmFile.Hash` (SHA-256) so `BeatmapInfo.File` and `Path` resolve.
- Import currently resolves declared `.wav` resources to existing sibling files with other extensions such as `.ogg`; keep this behaviour for real BMS sets.
- Resource names currently use `Path.GetFileName()`, so relative subdirectories and duplicate basenames are unresolved debt.
- `Import(ImportTask[] tasks, ImportParameters parameters)` currently uses task paths only; stream/archive import is not complete.

## Native Gaps To Check Before Feature Work

- Read `Doc/native-gap-todo.md` before touching gameplay, timing, scoring, gauge, input, renderer, mods, skinning, or import. It lists intentional skeletons and placeholders.
- High-risk placeholders: input does not judge notes; rendering is time-based and assumes 8 columns; LN body/release judgement is missing; hit windows ignore `#RANK/#EXRANK`; gauge and difficulty are shells.

## UI/Test Harness Gotchas

- `SettingsSubsection` is a `FillFlowContainer`; children should not use `RelativeSizeAxes = Axes.Both` there.
- `BmsRulesetConfigManager` is not globally registered. For arbitrary screens, use `Storage` persistence like `BmsFileImportScreen.readLastPath/writeLastPath`.
- File import handler registration is in `BmsSettingsSubsection.load()` via `game.RegisterImportHandler(...)`; unregister in `Dispose()` and guard against double registration.
- Screen tests needing popovers must provide `PopoverContainer` plus cached `OverlayColourProvider` for controls like `OsuDirectorySelector`.
