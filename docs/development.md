# Development

[Back to README](../README.md)

## Architecture and Verification

`Gameplay/Judgement` owns judgement windows, candidate selection, note and hold state, and rewind. Its rules run
independently of drawables; `Judgement/Framework` adapts results and hit windows to osu!, and the UI bridge connects
the core to drawables and visual feedback. `Gameplay/Scoring` applies results to EX score, combo, and health;
`Scoring/Events` stores and projects score and timing history. Result colours belong to `UI`.

Player tests cover input, replay, rewind, Mods, pooling, culling, and visual feedback.

Gauge history records exact values in contiguous buffers during gameplay. Snapshot objects are expanded when
history is read for rewind or score export; live Auto Gauge score population shares the history view. Saved
scores copy that view so subsequent seeks and retries cannot change their graphs.

Run the selected functional and instrumentation groups against existing sibling reference binaries:

```powershell
./scripts/verify-judgement-migration.ps1
```

All test commands must use `--filter`. Tests tagged `JudgementInstrumentation` use a separate process
with the repository's runsettings to keep method-entry observations visible; ordinary tests retain default
JIT settings. Performance benchmarks require explicit approval.

The verification script saves deterministic Player traces under `comparison/current` in its results directory.
Use [compare-judgement-traces.py](../scripts/compare-judgement-traces.py) to compare traces from two builds:

```powershell
python scripts/compare-judgement-traces.py artifacts/baseline/comparison/current artifacts/candidate/comparison/current --output artifacts/comparison.json
```

## Design Plans

- [Online leaderboard (中文)](plans/online-leaderboard-plan.zh-CN.md): osu! OAuth, replay uploads, and SQLite storage.
- [BMSON support](plans/bmson-support-plan.md): native BMSON 1.0 parsing and shared runtime chart data.

Both are proposals for features that are not implemented.

## Build Manually

