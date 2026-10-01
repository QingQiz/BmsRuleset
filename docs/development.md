# Development

[Back to README](../README.md)

## Design Plans

- [Online leaderboard plan (中文)](online-leaderboard-plan.zh-CN.md): osu! OAuth, full replay uploads, and SQLite storage. Design only; not implemented.
- [HitObject implementation scheme (中文)](hitobject-refactor-plan.zh-CN.md): [remaining work](hitobject-refactor-plan.zh-CN.md#implementation-order) covers complete core and Mod migration, followed by test migration, Git-based behaviour comparison and final acceptance. See the [target architecture](hitobject-refactor-plan.zh-CN.md#target-structure), [Git baselines and comparison procedure](hitobject-refactor-plan.zh-CN.md#git-comparison), [validation commands](hitobject-refactor-plan.zh-CN.md#verification-commands) and [completion criteria](hitobject-refactor-plan.zh-CN.md#completion).

### HitObject implementation status

Implementation now proceeds through the complete core, Mods and production consumers before the remaining tests are migrated and run. Intermediate commits do not require temporary compatibility layers for old tests. Git comparisons and performance acceptance remain final requirements.

Independent LN/CN/HCN rules, typed runtime state, operation bindings, landmine rules and the data-driven multi-column frame entry are connected to production. Drawable bindings and lifecycle notifications belong to the output adapter; the core column context no longer depends on a Drawable, container or graphical clock. Each drawable composes its own graphics. Long-note visual observations now have separate generation-aware registrations, and layout preparation reads the sampled head position and direction without advancing their history. The shared update prepares bounds, applies culling, then applies visible graphics; long notes reuse their prepared endpoints. Post-result scheduling uses the engine's epoch and simulation step per binding. Both the ruleset and the complete test project build in Release with no warnings or errors. The earlier 634 passing checks apply only to lifecycle checkpoint `e1de0257`; runtime verification of the final migration is still pending.

The no-automation scheduling recorder matched the unchanged frozen keyboard and replay traces at the earlier checkpoint. Note, LN, CN and HCN execution tests compare the framework and recording outputs directly, including pending tails, synthetic endpoints, body ticks, scratch timing and PMS rewind. Remaining checkpoint, input, Mod and presentation test consumers now use the final APIs; the old Controller recorder is removed. The shared recorder also observes automatic scheduling, preserves the original AS-1 fixture and compares ordered output between ordinary and deferred visual traversal. The full scheduling and Git comparison matrix remains to be verified. See the [current implementation entry](hitobject-refactor-plan.zh-CN.md#implementation-start) for scope and the [Git baselines](hitobject-refactor-plan.zh-CN.md#git-baselines) for preserved versions.

AutoScratch and its registration are restored using the simulation engine and object identities. Automatic heads and tails run after the physical-input batch; HCN samples the existing automatic hold before new endpoints. Bindings reset automatic holding, rewind restores it from retained rule state, and the next simulation step resumes a rebound hold. This intentionally removes the old AS-1 dependency on deferred visual traversal. Autoplay already feeds ordinary replay actions to the same engine.

The core now also owns the deferred hold plan and rewind coordination. Resume visibility belongs to the long-note presentation, while the generic container reports lifecycle boundaries and applies common layout. Framework history and index notifications retain their two separate synchronous checkpoints through the output adapter; synthetic PMS penalty decisions are made in the core. Empty-press keysounds query core activation/results, and unused column input forwarding and drawable-query aliases are removed. Production and test-consumer migration now compile together using existing read-only sibling binaries. Filtered runtime tests, isolated Git comparisons and approved performance measurements are the next validation steps.

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

For reusable real Player profiling across charts, skins and long-note modes, see [Gameplay performance template (中文)](gameplay-diagnostic.zh-CN.md).

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
| **Parser**    | [BMSON support](bmson-support-plan.md)                                                   | 1        |
| **Parser**    | `#BMPxx` / `#EXBMPxx` — image definitions (non-resource-scan)                            |
| **Parser**    | `#CDDA` — CD audio                                                                       |
| **Parser**    | `#CHARFILE` / `#ExtChr` — character / skin                                               |
| **Parser**    | `#EXBPMxx` — `#BPMxx` alias (BMSC parser bug workaround)                                 |
| **Parser**    | `#EXWAVxx`, `#WAVCMD` — advanced audio controls                                          |
| **Parser**    | `#MATERIALS` / `#MATERIALSWAV` / `#MATERIALSBMP` / `#DIVIDEPROP` — resource groups       |
| **Parser**    | `#OCT/FP` — octave / pedal                                                               |
| **Parser**    | `#OPTION` — forced option                                                                |
| **Parser**    | `#PATH_WAV` / `#PATH_BMP` — resource path prefixes                                       |
| **Parser**    | `#MOVIE` — metadata only                                                                 |          |
| **Parser**    | `#STP` — absolute STOP sequence                                                          |
| **Parser**    | `#VIDEOFILE` / `#VIDEOf/s` / `#VIDEOCOLORS` / `#VIDEODLY` / `#MOVIE` / `#SEEKxx` — video |
| **Parser**    | `#CHARSET` — character encoding specification                                            |
| **Parser**    | `#ExtChr` — BM98 extended character sprite display                                       |
| **Parser**    | Channel `17` / `27` — free-zone keys                                                     |
| **Parser**    | Channel `A6` / `#CHANGEOPTIONxx` — dynamic option changes                                |
| **Renderer**  | Configurable POOR BGA duration — fixed at 500 ms; beatoraja uses `misslayerDuration`     | 3        |
| **Scoring**   | 24KEYS / 24KEYS DOUBLE judgement profile matching beatoraja `KEYBOARD`                   | 3        |
| **Skin**      | `HitGreat` → `HitGreatSlow` / `HitGreatFast` split images                                |
| **Skin**      | E-POOR judgement image                                                                   | 3        |
| **UI**        | Lane cover / skin / movement                                                             | 2        |
| **Perf**      | Frame spikes during mass gameplay object activation and pool return                       | 4        |
