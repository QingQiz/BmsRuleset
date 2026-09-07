using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.Mods;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;

public sealed partial class BmsLampDisplay : CompositeDrawable
{
    private readonly Sprite baseFill;
    private readonly Sprite flashFill;

    private BmsLamp lamp;

    internal Action? SelectedModsChanged { private get; init; }

    [Resolved(CanBeNull = true)]
    private IBindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

    public BmsLamp Lamp
    {
        get => lamp;
        set
        {
            if (lamp == value)
                return;

            lamp = value;
            updateVisuals();
        }
    }

    public BmsLampDisplay(BmsLamp lamp)
        : this(lamp, null)
    {
    }

    // Transparent panels need a shaped fill because an opaque cover would change their background.
    internal BmsLampDisplay(BmsLamp lamp, Texture? texture)
    {
        Size = new Vector2(40, 20);
        Masking = false;

        baseFill = texture == null ? new Box() : new Sprite { Texture = texture };
        flashFill = texture == null ? new Box() : new Sprite { Texture = new Texture(texture) };
        baseFill.RelativeSizeAxes = flashFill.RelativeSizeAxes = Axes.Both;
        baseFill.Size = flashFill.Size = Vector2.One;
        flashFill.Alpha = 0;
        InternalChildren = [baseFill, flashFill];

        this.lamp = lamp;
        updateVisuals();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (selectedMods != null)
            selectedMods.ValueChanged += selectedModsChanged;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (selectedMods != null)
            selectedMods.ValueChanged -= selectedModsChanged;

        base.Dispose(isDisposing);
    }

    private void selectedModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> _) => SelectedModsChanged?.Invoke();

    protected override void Update()
    {
        base.Update();

        switch (lamp)
        {
            // beatoraja default skin cycles two-frame lamp atlas cells every 60 ms (cycle=50 for failed,
            // cycle=100 for the other flashing lamps; two frames each, so the period is 2 × cycle).
            case BmsLamp.Failed:
            case BmsLamp.ExHardClear:
            case BmsLamp.Max:
            case BmsLamp.Perfect:
            case BmsLamp.FullCombo:
                flashFill.Alpha = syncedPulse(0.15f, 0.65f, 60);
                break;

            default:
                flashFill.Alpha = 0;
                break;
        }
    }

    private void updateVisuals()
    {
        baseFill.Colour = baseColourFor(lamp);
        flashFill.Colour = flashColourFor(lamp);
    }

    private float syncedPulse(float min, float max, double period)
    {
        var phase = Time.Current % period / period;
        var wave = (float)((1 + Math.Sin(phase * Math.PI * 2)) / 2);
        return min + (max - min) * wave;
    }

    private static Color4 baseColourFor(BmsLamp lamp) => lamp switch
    {
        BmsLamp.NoPlay => new Color4(40, 44, 48, 255),
        BmsLamp.Failed => new Color4(233, 47, 10, 255),
        BmsLamp.AssistClear => new Color4(206, 1, 214, 255),
        BmsLamp.LightAssistClear => new Color4(221, 162, 223, 255),
        BmsLamp.EasyClear => new Color4(86, 202, 67, 255),
        BmsLamp.Clear => new Color4(245, 199, 88, 255),
        BmsLamp.HardClear => new Color4(248, 247, 245, 255),
        BmsLamp.ExHardClear => new Color4(239, 253, 9, 255),
        BmsLamp.Max or BmsLamp.Perfect or BmsLamp.FullCombo
            => new Color4(255, 255, 255, 255),
        _ => Color4.White,
    };

    private static Color4 flashColourFor(BmsLamp lamp) => lamp switch
    {
        // beatoraja default skin lamp.png atlas, second frame of the two-frame flashing cells.
        BmsLamp.Failed => new Color4(15, 3, 0, 255),
        BmsLamp.ExHardClear => new Color4(253, 9, 9, 255),
        BmsLamp.FullCombo => new Color4(9, 250, 253, 255),
        BmsLamp.Perfect => new Color4(63, 255, 77, 255),
        BmsLamp.Max => new Color4(255, 235, 66, 255),
        _ => Color4.Transparent,
    };
}
