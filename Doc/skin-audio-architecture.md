# Skin Audio Architecture

## Are beatmap resources (samples, music, etc.) provided through the skin system?

Yes. This is fully intentional osu! design, not an accident.

`WorkingBeatmapCache.GetSkin()` constructs a `LegacyBeatmapSkin` backed by a
`RealmBackedResourceStore` — every file in the beatmap folder. That skin is passed to
`RulesetSkinProvidingContainer` at `Player.cs:289` and inserted as the first (highest
priority) source in `BeatmapSkinProvidingContainer`. All children of the playfield see
beatmap files as the top of the `ISkinSource` chain.

`LegacyBeatmapSkin.GetSample()` serves those files directly:

```csharp
// LegacyBeatmapSkin.cs:92
public override ISample? GetSample(ISampleInfo sampleInfo)
{
    if (sampleInfo is HitSampleInfo hitSampleInfo && !hitSampleInfo.UseBeatmapSamples)
        return null;  // block standard hitsounds unless they are explicit per-note overrides

    return base.GetSample(sampleInfo);  // opens the .wav/.ogg from the beatmap folder
}
```

The gate is `HitSampleInfo.UseBeatmapSamples` — `true` only when a hitobject has an
explicit custom sample override (e.g. `0:0:0:0:kick.wav` in the `.osu`). Normal bank
hitsounds (`normal-hitnormal`, etc.) never reach the beatmap skin; they fall through to
the user skin or defaults.

There is a second gate at the container level: `BeatmapSkinProvidingContainer`
short-circuits the entire `LegacyBeatmapSkin` source when the user has the
"Beatmap Hitsounds" setting disabled (`OsuSetting.BeatmapHitsounds`).

---

## The complete lookup chain

```
Beatmap .wav/.ogg
  → RealmBackedResourceStore
  → Skin.Samples (ISampleStore via AudioManager.GetSampleStore)
  → LegacyBeatmapSkin.GetSample()       guarded by UseBeatmapSamples for HitSampleInfo
  → BeatmapSkinProvidingContainer        guarded by "Beatmap Hitsounds" user setting
  → ISkinSource from DI
  → PoolableSkinnableSample              standard hitsound path
    OR BmsDrawableRuleset / BmsChartSampleSound   BMS keysound/BGM path (see below)
```

---

## Why BmsRuleset bypasses the standard path for keysounds and BGM

`BmsSampleInfo` is not a `HitSampleInfo`, so the `UseBeatmapSamples` gate does not
apply — `base.GetSample()` would be reached through the normal chain. However,
BmsRuleset does not use `PoolableSkinnableSample`'s lookup path at all for chart audio.

`BmsDrawableRuleset` and `BmsChartSampleSound` instead scan `CurrentSkin.AllSources`
directly, unwrap any `SkinTransformer` layers, and call `GetSample()` only on
`LegacyBeatmapSkin` instances:

```csharp
// BmsDrawableRuleset.cs
private static LegacyBeatmapSkin? extractBeatmapSkin(ISkin skin) => skin switch
{
    LegacyBeatmapSkin s => s,
    SkinTransformer t => t.Skin as LegacyBeatmapSkin,
    _ => null,
};
```

Two reasons for this deliberate bypass:

1. **Source isolation** — guarantees BMS chart audio always comes from the beatmap's own
   files and never accidentally falls through to user skins or embedded skins.
2. **Volume routing** — BGM and keysounds are routed through master × track volume, not
   effect volume, per the AGENTS.md rule. Using `PoolableSkinnableSample` directly would
   subject them to the effect volume bus.

---

## Why skin.ini cannot be officially extended by a ruleset plugin

`LegacySkinDecoder` detects section headers by matching against the `LegacyDecoder.Section`
enum: `General`, `Editor`, `Metadata`, `Difficulty`, `Events`, `TimingPoints`, `Colours`,
`Fonts`, `CatchTheBeat`, `Mania`. Any `[SectionName]` not in that enum is logged as a
warning and its lines continue to be parsed under the **previous** section.

