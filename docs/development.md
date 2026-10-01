# Development

[Back to README](../README.md)

## Design Plans

- [Online leaderboard plan (中文)](online-leaderboard-plan.zh-CN.md): osu! OAuth, full replay uploads, and SQLite storage. Design only; not implemented.
- [HitObject implementation scheme (中文)](hitobject-refactor-plan.zh-CN.md): [execution order and Git baselines](hitobject-refactor-plan.zh-CN.md#implementation-order) define the workflow: commit the existing work, squash the refactor history, remove transitional layers, implement the complete core, compare and adapt it against old Git revisions, then adapt Mods. The [target architecture](hitobject-refactor-plan.zh-CN.md#target-structure), [Git comparison procedure](hitobject-refactor-plan.zh-CN.md#git-comparison), [validation commands](hitobject-refactor-plan.zh-CN.md#verification-commands) and [completion criteria](hitobject-refactor-plan.zh-CN.md#completion) describe the final implementation. No bounded task-card architecture or production legacy/new switch is required for intermediate testing.

  [P2](hitobject-refactor-p2-validation.zh-CN.md), [P3](hitobject-refactor-p3-validation.zh-CN.md) and [checkpoint/lifecycle validation](hitobject-refactor-p3a0-validation.zh-CN.md) retain historical fixtures, observations and reproduction commands. Their old next-task instructions are superseded by the implementation scheme. Historical test passes do not establish completion of the final architecture.

### HitObject implementation status

The current engine and column contexts own input state, selection and part of the judgement state/indexes. Long notes still use the shared controller, and optional test compositions exercise new output and scheduling boundaries. Drawables and containers still participate in lifecycle and judgement forwarding; landmine state and independent scheduling are not fully migrated. These are current code facts, not an intermediate architecture to preserve.

The implementation scheme replaces these paths with independent Note/LN/CN/HCN/landmine rules, engine-owned state and scheduling, synchronous framework result adapters and read-only presentation. Complete core implementation precedes cross-version behaviour comparison and adaptation. AutoScratch and other affected Mods are considered only after that core comparison and adaptation are complete; their old sampling behaviour does not prescribe core interfaces.

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
