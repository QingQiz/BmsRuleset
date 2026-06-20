using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

/// <summary>
///     External-facing view of a BMS column, exposing only the members
///     consumed by <see cref="BmsPlayfield"/> and mods.
/// </summary>
public interface IBmsColumn
{
    int ColumnIndex { get; }

    bool IsScratch { get; }

    HitObjectContainer HitObjectContainer { get; }

    Container HitExplosionArea { get; }

    bool Hidden { get; set; }
}
