# Custom HUD Component Guide

How to create, skin, and configure ruleset-specific HUD components for the osu! skin editor.

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Step 1: Make a Component Skin-Editable](#step-1-make-a-component-skin-editable)
- [Step 2: Expose Properties in the Inspector](#step-2-expose-properties-in-the-inspector)
- [Step 3: Define the Default HUD Layout](#step-3-define-the-default-hud-layout)
- [Step 4: Disable Built-in HUD Components](#step-4-disable-built-in-hud-components)
- [Advanced: Custom Property Controls](#advanced-custom-property-controls)
- [Step 5: Receive Data from the Playfield](#step-5-receive-data-from-the-playfield)
- [Reference: Skin Transformer HUD Patterns](#reference-skin-transformer-hud-patterns)

---

## Architecture Overview

The skin editor discovers HUD components through two mechanisms:

1. **Assembly scanning** — `SerialisedDrawableInfo.GetAllAvailableDrawables(RulesetInfo?)`
   scans the ruleset's assembly for all **public** non-abstract types implementing
   `ISerialisableDrawable`. These appear in the editor's **"Components (RulesetName)"** toolbox.
   Global components (from `osu.Game`) appear in a separate **"Components"** toolbox.

2. **Skin transformer resolution** — When the game loads, `HUDOverlay` creates two
   `SkinnableContainer` instances keyed by `GlobalSkinnableContainerLookup`:
   - `(MainHUDComponents, ruleset: null)` — global HUD
   - `(MainHUDComponents, ruleset: yourRulesetInfo)` — ruleset-scoped HUD

   The skin's `GetDrawableComponent` is called with these lookups. If no user-saved layout
   exists, the built-in skin (e.g. ArgonSkin) or your skin transformer provides the default.

```
SkinEditor toolbox
  → SerialisedDrawableInfo.GetAllAvailableDrawables(ruleset)
  → Shows all ISerialisableDrawable types from ruleset assembly

SkinEditor places component
  → SkinnableContainer.Add(component)
  → Skin serializes the layout via SkinLayoutInfo

Gameplay loads
  → HUDOverlay creates SkinnableContainer per lookup
  → SkinnableContainer.Reload()
  → UserSkinComponentLookup → SkinTransformer → ISkinSource → ISkin
     Each level can intercept/transform the lookup
  → Result is a Container with children
  → SkinnableContainer adds children
```

---

## Step 1: Make a Component Skin-Editable

Every skinnable HUD component must implement `ISerialisableDrawable`.

```csharp
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.MyGame.UI;

public sealed partial class MyCustomHud : CompositeDrawable, ISerialisableDrawable
{
    // Required by ISerialisableDrawable
    public bool UsesFixedAnchor { get; set; }

    public MyCustomHud()
    {
        Anchor = Anchor.TopRight;
        Origin = Anchor.TopRight;
        AutoSizeAxes = Axes.Both;
    }

    // ... your component logic ...
}
```

**Requirements:**
- Class must be **public** (assembly scanning discovers it)
- Must implement `ISerialisableDrawable` (two members: `UsesFixedAnchor` + `CopyAdjustedSetting` which has a default impl)
- Must be a `Drawable` subclass (`CompositeDrawable`, `Container`, etc.)
- Can optionally set `IsEditable => false` to hide from the editor toolbox

Once this class exists in your ruleset assembly, it automatically appears in the skin
editor's **"Components (YourRuleset)"** panel.

### Resolving Dependencies

If your component needs dependencies that aren't available when the editor instantiates
it, it will be silently skipped (`DependencyNotRegisteredException` is caught).

For editor-only dependencies, check `SkinComponentToolbox.attemptAddComponent` in
`SkinComponentToolbox.cs:59-66`. Use `[Resolved]` with `[BackgroundDependencyLoader]`
as normal — the editor creates a minimal DI scope.

---

## Step 2: Expose Properties in the Inspector

Tag public bindable properties with `[SettingSource]` to make them editable in the skin
editor's property panel when the component is selected.

### Auto-Detected Control Types

| Bindable Type          | UI Control              |
|------------------------|-------------------------|
| `BindableFloat`        | `SettingsSlider<float>` |
| `BindableDouble`       | `SettingsSlider<double>`|
| `Bindable<int>`        | `SettingsSlider<int>`   |
| `Bindable<bool>`       | `SettingsCheckbox`      |
| `Bindable<string>`     | `SettingsTextBox`       |
| `BindableColour4`      | `SettingsColour` picker |
| `Bindable<YourEnum>`   | `SettingsEnumDropdown<T>` |

### Example

```csharp
using osu.Framework.Bindables;
using osu.Game.Configuration;

public sealed partial class MyCustomHud : CompositeDrawable, ISerialisableDrawable
{
    public bool UsesFixedAnchor { get; set; }

    // → Slider: 0.0 to 1.0
    [SettingSource("Opacity", "Controls the opacity of the overlay")]
    public BindableFloat Opacity { get; } = new BindableFloat(0.8f)
    {
        MinValue = 0, MaxValue = 1, Precision = 0.01f
    };

    // → Checkbox
    [SettingSource("Show label", "Whether to show the label text")]
    public Bindable<bool> ShowLabel { get; } = new BindableBool(true);

    // → Color picker
    [SettingSource("Accent colour", "The main colour of the overlay")]
    public BindableColour4 AccentColour { get; } = new BindableColour4(Color4.White);

    // → Dropdown (enum)
    [SettingSource("Alignment", "Anchor position")]
    public Bindable<Anchor> Alignment { get; } = new Bindable<Anchor>();

    // → Text input
    [SettingSource("Label text", "Custom label")]
    public Bindable<string> LabelText { get; } = new Bindable<string>("default");
}

public enum MyDisplayMode { Simple, Detailed, Full }
```

### Controlling Property Order

Pass an `int` as the third parameter (or use the `(label, description, orderPosition)`
constructor). Lower values appear first. Unordered properties come after all ordered ones.

```csharp
[SettingSource("Primary", "Appears first", 0)]
public BindableFloat Primary { get; }

[SettingSource("Secondary", "Appears second", 1)]
public BindableFloat Secondary { get; }
```

### Using Localised Strings

Instead of raw strings, reference static members of a string resource class:

```csharp
[SettingSource(typeof(SkinnableComponentStrings), nameof(SkinnableComponentStrings.ShowLabel))]
public Bindable<bool> ShowLabel { get; }
```

### Wiring Bindables to Visuals

Subscribe to changes in `LoadComplete` or use `BindTarget`:

```csharp
[BackgroundDependencyLoader]
private void load()
{
    InternalChild = new SpriteText();
}

protected override void LoadComplete()
{
    base.LoadComplete();
    Opacity.BindValueChanged(v => Alpha = v.NewValue, true);
    Alignment.BindValueChanged(v =>
    {
        Anchor = v.NewValue;
        Origin = v.NewValue;
    }, true);
}
```

---

## Step 3: Define the Default HUD Layout

The default layout for the ruleset-scoped HUD is defined in your **skin transformer's**
`GetDrawableComponent` override. This is where you decide which components appear by
default and where they are placed.

### In a Built-in Skin Transformer

Return a `DefaultSkinComponentsContainer` for the ruleset-scoped HUD lookup:

```csharp
// BmsBuiltInSkinTransformer.cs
public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
{
    if (lookup is GlobalSkinnableContainerLookup
        {
            Lookup: GlobalSkinnableContainers.MainHUDComponents,
            Ruleset: not null
        })
    {
        return new DefaultSkinComponentsContainer(container =>
        {
            foreach (var d in container.OfType<ISerialisableDrawable>())
                d.UsesFixedAnchor = true;
        })
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new MyCustomHud(),
                new DrawableGameplayLeaderboard(),
                // ... other ruleset-specific HUD components
            },
        };
    }

    return base.GetDrawableComponent(lookup);
}
```

### In a Legacy Skin Transformer

```csharp
// BmsLegacySkinTransformer.cs
public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
{
    if (lookup is GlobalSkinnableContainerLookup containerLookup
        && containerLookup.Lookup == GlobalSkinnableContainers.MainHUDComponents)
    {
        if (containerLookup.Ruleset != null)
        {
            return new DefaultSkinComponentsContainer(container =>
            {
                foreach (var d in container.OfType<ISerialisableDrawable>())
                    d.UsesFixedAnchor = true;
            })
            {
                Children = new Drawable[]
                {
                    new LegacyScoreCounter(),
                    new MyCustomHud(),
                    new LegacySongProgress(),
                    new BarHitErrorMeter
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.CentreLeft,
                        Rotation = -90,
                    },
                },
            };
        }

        // Global HUD — pass through or strip
        return base.GetDrawableComponent(lookup);
    }

    // ... other lookups
}
```

### The Dual-Container Model

`HUDOverlay` creates two `SkinnableContainer` instances:

| Container    | Lookup                                    | Content                          |
|-------------|-------------------------------------------|----------------------------------|
| Global HUD  | `(MainHUDComponents, ruleset: null)`      | Score, accuracy, combo, health   |
| Ruleset HUD | `(MainHUDComponents, ruleset: yourRuleset)`| Leaderboard, per-ruleset HUD    |

In your `GetDrawableComponent`:
- `Ruleset == null` → return the **global** HUD (built-in skin's components)
- `Ruleset != null` → return the **ruleset-specific** HUD (your components)

---

## Step 4: Disable Built-in HUD Components

There is **no attribute** to hide global components (score, accuracy, combo, health, etc.)
from the skin editor toolbox. Instead, intercept at runtime in your skin transformer.

### Strategy A: Replace the Ruleset HUD Entirely (Recommended)

Return a `DefaultSkinComponentsContainer` with only the components you want when
`Ruleset != null`. Global components remain in the global HUD but your ruleset-scoped
HUD replaces them:

```csharp
case GlobalSkinnableContainerLookup { Lookup: MainHUDComponents, Ruleset: not null }:
    return new DefaultSkinComponentsContainer(...)
    {
        Children = new Drawable[]
        {
            new MyCustomHud(),
            // NO health, score, accuracy — keep it minimal
        },
    };
```

If you return `null`, the `SkinnableContainer` will be empty (no ruleset HUD).

### Strategy B: Strip Specific Components from Global HUD

If you want the global HUD components but need to remove specific types (e.g. health
display), wrap the result with a filter that removes them at `LoadComplete`:

```csharp
// BmsBuiltInSkinTransformer.cs — pattern reference
public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
{
    // Global HUD: pass through but strip health
    if (lookup is GlobalSkinnableContainerLookup
        { Lookup: MainHUDComponents, Ruleset: null })
        return WithoutComponent<HealthDisplay>(base.GetDrawableComponent(lookup));

    // Ruleset HUD: replace entirely
    if (lookup is GlobalSkinnableContainerLookup
        { Lookup: MainHUDComponents, Ruleset: not null })
        return null; // or return your own HUD container

    return base.GetDrawableComponent(lookup);
}
```

The stripping wrapper defers removal to `LoadComplete` (built-in skins populate children
asynchronously), recursively scanning children:

```csharp
internal static Drawable? WithoutComponent<T>(Drawable? drawable)
    where T : Drawable
    => drawable is Container container
        ? new ComponentFilteredContainer<T>(container)
        : drawable;

private sealed partial class ComponentFilteredContainer<T> : Container
    where T : Drawable
{
    private readonly Container container;

    public ComponentFilteredContainer(Container container)
    {
        this.container = container;
        RelativeSizeAxes = Axes.Both;
        InternalChild = container;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        removeComponents(container);
    }

    private static void removeComponents(Container target)
    {
        for (var i = target.Children.Count - 1; i >= 0; i--)
        {
            var child = target.Children[i];

            if (child is T)
                target.Remove(child, true);
            else if (child is Container nested)
                removeComponents(nested);
        }
    }
}
```

---

## Advanced: Custom Property Controls

To use a custom UI control instead of the auto-detected one, set `SettingControlType`:

```csharp
[SettingSource("Corner radius", "Rounds the corners of the box",
    SettingControlType = typeof(SettingsPercentageSlider<float>))]
public BindableFloat CornerRadius { get; } = new BindableFloat(0.1f)
{
    MinValue = 0, MaxValue = 1, Precision = 0.01f
};
```

The custom control must:
1. Derive from `SettingsItem<T>` (directly or indirectly)
2. Have a public parameterless constructor
3. Accept the bindable via its `.Current` property

### Example: Custom Spinner Control

`SkinnableSprite` uses a `SpriteSelectorControl` that lists available skin textures.
You can create any `SettingsItem<T>` subclass:

```csharp
public partial class MyCustomSlider : SettingsItem<float>
{
    public MyCustomSlider()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        // Use RoundedSliderBar for the slider
        Add(new RoundedSliderBar<float>
        {
            RelativeSizeAxes = Axes.X,
            Height = 20,
            Current = Current,
        });
    }
}
```

---

## Reference: Skin Transformer HUD Patterns

| Ruleset     | Global HUD                 | Ruleset-scoped HUD            | Health Strip Method      |
|-------------|---------------------------|-------------------------------|--------------------------|
| **BMS**     | Strip health via filter   | Replace with minimal set      | `HealthFilteredHudContainer` |
| **Mania**   | Pass through unchanged    | Replace (combo+leaderboard+spectators) or return null | Replacement |
| **Taiko**   | Pass through unchanged    | Replace (combo+leaderboard+spectators) or return null | Replacement (not strip) |
| **osu!**    | Pass through unchanged    | Pass through unchanged        | N/A                      |

---

## Step 5: Receive Data from the Playfield

HUD components are created by the skin system in `HUDOverlay`'s `SkinnableContainer`, not
by the playfield. They live in a different branch of the visual tree. The correct
communication pattern is **pull, not push**: the HUD component resolves the playfield
(or other game state) from DI and subscribes to its data.

### 5.1 The Wrong Way (Current BMS Bug)

```csharp
// BmsPlayfield.cs — broken pattern
private readonly BmsTextHud? textHud = null;  // Always null!
// ...
textHud?.ShowScrollSpeed(ScrollSpeed, configuredScrollSpeed.Value);  // Never executes
```

The playfield should never hold a direct reference to a HUD component — the user may
replace or remove it via the skin editor.

### 5.2 The Correct Pattern: Resolve + Subscribe

#### Registration

The playfield needs to be discoverable via DI. Choose one approach:

**Option A — Attribute `[Cached]` on the playfield class:**

```csharp
// BmsPlayfield.cs
[Cached]
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
{
    public Bindable<double> ScrollSpeed { get; }
    // ...
}
```

**Option B — Manual cache in `DrawableRuleset.CreateChildDependencies` (if you can't
modify the playfield class):**

```csharp
// BmsDrawableRuleset.cs
protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
{
    var deps = new DependencyContainer(base.CreateChildDependencies(parent));
    deps.CacheAs(sampleStore);
    deps.CacheAs(Playfield);  // ← makes BmsPlayfield resolvable by HUD components
    return deps;
}
```

#### Resolution + Subscription

```csharp
// BmsTextHud.cs
public sealed partial class BmsTextHud : CompositeDrawable, ISerialisableDrawable
{
    [Resolved]
    private BmsPlayfield playfield { get; set; } = null!;

    public bool UsesFixedAnchor { get; set; }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // Subscribe to playfield state changes
        playfield.ScrollSpeed.BindValueChanged(speed =>
        {
            ShowScrollSpeed(speed.NewValue, configuredSpeed);
        }, true);
    }
}
```

### 5.3 What Data Comes From Where

| Data | Source | How HUD Gets It |
|---|---|---|
| Scroll speed, text events | `BmsPlayfield` (its own bindables) | `[Resolved] BmsPlayfield` + subscribe |
| Judgement results | `Playfield.NewResult` event (base class) | `[Resolved] BmsPlayfield` + `NewResult +=` |
| Score | `ScoreProcessor` | `[Resolved] ScoreProcessor` (cached at `Player.cs:264`) |
| Health | `HealthProcessor` | `[Resolved] HealthProcessor` (cached at `Player.cs:270`) |
| Timing, clocks | `IGameplayClock` / `IFrameStableClock` | `[Resolved]` (cached at `Player.cs:357-358`) |
| Scrolling info | `IScrollingInfo` | `[Resolved] IScrollingInfo` (cached at `Player.cs:258`) |
| Player state | `GameplayState` | `[Resolved] GameplayState` (cached at `Player.cs:287`) |
| HUD visibility | `HUDOverlay` | `[Resolved] HUDOverlay` (cached at `Player.cs:369`) |
| Input / key counters | `InputCountController` / `ClicksPerSecondController` | Via `ICanAttachHUDPieces` interface (see §5.5) |

### 5.4 Multiple Instances of the Same Bindable Type

If you need to expose multiple `Bindable<double>` values, use interface-keyed caching
so DI can distinguish them:

```csharp
// Marker interfaces
public interface IBmsScrollSpeedData { }
public interface IBmsTextEventData { }

// BmsPlayfield — registration
[Cached(Type = typeof(IBmsScrollSpeedData))]
public Bindable<double> ScrollSpeed { get; } = new();

[Cached(Type = typeof(IBmsTextEventData))]
public Bindable<string> CurrentText { get; } = new();

// BmsTextHud — resolution
[Resolved]
private IBmsScrollSpeedData scrollSpeedData { get; set; } = null!;  // → Bindable<double>

[Resolved]
private IBmsTextEventData textEventData { get; set; } = null!;      // → Bindable<string>
```

Or bundle them in a single dedicated class cached by interface:

```csharp
public interface IBmsHudState
{
    IBindable<double> ScrollSpeed { get; }
    IBindable<string> CurrentText { get; }
    event Action<string>? TextEvent;
}
```

### 5.5 Input-Related HUD: `ICanAttachHUDPieces`

For key counters and CPS displays, use the `ICanAttachHUDPieces` interface instead
of DI. `HUDOverlay.BindDrawableRuleset()` calls it automatically:

```csharp
// HUDOverlay.cs:361-370
protected virtual void BindDrawableRuleset(DrawableRuleset drawableRuleset)
{
    if (drawableRuleset is ICanAttachHUDPieces attachTarget)
    {
        attachTarget.Attach(InputCountController);
        attachTarget.Attach(clicksPerSecondController);
    }
}
```

`DrawableRuleset<TObject>` delegates to the input manager, which creates internal
listeners that count key presses. No manual subscription needed.

### Key Files for Reference

| File | Path | Purpose |
|------|------|---------|
| `ISerialisableDrawable.cs` | `osu.Game\Skinning\ISerialisableDrawable.cs` | Interface for skin-editable components |
| `SettingSourceAttribute.cs` | `osu.Game\Configuration\SettingSourceAttribute.cs` | Attribute + control factory |
| `SerialisedDrawableInfo.cs` | `osu.Game\Skinning\SerialisedDrawableInfo.cs` | Assembly scanning for toolbox |
| `DefaultSkinComponentsContainer.cs` | `osu.Game\Skinning\DefaultSkinComponentsContainer.cs` | Default layout container |
| `SkinSettingsToolbox.cs` | `osu.Game\Overlays\SkinEditor\SkinSettingsToolbox.cs` | Property panel renderer |
| `SkinComponentToolbox.cs` | `osu.Game\Overlays\SkinEditor\SkinComponentToolbox.cs` | Component palette |
| `SkinEditor.cs` | `osu.Game\Overlays\SkinEditor\SkinEditor.cs` | Editor with selection logic |
| `SkinnableContainer.cs` | `osu.Game\Skinning\SkinnableContainer.cs` | Container for skinnable components |
| `ArgonJudgementCounterDisplay.cs` | `osu.Game\Skinning\Components\ArgonJudgementCounterDisplay.cs` | Full [SettingSource] example |
| `BarHitErrorMeter.cs` | `osu.Game\Screens\Play\HUD\HitErrorMeters\BarHitErrorMeter.cs` | Enum + slider properties example |
| `BoxElement.cs` | `osu.Game\Skinning\Components\BoxElement.cs` | Custom control + color example |
