using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

/// <summary>
///     External-facing view of a BMS column, exposing only the members
///     consumed by <see cref="BmsPlayfield"/> and mods.
/// </summary>
public interface IBmsColumn
{
    int ColumnIndex { get; }

    bool IsScratch { get; }

    HitObjectContainer HitObjectContainer { get; }

    Drawable KeyArea { get; }

    Container KeyAreaUnderNotesLayer { get; }

    Container HitExplosionArea { get; }

    bool Hidden { get; set; }

    void TriggerHitExplosion(BmsHitLightStyle style);

    void PlaySample(ushort? sampleKey, int volume);

}
