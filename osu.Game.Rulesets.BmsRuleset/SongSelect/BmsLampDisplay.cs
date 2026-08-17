using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.Mods;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public sealed partial class BmsLampDisplay : CompositeDrawable
{
    private readonly Box baseFill;
    private readonly Box flashFill;

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

    // Slab constructor used by the song-select patch: a label-less background that lets the
    // panel's two rounded corners define the lamp shape. `masking` defaults to true but is
    // passed as false in song select so the full-size lamp doesn't allocate its own frame
    // buffer on top of TopLevelContent's.
    public BmsLampDisplay(BmsLamp lamp)
    {
        Size = new Vector2(40, 20);
        Masking = false;

        InternalChildren =
        [
            baseFill = new Box
            {
                RelativeSizeAxes = Axes.Both,
            },
            flashFill = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
            },
        ];

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
            case BmsLamp.ExHardClear:
                flashFill.Alpha = syncedPulse(0.15f, 0.65f, 210);
                break;

            case BmsLamp.Max:
            case BmsLamp.Perfect:
            case BmsLamp.FullCombo:
                flashFill.Alpha = syncedPulse(0.25f, 0.75f, 180);
                break;

            case BmsLamp.Failed:
                flashFill.Alpha = irregularPulse();
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

    private float irregularPulse()
    {
        var first = (1 + Math.Sin(Time.Current / 73)) / 2;
        var second = (1 + Math.Sin(Time.Current / 191 + 0.8)) / 2;
        return (float)(0.15 + 0.65 * first * second);
    }

    private static Color4 baseColourFor(BmsLamp lamp) => lamp switch
    {
        BmsLamp.NoPlay => new Color4(92, 96, 104, 255),
        BmsLamp.Failed => new Color4(160, 20, 28, 255),
        BmsLamp.AssistClear => new Color4(150, 72, 220, 255),
        BmsLamp.EasyClear => new Color4(65, 185, 80, 255),
        BmsLamp.Clear => new Color4(45, 120, 230, 255),
        BmsLamp.HardClear => new Color4(235, 235, 235, 255),
        BmsLamp.ExHardClear => new Color4(245, 210, 40, 255),
        BmsLamp.Max or BmsLamp.Perfect or BmsLamp.FullCombo
            => new Color4(40, 135, 255, 255),
        _ => Color4.White,
    };

    private static Color4 flashColourFor(BmsLamp lamp) => lamp switch
    {
        BmsLamp.Failed => new Color4(255, 40, 40, 255),
        BmsLamp.ExHardClear => new Color4(220, 35, 20, 255),
        BmsLamp.Max or BmsLamp.Perfect or BmsLamp.FullCombo
            => Color4.White,
        _ => Color4.Transparent,
    };
}
