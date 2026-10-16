using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    private static int visibilityQueries;

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(4)]
    public void MineTemplatePreservesHitObjectSearchAndRemoval(int templateIndex)
    {
        var beatmap = attachBeatmap(new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 });
        var playfield = new BmsPlayfield(beatmap);
        var column = (BmsColumn)playfield.Stage.Columns[1];
        var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
        var compare = typeof(BmsColumnHitObjectContainer).GetMethod("Compare", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Comparison<Drawable>>(container);
        var children = (IReadOnlyList<Drawable>)typeof(CompositeDrawable).GetProperty("InternalChildren", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(container)!;
        for (var i = 0; i < 128; i++)
            container.Add(new BmsHitObjectLifetimeEntry(new BmsLandmine { StartTime = 5000 + i, Column = 1 }, playfield.ScrollController, () => 0));

        var drawables = new List<OrderingDrawable>();
        for (var i = 0; i < 8; i++)
        {
            if (i == templateIndex)
                container.InitializeMineBatch(column);

            var drawable = new OrderingDrawable(new BmsNote { StartTime = 5000 + i * 10, Column = 1, Beatmap = beatmap });
            drawables.Add(drawable);
            container.Add(drawable);

            for (var first = 0; first < children.Count; first++)
            for (var second = first + 1; second < children.Count; second++)
                Assert.That(compare(children[first], children[second]), Is.LessThan(0), "Every pair must agree with the stored order so binary search can remove pooled children.");
        }

        foreach (var drawable in drawables)
            Assert.That(container.IndexOf(drawable), Is.GreaterThanOrEqualTo(0), "Every resident note must remain searchable regardless of template insertion order.");
        Assert.That(drawables.OrderBy(container.IndexOf).Select(d => d.HitObject.StartTime), Is.Ordered.Descending);
        foreach (var drawable in drawables)
        {
            Assert.That(container.Remove(drawable), Is.True);
            Assert.That(container.IndexOf(drawable), Is.LessThan(0));
        }
    }

    private sealed partial class OrderingDrawable : DrawableHitObject
    {
        internal OrderingDrawable(HitObject note) => Entry = new HitObjectLifetimeEntry(note);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReverseScrollMarkersRetainEarlierVisibleIntervals(bool invisible)
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [],
            [new BmsScrollEvent(768, -1, 0), new BmsScrollEvent(1536, 1, 1)], [], 120);
        BmsHitObject marker = invisible ? new BmsInvisibleNote() : new BmsLandmine();
        marker.StartTime = 24000;
        marker.Column = 1;
        marker.ScrollPositionAtStartTime = map.GetScrollPositionAtTime(marker.StartTime);
        var beatmap = attachBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            TimingMap = map,
            HitObjects = [marker],
        });
        var controller = new BmsGameplayScrollController(map);
        var container = new BmsColumnHitObjectContainer(controller, () => 124.8f) { RelativeSizeAxes = Axes.None, Height = 768 };
        var entry = new BmsHitObjectLifetimeEntry(marker, controller, () => 0);
        container.Add(entry);
        container.RefreshAllEntries();

        foreach (var time in new[] { 8000, 16000, 23000, 16000, 8000, 0, 23000 })
        {
            controller.Update(time);
            container.UpdateScrollEntries();
            var visible = time is 8000 or 23000;
            Assert.That(entry.LifetimeStart <= time, Is.EqualTo(visible), $"Marker at {time}ms must follow the viewport through reversals and seeks.");
            if (visible)
                Assert.That(entry.LifetimeEnd, Is.EqualTo(marker.StartTime + (invisible ? 1 : 100)));
        }
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void MarkerIndexMatchesViewportAcrossSpeedAndScrollModes(double speed)
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], [], [new BmsSpeedEvent(0, speed, 0)], 120);
        var controller = new BmsGameplayScrollController(map);
        var container = new BmsColumnHitObjectContainer(controller, () => 124.8f) { RelativeSizeAxes = Axes.None, Height = 768 };
        var entries = Enumerable.Range(0, 200).Reverse().Select(i =>
        {
            var mine = new BmsLandmine { StartTime = 100000 + i, ScrollPositionAtStartTime = i * 1000 };
            var entry = new BmsHitObjectLifetimeEntry(mine, controller, () => 0);
            container.Add(entry);
            return entry;
        }).ToArray();
        container.RefreshAllEntries();

        foreach (var constant in new[] { false, true, false })
        foreach (var time in new[] { 0, 50000, 100000, 103000, 103001, 160000, 50000, 0 })
        {
            controller.ConstantScrollActive = constant;
            controller.SoftConstant.Value = true;
            controller.Update(time);
            container.UpdateScrollEntries();
            var scale = Math.Abs(controller.ScrollCoordinateScale(768, 124.8f));
            foreach (var entry in entries)
            {
                var position = constant ? entry.HitObject.StartTime : ((BmsHitObject)entry.HitObject).ScrollPositionAtStartTime;
                var inView = Math.Abs(position - controller.CurrentScrollPosition) * scale <= 1536;
                var end = entry.HitObject.StartTime + 100;
                Assert.That(entry.LifetimeStart <= time && entry.LifetimeEnd > time, Is.EqualTo(inView && time < end));
            }
        }

        var removed = entries[0];
        Assert.That(container.Remove(removed), Is.True);
        container.UpdateScrollEntries();
        Assert.That(container.Entries, Does.Not.Contain(removed));
        container.Add(removed);
        container.RefreshAllEntries();
        controller.Update(removed.HitObject.StartTime);
        container.UpdateScrollEntries();
        Assert.That(container.Entries, Does.Contain(removed));
    }

    [Test]
    public void HiddenMarkerRetainsChangedExpiryAndVisualOffset()
    {
        var controller = new BmsGameplayScrollController(null) { ConstantScrollActive = true };
        var container = new BmsColumnHitObjectContainer(controller, () => 124.8f);
        var offset = 0d;
        var mine = new BmsLandmine { StartTime = 10000 };
        var entry = new BmsHitObjectLifetimeEntry(mine, controller, () => offset);
        container.Add(entry);
        container.RefreshAllEntries();
        controller.Update(0);
        container.UpdateScrollEntries();
        entry.LifetimeEnd = 10050;
        offset = 500;
        container.ApplyVisualOffsetToAllEntries();
        Assert.That(entry.LifetimeStart, Is.GreaterThan(mine.StartTime));
        controller.Update(10000 + offset);
        container.UpdateScrollEntries();
        Assert.That(entry.LifetimeStart, Is.LessThanOrEqualTo(10000));
        Assert.That(entry.LifetimeEnd, Is.EqualTo(10050));
    }

    [TestCase(500)]
    [TestCase(240000)]
    [TestCase(3600000)]
    [NonParallelizable]
    public void MonotonicLifetimeSearchDoesNotScanTheEntireSong(double time)
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], 120);
        var note = new BmsNote { StartTime = time, Column = 1, ScrollPositionAtStartTime = time };
        var beatmap = attachBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            TimingMap = map,
            HitObjects = [note],
        });
        var entry = new BmsHitObjectLifetimeEntry(beatmap.HitObjects[0], new BmsGameplayScrollController(map), () => 0);
        using var probe = new ScopedMethodProbe(typeof(BmsTimingMap).GetMethod(nameof(BmsTimingMap.GetScrollPositionAtTime)),
            typeof(BmsGameplayVirtualisationTest).GetMethod(nameof(countVisibilityQuery), BindingFlags.Static | BindingFlags.NonPublic));
        visibilityQueries = 0;
        entry.RefreshLifetime();

        Assert.That(entry.LifetimeStart, Is.EqualTo(time - BmsDrawableRuleset.ComputeScrollTime(8)).Within(1));
        Assert.That(visibilityQueries, Is.LessThan(100), "Visibility search should depend on the visible interval, not song length.");
    }

    private static void countVisibilityQuery() => visibilityQueries++;
}
