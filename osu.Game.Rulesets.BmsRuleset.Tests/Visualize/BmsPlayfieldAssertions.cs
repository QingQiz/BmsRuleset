#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Shared playfield introspection helpers used by visual scenes that
/// assert on note positions and spacing.
/// </summary>
public static class BmsPlayfieldAssertions
{
    /// <summary>
    /// Returns the first alive <see cref="DrawableBmsHitObject"/> at the given tick, or <c>null</c>.
    /// </summary>
    /// <param name="playfield">The playfield to query.</param>
    /// <param name="tick">Native BMS tick to match against <see cref="Objects.BmsHitObject.TickInfo"/>.</param>
    /// <param name="excludeLongNotes">If <c>true</c>, long notes are skipped — useful when a non-LN
    /// note shares its tick with an LN body and the test only cares about the tap.</param>
    public static DrawableBmsHitObject? GetAliveObjectAtTick(this BmsPlayfield playfield, long tick, bool excludeLongNotes = false)
    {
        // After the scrolling refactor, hit objects live in per-column BmsColumnHitObjectContainer
        // instances, not in the playfield's default (empty) HitObjectContainer.
        foreach (var column in playfield.Stage.Columns)
        {
            foreach (var (entry, d) in column.HitObjectContainer.AliveEntries)
            {
                if (d is DrawableBmsHitObject note &&
                    note.HitObject.TickInfo.Tick == tick &&
                    (!excludeLongNotes || note.HitObject is not BmsLongNote))
                {
                    return note;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Distance between the screen-space tops of the two notes at the given ticks,
    /// or <c>0</c> if either note is not currently alive.
    /// </summary>
    public static float SpacingBetweenTicks(this BmsPlayfield playfield, long firstTick, long secondTick, bool excludeLongNotes = false)
    {
        var first = playfield.GetAliveObjectAtTick(firstTick, excludeLongNotes);
        var second = playfield.GetAliveObjectAtTick(secondTick, excludeLongNotes);

        if (first == null || second == null)
            return 0;

        return Math.Abs(TopOf(first) - TopOf(second));
    }

    /// <summary>
    /// Screen-space Y of the playfield's judgement line.
    /// </summary>
    public static float JudgementLineY(this BmsPlayfield playfield)
    {
        var lineLocalY = playfield.Stage.DrawHeight - playfield.Stage.HitTargetPosition;
        return playfield.Stage.ToScreenSpace(new Vector2(0, lineLocalY)).Y;
    }

    public static IEnumerable<DrawableHitObject> AllColumnAliveObjects(this BmsPlayfield playfield)
        => playfield.Stage.Columns.SelectMany(c => c.HitObjectContainer.AliveEntries.Values);

    public static float TopOf(Drawable drawable) => drawable.ScreenSpaceDrawQuad.TopLeft.Y;

    public static float BottomOf(Drawable drawable) => drawable.ScreenSpaceDrawQuad.BottomLeft.Y;
}
