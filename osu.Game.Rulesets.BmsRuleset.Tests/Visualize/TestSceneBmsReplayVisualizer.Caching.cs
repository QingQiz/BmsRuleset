#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsReplayVisualizer
{
    private IEnumerable<BmsReplayVisualizer.TimelineRectangle> cachedRectangles()
    {
        _ = visualizer.Plot.GeometryVersion;
        return visualizer.Plot.Timeline!.Blocks.SelectMany(block => block.Rectangles);
    }

    [Test]
    public void TestTimelineSnapshotsReuseUnchangedBlocksAndKeepSlotsIndependent()
    {
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        BmsReplayVisualizer.PlotLayer layer = null!;
        var view = new BmsReplayVisualizer.TimelineView { Time = 1000, Range = 4, Size = new Vector2(240, 360) };
        var block = new BmsReplayVisualizer.TimelineBlock();
        var frame = 0UL;
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("add cached layer", () =>
        {
            for (var i = 0; i < 2048; i++)
                block.Rectangles.Add(new BmsReplayVisualizer.TimelineRectangle(i % 8 * 30, 24, i, i, 0, 0,
                    BmsReplayVisualizer.TimelineKind.Note, i % 2 == 0 ? Colour4.Cyan : Colour4.Red));
            block.Commit();
            view.Blocks.Add(block);
            Player.HUDOverlay.Add(layer = new BmsReplayVisualizer.PlotLayer { Size = view.Size, Timeline = view, Alpha = 0.5f });
        });
        AddUntilStep("layer loaded", () => layer.IsLoaded);
        AddStep("reuse time-only snapshots", () =>
        {
            Player.GameplayClockContainer.Stop();
            layer.UpdateSubTree();
            layer.UpdateSubTreeMasking();
            var snapshots = Enumerable.Range(0, 3).Select(snapshot).ToArray();
            var copied = snapshots.Select(node => node.TimelineCopiedRectangles).ToArray();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                view.Time += 0.25;
                layer.Invalidate(Invalidation.DrawNode);
                snapshot(i % 3);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(32000));
            for (var slot = 0; slot < 3; slot++)
            {
                Assert.That(snapshots[slot].TimelineCopiedRectangles, Is.EqualTo(copied[slot]));
                Assert.That(snapshots[slot].QuadCount, Is.EqualTo(2048));
            }
            var previous = snapshots[0].GetTimelineBlock(0).ToArray();
            block.Rectangles[0] = block.Rectangles[0] with { Colour = Colour4.Yellow };
            block.Commit();
            layer.Invalidate(Invalidation.DrawNode);
            var changed = snapshot(1);
            Assert.That(changed.GetTimelineBlock(0)[0].Colour.Linear.A, Is.EqualTo(0.5f).Within(0.001));
            Assert.That(changed.GetTimelineBlock(0)[0].Colour, Is.Not.EqualTo(previous[0].Colour));
            Assert.That(snapshots[0].GetTimelineBlock(0), Is.EqualTo(previous));
            layer.Colour = ColourInfo.GradientHorizontal(Colour4.Cyan, Colour4.White);
            view.Blocks.Clear();
            layer.Timeline = null;
            layer.Commit();
            Assert.That(snapshot(2).QuadCount, Is.Zero);
            Assert.That(snapshots[0].GetTimelineBlock(0), Is.EqualTo(previous));
        });

        BmsReplayVisualizer.PlotLayer.PlotDrawNode snapshot(int slot)
            => (BmsReplayVisualizer.PlotLayer.PlotDrawNode)generate(layer, ++frame, slot, false)!;
    }

    [Test]
    public void TestCachedHudKeepsGeometryAcrossTimeAndUpdatesJudgements()
    {
        BmsReplayVisualizer.TimelineBlock[] blocks = [];
        long[] versions = [];
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("add HUD", () => Player.HUDOverlay.Add(visualizer = new BmsReplayVisualizer()));
        AddUntilStep("HUD loaded", () => visualizer.IsLoaded);
        seek(500);
        AddStep("remember cached blocks", () =>
        {
            _ = primitives();
            blocks = visualizer.Plot.Timeline!.Blocks.ToArray();
            versions = blocks.Select(block => block.Version).ToArray();
        });
        seek(501);
        AddAssert("time-only motion keeps geometry", () =>
            visualizer.Plot.Timeline!.Blocks.SequenceEqual(blocks) && blocks.Select(block => block.Version).SequenceEqual(versions));
        seek(1025);
        AddStep("judgement changes cached note colour", () =>
        {
            var expected = primitives().First(rectangle => rectangle.Kind == BmsReplayVisualizer.PlotElementKind.Note).Colour;
            var note = visualizer.Plot.Timeline!.Blocks.SelectMany(block => block.Rectangles)
                .Single(rectangle => rectangle.Kind == BmsReplayVisualizer.TimelineKind.Note && rectangle.TopTime == 1000);
            Assert.That(note.Colour, Is.EqualTo(expected));
            Assert.That(note.Colour, Is.Not.EqualTo(Colour4.White));
        });
        seek(500);
        AddAssert("rewind restores future note colour", () => visualizer.Plot.Timeline!.Blocks.SelectMany(block => block.Rectangles)
            .Where(rectangle => rectangle.Kind == BmsReplayVisualizer.TimelineKind.Note).All(rectangle => rectangle.Colour == Colour4.White));
    }
}
