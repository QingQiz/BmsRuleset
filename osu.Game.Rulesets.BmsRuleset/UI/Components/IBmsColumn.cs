using osu.Framework.Graphics;
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

    Drawable KeyArea { get; }

    Container KeyAreaUnderNotesLayer { get; }

    Container HitExplosionArea { get; }

    bool Hidden { get; set; }

    void TriggerHitExplosion(bool isLongNote, bool isHold = false);

    void PlaySample(string samplePath);

    PressOutcome HandlePress(double time);

    void HandleRelease(double time);
}
