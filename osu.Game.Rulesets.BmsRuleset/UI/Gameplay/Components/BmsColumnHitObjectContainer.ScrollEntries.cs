using System;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnHitObjectContainer
{
    private readonly List<BmsHitObjectLifetimeEntry> scrollEntries = [];
    private bool scrollEntriesDirty = true;
    private bool scrollEntriesConstant;
    private int scrollEntryFirst;
    private int scrollEntryEnd;

    internal void UpdateScrollEntries()
    {
        updateMineBatch();
        if (scrollEntriesDirty || scrollEntriesConstant != scrollController.ConstantScrollActive)
        {
            scrollEntries.Clear();
            scrollEntriesConstant = scrollController.ConstantScrollActive;
            foreach (var entry in Entries)
            {
                if (entry is BmsHitObjectLifetimeEntry marker && entry.HitObject is BmsLandmine or BmsInvisibleNote)
                {
                    marker.SetScrollVisibility(false);
                    scrollEntries.Add(marker);
                }
            }

            scrollEntries.Sort((a, b) => scrollPosition(a).CompareTo(scrollPosition(b)));
            scrollEntryFirst = scrollEntryEnd = 0;
            scrollEntriesDirty = false;
        }

        if (scrollEntries.Count == 0)
            return;

        var frame = visualFrame();
        // Match the drawable's one-screen padding on both viewport edges. Zero SPEED
        // puts every marker at the same Y, so the full index must remain eligible.
        var distance = Math.Abs(frame.Scale) > 0 && double.IsFinite(frame.Scale)
            ? 2 * Math.Max(1, frame.Height) / Math.Abs(frame.Scale)
            : double.PositiveInfinity;
        var first = scrollBound(frame.ScrollPosition - distance, upper: false);
        var end = scrollBound(frame.ScrollPosition + distance, upper: true);

        // Only the changed edges touch lifetime management. Reverse scroll and replay
        // seeks can move either edge in either direction without scanning the chart.
        for (var i = scrollEntryFirst; i < Math.Min(first, scrollEntryEnd); i++)
            scrollEntries[i].SetScrollVisibility(false);
        for (var i = Math.Max(end, scrollEntryFirst); i < scrollEntryEnd; i++)
            scrollEntries[i].SetScrollVisibility(false);
        for (var i = first; i < Math.Min(scrollEntryFirst, end); i++)
            scrollEntries[i].SetScrollVisibility(true);
        for (var i = Math.Max(scrollEntryEnd, first); i < end; i++)
            scrollEntries[i].SetScrollVisibility(true);

        scrollEntryFirst = first;
        scrollEntryEnd = end;
    }

    private double scrollPosition(BmsHitObjectLifetimeEntry entry) => scrollEntriesConstant
        ? entry.HitObject.StartTime
        : ((BmsHitObject)entry.HitObject).ScrollPositionAtStartTime;

    private int scrollBound(double position, bool upper)
    {
        var low = 0;
        var high = scrollEntries.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            var value = scrollPosition(scrollEntries[middle]);
            if (value < position || (upper && value == position))
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
