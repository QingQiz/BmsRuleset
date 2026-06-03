# Mod Development Guide

How to create, register, and wire osu! ruleset mods.

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Mod Patterns](#mod-patterns)
  - [Pattern A: Thin Wrapper](#pattern-a-thin-wrapper)
  - [Pattern B: Beatmap Conversion](#pattern-b-beatmap-conversion)
  - [Pattern C: Playfield-Aware](#pattern-c-playfield-aware)
  - [Pattern D: Autoplay / Replay](#pattern-d-autoplay--replay)
- [Registration](#registration)
- [Framework Interfaces Reference](#framework-interfaces-reference)
- [Exposing Settings](#exposing-settings)
- [Checklist: Adding a New Mod](#checklist-adding-a-new-mod)

---

## Architecture Overview

Mods extend `Mod` (or a framework base like `ModAutoplay`, `ModNoFail`) and implement
one or more `IApplicable*` interfaces. The framework discovers and applies them
automatically during gameplay setup.

```
Mod class { }                         // define properties (Name, Acronym, Type, etc.)
  + implements IApplicable*            // one or more framework interfaces
    → framework calls Apply*()          // at the appropriate lifecycle stage
      → mod configures the target       // converter / drawable ruleset / playfield / player
```

**Key rule:** every mod's behaviour must be encapsulated in the mod class itself.
Do not leak mod-specific logic into the playfield, drawable ruleset, or other UI
code. Use the appropriate `IApplicable*` interface to inject behaviour from the mod.

---

## Mod Patterns

### Pattern A: Thin Wrapper

For mods that need no custom behaviour beyond what the framework base class already
provides. Create a ruleset-scoped subclass so the type system can distinguish it.

```csharp
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mods;

public class RulesetModNoFail : ModNoFail
{
}
```

No additional wiring needed. The mod inherits everything (score multiplier, fail
override, rate adjustment, replay generation) from its base class.

**Use when:** wrapping `ModNoFail`, `ModHalfTime`, `ModDoubleTime`, `ModCinema<T>`, or
similar framework mods that need no ruleset-specific behaviour.

---

### Pattern B: Beatmap Conversion

For mods that modify the beatmap during the conversion phase (column mirroring, layout
switching, difficulty tweaks). Implement `IApplicableToBeatmapConverter`.

```csharp
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mods;

public class RulesetModConversion : Mod, IApplicableToBeatmapConverter
{
    public override string Name => "My Conversion";
    public override string Acronym => "MC";
    public override LocalisableString Description => "Modifies the beatmap during conversion.";
    public override ModType Type => ModType.Conversion;
    public override double ScoreMultiplier => 1;

    public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
    {
        // Cast to your ruleset's converter type and set flags.
        // Conversion happens before gameplay, no per-frame logic possible.
    }
}
```

**When it runs:** during beatmap conversion, before the playfield is created.

**Common uses:** mirroring, layout variant switching, difficulty adjustments that must
be baked into the beatmap before gameplay starts.

---

### Pattern C: Playfield-Aware

For mods that affect live gameplay (auto-input, visibility changes, input blocking).
These need access to the playfield and per-frame update calls.

Implement **two** framework interfaces:

| Interface | When Called | Purpose |
|---|---|---|
| `IApplicableToDrawableRuleset<T>` | Once, after playfield creation | Get the playfield reference, configure flags |
| `IUpdatableByPlayfield` | Every frame (from `Playfield.Update()`) | Run gameplay logic against the playfield |

```csharp
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Mods;

public class RulesetModPlayfieldAware : Mod, IApplicableToDrawableRuleset<HitObject>, IUpdatableByPlayfield
{
    // ... Name, Acronym, Description, Type, ScoreMultiplier ...

    private Playfield playfield = null!;

    public void ApplyToDrawableRuleset(DrawableRuleset<HitObject> drawableRuleset)
    {
        playfield = drawableRuleset.Playfield;
        // Configure playfield flags, register hooks, etc.
    }

    public void Update(Playfield _)
    {
        // Per-frame logic operating on playfield.
        // Runs every frame via Playfield.Update() → IUpdatableByPlayfield.
    }
}
```

**Rules:**

- Get the playfield via `drawableRuleset.Playfield` — never query mods with
  `Mods.OfType<T>()` in UI code.
- Put per-frame gameplay logic in `IUpdatableByPlayfield.Update()`, not in
  `Playfield.Update()`.
- Do not pass mod state as constructor parameters to the playfield. Let the mod
  set properties directly via `IApplicableToDrawableRuleset<T>`.

**Common uses:** auto-scratch/auto-input, hidden modifiers, flashlight, relax.

---

### Pattern D: Autoplay / Replay

For mods that generate replay data (autoplay, cinema). Extend `ModAutoplay` and
override `CreateReplayData`.

Also implement `IApplicableToDrawableRuleset<T>` to inform the playfield that
autoplay is active (the playfield needs to know to skip player-input logic).

```csharp
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Mods;

public class RulesetModAutoplay : ModAutoplay, IApplicableToDrawableRuleset<HitObject>
{
    public void ApplyToDrawableRuleset(DrawableRuleset<HitObject> drawableRuleset)
    {
        // Set an IsAutoplay flag on the playfield so it skips input checks.
    }

    public override ModReplayData CreateReplayData(IBeatmap beatmap, IReadOnlyList<Mod> mods)
    {
        // Generate replay frames using your ruleset's auto-generator.
        return new ModReplayData(replay, new ModCreatedUser { Username = "autoplay" });
    }
}
```

**Common uses:** autoplay (showcase), cinema (background only), practice mods that
simulate inputs.

---

## Registration

Mods are registered in the ruleset's `GetModsFor(ModType)` override:

```csharp
public override IEnumerable<Mod> GetModsFor(ModType type) => type switch
{
    ModType.DifficultyReduction => [new ModA(), new ModB()],
    ModType.DifficultyIncrease => [new ModC()],
    ModType.Automation => [new ModAutoplay(), new ModCinema()],
    ModType.Conversion => [new ModMirror(), new ModLayoutSwitch()],
    ModType.System => [new ModInternal()],
    _ => [],
};
```

| `ModType` | UI Category |
|---|---|
| `DifficultyReduction` | Difficulty Reduction |
| `DifficultyIncrease` | Difficulty Increase |
| `Automation` | Automation |
| `Conversion` | Conversion |
| `System` | System (hidden from normal selection) |

A mod's `Type` property must match its bucket in `GetModsFor`. For System mods, also
set `UserPlayable = false`, `ValidForMultiplayer = false` to hide from the UI.

---

## Framework Interfaces Reference

| Interface | Method | When Called | Typical Use |
|---|---|---|---|
| `IApplicableMod` | *(marker)* | — | Mod needs no special application |
| `IApplicableToBeatmapConverter` | `ApplyToBeatmapConverter(IBeatmapConverter)` | During beatmap conversion | Set flags on the beatmap converter |
| `IApplicableToDrawableRuleset<TObject>` | `ApplyToDrawableRuleset(DrawableRuleset<TObject>)` | After playfield creation | Configure playfield, store reference |
| `IApplicableToPlayer` | `ApplyToPlayer(Player)` | During player setup | Hook into game-wide components |
| `IApplicableToScoreProcessor` | `ApplyToScoreProcessor(ScoreProcessor)` | During score processor setup | Override scoring logic |
| `IApplicableToHealthProcessor` | `ApplyToHealthProcessor(HealthProcessor)` | During health processor setup | Override health/gauge logic |
| `IApplicableToHitObject` | `ApplyToHitObject(HitObject)` | Per hit object during conversion | Modify individual hit objects |
| `IApplicableToDrawableHitObject` | `ApplyToDrawableHitObject(DrawableHitObject)` | Per drawable after creation | Modify individual drawables |
| `IApplicableFailOverride` | `PerformFail()` / `RestartOnFail` | On health failure | Block or customise failure |
| `IApplicableToHUD` | `ApplyToHUD(HUDOverlay)` | During HUD setup | Modify HUD visibility or contents |
| `IUpdatableByPlayfield` | `Update(Playfield)` | Every frame | Per-frame gameplay logic |

### Application Order

```
IApplicableToBeatmapConverter
  → Playfield created (CreatePlayfield)
    → IApplicableToDrawableRuleset<TObject>
      → IApplicableToPlayer
        → IApplicableToScoreProcessor
        → IApplicableToHealthProcessor
          → IApplicableToHUD
            → IApplicableToDrawableHitObject (per drawable)
              → IUpdatableByPlayfield (every frame)
```

---

## Exposing Settings

Add configurable settings using `[SettingSource]` on bindable properties:

```csharp
using osu.Framework.Bindables;
using osu.Game.Configuration;

[SettingSource("Setting name", "Setting description")]
public Bindable<bool> MySetting { get; } = new();
```

Supported bindable types produce automatic UI controls:

| Bindable Type | UI Control |
|---|---|
| `Bindable<bool>` | Checkbox |
| `Bindable<int>` | Integer slider (use `MinValue`, `MaxValue`) |
| `BindableFloat` / `BindableDouble` | Float slider (use `MinValue`, `MaxValue`, `Precision`) |
| `Bindable<string>` | Text box |
| `BindableColour4` | Colour picker |
| `Bindable<YourEnum>` | Dropdown |

Values are persisted per-user by the framework automatically.

---

## Checklist: Adding a New Mod

1. Create the mod class in `Mods/` extending `Mod` or a framework base.
2. Override `Name`, `Acronym`, `Description`, `Type`, `ScoreMultiplier`.
3. Add `[SettingSource]` bindables if the mod has user-configurable options.
4. Implement the appropriate `IApplicable*` interfaces for the mod's behaviour.
5. Register the mod in `GetModsFor(ModType)` with the matching `ModType`.
6. For playfield flags: add a settable property to the playfield if one doesn't
   exist, then set it from `IApplicableToDrawableRuleset<T>.ApplyToDrawableRuleset()`.
7. For per-frame logic: implement `IUpdatableByPlayfield` — do not add mod logic
   to `Playfield.Update()`.
8. Ensure the mod is constructible and visible in the mod selector.
