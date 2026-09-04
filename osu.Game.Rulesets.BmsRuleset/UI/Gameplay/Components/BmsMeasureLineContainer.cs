using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsMeasureLineContainer : Container
{

    internal readonly record struct MeasureLineInfo(double ScrollPosition, double Time);

    private const double past_margin = 0;
    private const double future_margin = 500;

    private readonly Dictionary<int, BmsMeasureLine> activeLines = new();
    private readonly Stack<BmsMeasureLine> pooledLines = new();
    private readonly List<int> removalBuffer = [];

    private MeasureLineInfo[] lines = [];
    private bool scrollPositionsMonotonic = true;
    private BmsGameplayScrollController? scrollController;
    private BmsStage? stage;

    public BmsMeasureLineContainer()
    {
        Masking = true;
    }

    internal void SetTimingMap(BmsTimingMap? timingMap, BmsGameplayScrollController owner, BmsStage ownerStage)
    {
        Clear(false);
        activeLines.Clear();
        pooledLines.Clear();

        scrollController = owner;
        stage = ownerStage;

        if (timingMap == null)
        {
            lines = [];
            scrollPositionsMonotonic = true;
            return;
        }

        lines = timingMap.Measures
            .Where(m => m.Index > 0)
            .Select(m => new MeasureLineInfo(timingMap.GetVisualScrollPositionAtTick(m.StartTick),
                timingMap.ProjectTickToTime(m.StartTick)))
            .ToArray();

        scrollPositionsMonotonic = isMonotonic(lines);
    }

    protected override void Update()
    {
        base.Update();

        if (scrollController == null || stage == null || lines.Length == 0)
            return;

        var current = scrollController.CurrentScrollPosition;
        var future = scrollController.MeasureLineFutureWindow + future_margin;
        var min = current - past_margin;
        var max = current + future;

        if (scrollController.ConstantScrollActive)
            updateRangeByTime(min, max);
        else if (scrollPositionsMonotonic)
            updateRangeByScroll(min, max);
        else
            updateRangeByScan(min, max);
    }

    private static bool isMonotonic(IReadOnlyList<MeasureLineInfo> source)
    {
        for (var i = 1; i < source.Count; i++)
        {
            if (source[i].ScrollPosition < source[i - 1].ScrollPosition)
                return false;
        }

        return true;
    }

    private void updateRangeByTime(double min, double max)
    {
        var first = lowerBoundByTime(min);
        var last = lowerBoundByTime(max);
        syncActiveRange(first, last, static line => line.Time);
    }

    private void updateRangeByScroll(double min, double max)
    {
        var first = lowerBoundByScroll(min);
        var last = lowerBoundByScroll(max);
        syncActiveRange(first, last, static line => line.ScrollPosition);
    }

    private void updateRangeByScan(double min, double max)
    {
        removalBuffer.Clear();

        foreach (var (index, _) in activeLines)
        {
            var value = lines[index].ScrollPosition;
            if (value < min || value > max)
                removalBuffer.Add(index);
        }

        foreach (var index in removalBuffer)
            hideLine(index);

        for (var i = 0; i < lines.Length; i++)
        {
            var value = lines[i].ScrollPosition;
            if (value >= min && value <= max)
                showLine(i);
        }
    }

    private void syncActiveRange(int first, int last, Func<MeasureLineInfo, double> valueSelector)
    {
        removalBuffer.Clear();

        foreach (var index in activeLines.Keys)
        {
            if (index < first || index >= last)
                removalBuffer.Add(index);
        }

        foreach (var index in removalBuffer)
            hideLine(index);

        for (var i = first; i < last && i < lines.Length; i++)
        {
            var value = valueSelector(lines[i]);
            if (double.IsFinite(value))
                showLine(i);
        }
    }

    private void showLine(int index)
    {
        if (activeLines.ContainsKey(index) || scrollController == null || stage == null)
            return;

        var line = pooledLines.Count > 0 ? pooledLines.Pop() : new BmsMeasureLine();
        line.Apply(lines[index], scrollController, stage);
        activeLines[index] = line;
        Add(line);
    }

    private void hideLine(int index)
    {
        if (!activeLines.Remove(index, out var line))
            return;

        Remove(line, false);
        pooledLines.Push(line);
    }

    private int lowerBoundByTime(double value)
    {
        var low = 0;
        var high = lines.Length;

        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (lines[mid].Time < value)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }

    private int lowerBoundByScroll(double value)
    {
        var low = 0;
        var high = lines.Length;

        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (lines[mid].ScrollPosition < value)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