`[Mania]` works because `Section.Mania` is in the core enum and `LegacySkin.ParseConfigurationStream`
runs a dedicated second-pass decoder (`LegacyManiaSkinDecoder`). Both of those are core
modifications — not a plugin API.

What the osu! core actually provides:

| Mechanism | Type | What it offers |
|---|---|---|
| `Ruleset.CreateSkinTransformer` | `public virtual` | Query-time interception only; parsing is already done |
| `LegacySkin.ParseConfigurationStream` | `protected virtual` | Only reachable by subclassing `LegacySkin` — a ruleset plugin cannot do that |
| `SkinConfiguration.ConfigDictionary` | `Dictionary<string,string>` | Flat catch-all for unhandled keys from recognised sections; no section prefix, same-name keys from different sections overwrite each other |

**Conclusion:** A ruleset plugin cannot hook into skin.ini parsing without reflection.
The only sanctioned runtime hook is `CreateSkinTransformer`, which operates at query time
after parsing is complete.

### How BmsRuleset works around this

`BmsSkinConfigurationDecoder` bypasses `LegacySkinDecoder` entirely. It opens `skin.ini`
directly from the raw `IResourceStore<byte[]>` and parses `[BMS]` and `[Mania]` sections
in a single custom pass. For user skins (concrete `Skin` subclasses), the store is
obtained via reflection on the private `store` field — ugly but load-bearing until osu!
exposes a first-class `Resources` property on `Skin`. The TODO tracking this is in
`BmsSkinConfigurationDecoder.cs`.

For `BmsEmbeddedSkin`, no reflection is needed — `BmsEmbeddedSkin.Resources` is
`internal` and accessible directly.

---

## Key file references

| File | Lines | Purpose |
|---|---|---|
| `osu.Game/Beatmaps/WorkingBeatmapCache.cs` | 339–350 | `GetSkin()` → `new LegacyBeatmapSkin(...)` |
| `osu.Game/Skinning/LegacyBeatmapSkin.cs` | 36–49 | Constructor — creates `RealmBackedResourceStore` |
| `osu.Game/Skinning/LegacyBeatmapSkin.cs` | 92–98 | `GetSample()` — `UseBeatmapSamples` gate |
| `osu.Game/Skinning/LegacySkin.cs` | 581–603 | `GetSample()` — actual file lookup via `Samples.Get()` |
| `osu.Game/Skinning/Skin.cs` | 155–165 | `RecycleSamples()` — builds `ISampleStore` from resource store |
| `osu.Game/Audio/HitSampleInfo.cs` | 69–80 | `UseBeatmapSamples` field definition |
| `osu.Game/Screens/Play/Player.cs` | 289–292 | `RulesetSkinProvidingContainer` constructed with `Beatmap.Value.Skin` |
| `osu.Game/Skinning/RulesetSkinProvidingContainer.cs` | 57–66 | Wraps beatmap skin in `BeatmapSkinProvidingContainer` as first source |
| `osu.Game/Skinning/BeatmapSkinProvidingContainer.cs` | 27 | `AllowSampleLookup` → `BeatmapHitsounds` setting |
| `osu.Game/Skinning/PoolableSkinnableSample.cs` | 102 | `CurrentSkin.GetSample(sampleInfo)` — standard call site |
| `osu.Game/Skinning/LegacySkinDecoder.cs` | — | Parses skin.ini; unknown section headers fall to previous section |
| `osu.Game/Skinning/LegacyManiaSkinDecoder.cs` | — | Dedicated second-pass `[Mania]` section decoder |
| `BmsRuleset/.../BmsDrawableRuleset.cs` | 270–296 | `extractBeatmapSkin` + `resolveSample` for BGM |
| `BmsRuleset/.../BmsChartSampleSound.cs` | 193–243 | Same pattern for per-note keysounds |
| `BmsRuleset/.../DrawableBmsHitObject.cs` | 85–89, 241–247 | `PlayKeySound()` + `LoadSamples()` with `BmsSampleInfo` |
| `BmsRuleset/.../BmsSkinConfigurationDecoder.cs` | — | Custom skin.ini parser; reflection TODO |
