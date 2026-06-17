using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public partial class BmsColumnHitObjectContainer : HitObjectContainer
{
    private readonly BmsColumn column;

    public BmsColumnHitObjectContainer(BmsColumn column)
    {
        this.column = column;
        RelativeSizeAxes = Axes.Both;
    }

    protected override void UpdateAfterChildrenLife()
    {
        base.UpdateAfterChildrenLife();

        var playfield = column.ParentPlayfield ?? (column.ParentPlayfield = this.FindClosestParent<BmsPlayfield>());
        if (playfield == null)
            return;

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

            if (note is DrawableBmsLongNote ln)
            {
                var endOffset = (float)((note.HitObject.ScrollPositionAtEndTime - currentScrollPos) * scale);

                // Clamp the tail so it never extends in the "past" direction past the judgement line:
                //   normal scroll (positive factor) → tail should not go below the hit target
                //   reverse scroll (negative factor) → tail should not go above the hit target
                float clampedEndOffset;
                if (playfield.ChartScrollFactor >= 0)
                    clampedEndOffset = Math.Max(endOffset, 0);
                else
                    clampedEndOffset = Math.Min(endOffset, 0);

                ln.UpdateBodyGeometry(y, -(hitTarget + clampedEndOffset));
            }
        }
    }
}
