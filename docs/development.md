# Development

[Back to README](../README.md)

## Design Plans

- [Online leaderboard plan (中文)](online-leaderboard-plan.zh-CN.md): osu! OAuth, full replay uploads, and SQLite storage. Design only; not implemented.

## Build Manually

Install [Git](https://git-scm.com/) and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then clone
this repository and osu! into sibling directories. Sparse checkout skips the large test-song folder:

```bash
git clone --filter=blob:none --sparse https://github.com/QingQiz/BmsRuleset.git
git -C BmsRuleset sparse-checkout set --no-cone '/*' '!bms_test_songs'
git clone --filter=blob:none https://github.com/ppy/osu.git
```

Read `OsuBase` in `BmsRuleset/Directory.Build.props`, then check out the matching osu! tag. For example,
`OsuBase` `2026.711` requires:

```bash
git -C osu checkout 2026.711.0-lazer
```

`UseLocalOsu` is enabled by default and references the sibling `osu` checkout. The `ppy.osu.Game` NuGet package
can lag behind osu!lazer releases, so building against the matching tag keeps the API version in sync.

Build the ruleset in Release configuration:

```bash
cd BmsRuleset
dotnet build osu.Game.Rulesets.BmsRuleset -c Release
```

The output is `osu.Game.Rulesets.BmsRuleset/bin/Release/net8.0/osu.Game.Rulesets.BmsRuleset.dll`. Install it using the
[README installation steps](../README.md#installation).

## Gameplay Performance

For reusable real Player profiling across charts, skins and long-note modes, see [Gameplay performance template (中文)](gameplay-diagnostic.zh-CN.md).

- Visual culling must preserve passive misses, HCN health ticks and column-owned mine judgements. Long-note visibility must include the full head-to-tail span and a held head's pinned position; rewind and pool reuse must restore visual state.
- Pool prewarming estimates residence at the default scroll speed. Its weighted budget limits loading-time allocations; pools can still grow under STOP, reverse scrolling and custom speeds. It is not a runtime memory cap.
- Judgement tests must synchronise with `FrameStableClock`, not only the playback clock. Replay checkpoints stop, seek and wait for simulation; players without replay must catch up before stopping.
- IN can expand an ordinary chart into LN/CN/HCN workloads. Keep its random seed fixed and validate both endpoints against the playable, modded beatmap. Native LN charts remain necessary for tail samples and chart-specific declarations.
- Mine visibility searches use discrete time steps and can miss very short visible intervals. Synthetic test skins do not cover every custom skin's dimensions or animations, so unusual skins need explicit regression captures.

All active objects are still traversed each frame. Mass activation and pool return can cause spikes through object unbinding, subtree invalidation and allocation. Further optimisation should separate judgement lifetime from visual resource lifetime while preserving judgement and rewind timing.

Arbitrary backwards replay seeking has a separate known scoring limitation: historical input can generate Empty POORs, and synthetic CN/HCN tail results are not reverted with the drawable result stack. This reproduces before the performance changes by completing an IN chart, seeking to zero and replaying it. The IN performance captures and endpoint checks cover forward playback; pause/resume lead-in rewind has separate regression tests. A complete fix needs to coordinate input, synthetic results and in-progress long-note controller restoration.

To reproduce without a local chart, temporarily append these steps to `TestSceneBmsInvertGameplay.TestConvertedNotesHoldAndRelease`, after its first `assertCompleted(mode)`:

```csharp
seek(0);
AddStep("clear test result history before replay", () => Player.Results.Clear());
seek(1550);
assertHolding();
seek(5900);
assertCompleted(mode);
```

Rebuild the test project with `-p:BuildProjectReferences=false`, then run the silent headless fixture:

```powershell
dotnet test osu.Game.Rulesets.BmsRuleset.Tests/osu.Game.Rulesets.BmsRuleset.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~TestSceneBmsInvertGameplay&Name~Argon&Name~False'
```

The single-play fixture reproduces 24 Empty POOR results in all three modes, including with the production assembly from `afcc7f62`. A guard that suppresses judgements during negative elapsed time removes these but leaves CN/HCN with 200 Perfects instead of 136 because 64 synthetic tails were not reverted. That incomplete guard is not part of the implementation. Restore the forward-only fixture after investigating; this reproduction is deliberately not part of the passing regression suite.

## Not Yet Implemented

| Area          | What is missing                                                                          | Priority |
|---------------|------------------------------------------------------------------------------------------|----------|
| **Audio**     | `#WAVCMD` (MacBeat) — pitch/volume/playback-time per WAV slot                            |
| **Audio**     | `#EXWAVxx` (nanasi) — pan/volume/frequency per WAV file                                  |
| **Input**     | Turntable input — scratch currently behaves as a column key                    |
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
| **Parser**    | Channel `31`–`49` — invisible notes                                                      |
| **Parser**    | Channel `A6` / `#CHANGEOPTIONxx` — dynamic option changes                                |
| **Renderer**  | Configurable POOR BGA duration — fixed at 500 ms; beatoraja uses `misslayerDuration`     | 3        |
| **Scoring**   | 24KEYS / 24KEYS DOUBLE judgement profile matching beatoraja `KEYBOARD`                   | 3        |
| **Skin**      | `HitGreat` → `HitGreatSlow` / `HitGreatFast` split images                                |
| **Skin**      | E-POOR judgement image                                                                   | 3        |
| **UI**        | Lane cover / skin / movement                                                             | 2        |
| **Perf**      | Frame spikes during mass gameplay object activation and pool return                       | 4        |