Install [Git](https://git-scm.com/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then clone
this repository and osu! into sibling directories. Sparse checkout skips the large test-song folder:

```bash
git clone --filter=blob:none --sparse https://github.com/QingQiz/BmsRuleset.git
git -C BmsRuleset sparse-checkout set --no-cone '/*' '!bms_test_songs'
git clone --filter=blob:none https://github.com/ppy/osu.git
```

Read `OsuVersion` in `BmsRuleset/Directory.Build.props`, then check out the matching osu! tag. For example,
`OsuVersion` `2026.920.0` requires:

```bash
git -C osu checkout 2026.920.0-lazer
```

`UseLocalOsu` is enabled by default and references the sibling `osu` checkout. The `ppy.osu.Game` NuGet package
can lag behind osu!lazer releases, so building against the matching tag keeps the API version in sync.
Pass `-p:OsuSourcePath=/absolute/path/to/osu` to use a separate checkout when the sibling repository must remain read-only.

Release versions use `<OsuVersion>-<RulesetRevision>`, with the ruleset counter starting at zero for each
official osu! version. Assembly and file versions use the same four components separated by dots.

Build the ruleset in Release configuration:

```bash
cd BmsRuleset
dotnet build osu.Game.Rulesets.BmsRuleset -c Release
```

The output is `osu.Game.Rulesets.BmsRuleset/bin/Release/net10.0/osu.Game.Rulesets.BmsRuleset.dll`. Install it using the
[README installation steps](../README.md#installation).

## Mod Icons

The maintained icon generator is [scripts/bms-icon-font-generator](../scripts/bms-icon-font-generator/README.md).
Use the repository generator when updating the font.

## Gameplay Performance

See the [gameplay diagnostic (中文)](gameplay-diagnostic.zh-CN.md) for reusable capture instructions.

With benchmark approval, the gauge history group measures recording with fresh and reused buffers, live
Auto Gauge score population, rewind, and score export. The advancing Player group includes `tap-ag` and
`overlap-ag` workloads for measuring Auto Gauge through gameplay. Each benchmark uses two warmup runs and
seven measured samples; set `BMS_PERFORMANCE_RESULTS` to save its JSON reports.

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~BmsGaugeHistoryPerformanceTest'
```

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~TestSceneBmsInvertGameplay|FullyQualifiedName~TestSceneBmsReplayRewind|FullyQualifiedName~TestSceneBmsPauseRewind'
```

## Not Yet Implemented

| Area          | What is missing                                                                          | Priority |
|---------------|------------------------------------------------------------------------------------------|----------|
| **Audio**     | `#WAVCMD` (MacBeat) — pitch/volume/playback-time per WAV slot                            |
| **Audio**     | `#EXWAVxx` (nanasi) — pan/volume/frequency per WAV file                                  |
| **Input**     | Analogue turntable-axis input; see [current scratch controls](./gameplay.md#input-and-key-bindings) for bindable directions |
| **Mods**      | DP only mods (FLIP / BATTLE / SP -> DP / SYNCHRONIZE RANDOM / SYMMETRY RANDOM)           |          |
| **Parser**    | `#@BGAxx` — 9-field BGA crop with destination width/height; only 7-field `#BGAxx` is parsed | 2        |
| **Parser**    | `#SWBGAxx` — switchable BGA definition                                                   | 3        |
| **Parser**    | `#ARGBxx` — ARGB color/alpha definition for BGA elements                                 | 3        |
| **Parser**    | `#EXBMPxx` — extended BMP definition slot                                                | 3        |
| **Parser**    | `#POORBGAxx` — per-slot POOR BGA crop definition (distinct from scalar `#POORBGA` mode)  | 2        |
| **Parser**    | `#BGAEXPAND` — global BGA scaling mode (0=stretch, 1=keep aspect, 2=no expand)           | 2        |
| **Parser**    | `#BGAOFF` — disable BGA for the chart                                                    | 2        |
| **Parser**    | [BMSON support](plans/bmson-support-plan.md)                                             | 1        |
| **Parser**    | `#BMPxx` / `#EXBMPxx` — image definitions (non-resource-scan)                            |
| **Parser**    | `#CDDA` — CD audio                                                                       |
| **Parser**    | `#CHARFILE` / `#ExtChr` — character / skin                                               |
| **Parser**    | `#EXBPMxx` — `#BPMxx` alias (BMSC parser bug workaround)                                 |
| **Parser**    | `#MATERIALS` / `#MATERIALSWAV` / `#MATERIALSBMP` / `#DIVIDEPROP` — resource groups       |
| **Parser**    | `#OCT/FP` — octave / pedal                                                               |
| **Parser**    | `#OPTION` — forced option                                                                |
| **Parser**    | `#PATH_WAV` / `#PATH_BMP` — resource path prefixes                                       |
| **Parser**    | `#MOVIE` — metadata only                                                                 |          |
| **Parser**    | `#STP` — absolute STOP sequence                                                          |
| **Parser**    | `#VIDEOFILE` / `#VIDEOf/s` / `#VIDEOCOLORS` / `#VIDEODLY` / `#SEEKxx` — video |
| **Parser**    | `#CHARSET` — character encoding specification                                            |
| **Parser**    | Channel `17` / `27` — free-zone keys                                                     |
| **Parser**    | Channel `A6` / `#CHANGEOPTIONxx` — dynamic option changes                                |
| **Renderer**  | Configurable POOR BGA duration — fixed at 500 ms; beatoraja uses `misslayerDuration`     | 3        |
| **Scoring**   | 24KEYS / 24KEYS DOUBLE judgement profile matching beatoraja `KEYBOARD`                   | 3        |
| **Skin**      | `HitGreat` → `HitGreatSlow` / `HitGreatFast` split images                                |
| **Skin**      | E-POOR judgement image                                                                   | 3        |
| **UI**        | Lane cover / skin / movement                                                             | 2        |
