using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.UI;

internal sealed class BmsAliveDrawableIndex
{
    private readonly Dictionary<int, List<DrawableBmsHitObject>> drawablesByColumn = new();
    private readonly Dictionary<DrawableBmsHitObject, int> columnsByDrawable = new();

    public void Add(BmsHitObject hitObject, DrawableBmsHitObject drawable)
    {
        Remove(drawable);

        if (!drawablesByColumn.TryGetValue(hitObject.Column, out var drawables))
            drawablesByColumn[hitObject.Column] = drawables = [];

        drawables.Add(drawable);
        columnsByDrawable[drawable] = hitObject.Column;
    }

    public void Remove(DrawableBmsHitObject drawable)
    {
        if (!columnsByDrawable.Remove(drawable, out var column))
            return;

        if (!drawablesByColumn.TryGetValue(column, out var drawables))
            return;

        drawables.Remove(drawable);

        if (drawables.Count == 0)
            drawablesByColumn.Remove(column);
    }

    public IReadOnlyList<DrawableBmsHitObject> GetColumn(int column)
        => drawablesByColumn.TryGetValue(column, out var drawables) ? drawables : ArraySegment<DrawableBmsHitObject>.Empty;
}
