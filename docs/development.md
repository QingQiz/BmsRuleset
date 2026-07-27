# Development

[Back to README](../README.md)

## Build Manually

Install [Git](https://git-scm.com/) and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), then clone
this repository and osu! into sibling directories under any working directory. The sparse checkout skips the large
test-song folder:

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

The project enables `UseLocalOsu` by default and automatically references the sibling `osu` checkout. This is needed
because ppy often does not update the `ppy.osu.Game` NuGet package at the same time as an osu!lazer release. Building
against the matching local tag keeps the referenced API and osu! version in sync without waiting for a NuGet update.

Build the ruleset in Release configuration:

```bash
cd BmsRuleset
dotnet build osu.Game.Rulesets.BmsRuleset -c Release
```

The output is `osu.Game.Rulesets.BmsRuleset/bin/Release/net8.0/osu.Game.Rulesets.BmsRuleset.dll`. Install it using the
[README installation steps](../README.md#installation).

## Not Yet Implemented

| Area          | What is missing                                                                          | Priority |
|---------------|------------------------------------------------------------------------------------------|----------|
| **Audio**     | `#WAVCMD` (MacBeat) — pitch/volume/playback-time per WAV slot                            |
| **Audio**     | `#EXWAVxx` (nanasi) — pan/volume/frequency per WAV file                                  |
| **Converter** | Mania 7K → BMS chart conversion                                                          | 3        |
| **Input**     | Scratch turntable semantics — scratch is routed as a plain column key                    |
| **Input**     | Judgement offset adjustment capability                                                   |
| **Mods**      | DP only mods (FLIP / BATTLE / SP -> DP / SYNCHRONIZE RANDOM / SYMMETRY RANDOM)           |          |
| **Parser**    | `#@BGAxx` — extended BGA crop with dest w/h (9 fields); only 7-field `#BGAxx` parsed     | 2        |
| **Parser**    | `#SWBGAxx` — switchable BGA definition                                                   | 3        |
| **Parser**    | `#ARGBxx` — ARGB color/alpha definition for BGA elements                                 | 3        |
| **Parser**    | `#EXBMPxx` — extended BMP definition slot                                                | 3        |
| **Parser**    | `#POORBGAxx` — per-slot POOR BGA crop definition (distinct from scalar `#POORBGA` mode)  | 2        |
| **Parser**    | `#BGAEXPAND` — global BGA scaling mode (0=stretch, 1=keep aspect, 2=no expand)           | 2        |
| **Parser**    | `#BGAOFF` — disable BGA for the chart                                                    | 2        |
| **Parser**    | BMSON support                                                                            | 1        |
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
| **Renderer**  | POOR BGA duration hardcoded 500ms (beatoraja uses config-driven `misslayerDuration`)     | 3        |
| **Scoring**   | 24KEYS / 24KEYS DOUBLE judgement profile matching beatoraja `KEYBOARD`                   | 3        |
| **Scoring**   | Course constraints that alter judgement windows, including NO_GOOD/NO_GREAT              | 4        |
| **Scoring**   | beatoraja non-default judge algorithms: Duration, Lowest, Score                          | 4        |
| **Skin**      | Non-legacy BMS skin — fully configurable via skin editor                                 |
| **Skin**      | `HitGreat` → `HitGreatSlow` / `HitGreatFast` split images                                |
| **Skin**      | E-POOR judgement image                                                                   | 3        |
| **UI**        | Lane cover / skin / movement                                                             | 2        |
| **Perf**      | fps is not stable when a large amount of mine disposed                                   | 4        |

### FIXME

```text
2026-06-01 15:13:14 [error]: osu.Game.Rulesets.UI.BeatmapInvalidForRulesetException:
  Beatmap can not be converted for the ruleset
  (ruleset: osu.Game.Rulesets.Mania.ManiaRuleset, osu.Game.Rulesets.Mania,
   converter: osu.Game.Rulesets.Mania.Beatmaps.ManiaBeatmapConverter).
  at osu.Game.Beatmaps.WorkingBeatmap.GetPlayableBeatmap(...)
  at osu.Game.Screens.Select.BeatmapTitleWedge.DifficultyDisplay.<>c__DisplayClass36_0
       .<updateCountStatistics>b__0()
```

Switching the active ruleset from BMS to any other crashes with
`BeatmapInvalidForRulesetException` because the beatmap title wedge tries to recalculate
difficulty using the wrong converter while the carousel selection is stale.
