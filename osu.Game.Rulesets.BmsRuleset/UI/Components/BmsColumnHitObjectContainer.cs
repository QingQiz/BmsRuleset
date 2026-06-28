using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Objects;
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

            if (note is ILongNoteHolder ln && note.HitObject is BmsLongNote longNote)
            {
                var endOffset = (float)((longNote.ScrollPositionAtEndTime - currentScrollPos) * scale);
                ln.UpdateBodyGeometry(y, -(hitTarget + endOffset));
            }

            note.UpdateColumnFrame();
        }
    }
}
