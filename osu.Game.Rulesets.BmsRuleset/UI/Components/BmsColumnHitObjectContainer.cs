using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsColumnHitObjectContainer : HitObjectContainer
{

    private readonly BmsPlayfield playfield;

    public BmsColumnHitObjectContainer(BmsPlayfield playfield)
    {
        this.playfield = playfield;
        RelativeSizeAxes = Axes.Both;
    }

    /// <summary>
    ///     Re-compute lifetimes for every entry in this container.
    ///     Triggered on load completion and whenever the user adjusts the scroll speed.
    /// </summary>
    public void RefreshAllEntries()
    {
        foreach (var entry in Entries)
        {
            if (entry is BmsHitObjectLifetimeEntry bmsEntry)
                bmsEntry.RefreshLifetime();
        }
    }

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();

        var currentScrollPos = playfield.CurrentScrollPosition;
        var hitTarget = playfield.Stage.HitTargetPosition;
        var scale = playfield.ScrollSpeedMultiplier / Math.Max(1.0, playfield.ScrollRange)
                    * Math.Max(1f, DrawHeight - hitTarget);

        // Position all alive entries in this column
        foreach (var entry in AliveEntries)
        {
            if (entry.Value is not DrawableBmsHitObject note)
                continue;

            var offset = (float)((note.HitObject!.ScrollPositionAtStartTime - currentScrollPos) * scale);
            var y = -(hitTarget + offset);

            note.Y = y;

            if (note is ILongNoteHolder ln)
            {
                var endOffset = (float)((note.HitObject.ScrollPositionAtEndTime - currentScrollPos) * scale);

                // Clamp the tail so it never extends in the "past" direction past the judgement line:
                //   normal scroll (positive factor) → tail should not go below the hit target
                //   reverse scroll (negative factor) → tail should not go above the hit target
                var clampedEndOffset = playfield.ChartScrollFactor >= 0
                    ? Math.Max(endOffset, 0)
                    : Math.Min(endOffset, 0);

                ln.UpdateBodyGeometry(y, -(hitTarget + clampedEndOffset));
            }
        }
    }
}
