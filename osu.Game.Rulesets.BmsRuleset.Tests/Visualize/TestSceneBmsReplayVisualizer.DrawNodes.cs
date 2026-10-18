#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsReplayVisualizer
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestDenseDrawBuffersReuseStorageAndPreserveSnapshots(bool gradient)
    {
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        BmsReplayVisualizer.PlotLayer layer = null!;
        var frame = 0UL;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("add batch layer", () => Player.HUDOverlay.Add(layer = new BmsReplayVisualizer.PlotLayer
        {
            Size = new Vector2(240, 360),
            Colour = gradient ? ColourInfo.GradientHorizontal(Colour4.Cyan, Colour4.White) : Colour4.White,
            Alpha = 0.5f,
        }));
        AddUntilStep("layer loaded", () => layer.IsLoaded);
        AddStep("prepare dense geometry", () =>
        {
            Player.GameplayClockContainer.Stop();
            for (var i = 0; i < 2048; i++)
                layer.Add(new BmsReplayVisualizer.PlotRectangle(new RectangleF(i % 20 * 10, i % 30 * 10, 10, 20), Colour4.White, i % 2 == 0));
            layer.Commit();
            layer.UpdateSubTree();
            layer.UpdateSubTreeMasking();
        });
        AddStep("check reusable buffers and snapshots", () =>
        {
            for (var slot = 0; slot < 3; slot++) snapshot(slot);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            {
                layer.Commit();
                for (var slot = 0; slot < 3; slot++) snapshot(slot);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThan(32000));
            var previous = snapshot(0);
            Assert.That(previous.Rectangles, Has.Count.EqualTo(6144));
            var previousRectangles = previous.Rectangles.ToArray();
            var previousColours = Enumerable.Range(0, previous.Rectangles.Count).Select(previous.GetRectangleColour).ToArray();
            Assert.That(previousColours[0].HasSingleColour, Is.EqualTo(!gradient));
            Assert.That(previousColours[0].TopLeft.Linear.A, Is.EqualTo(0.04f).Within(0.001f));
            Assert.That(previousColours[1].TopLeft.Linear.A, Is.EqualTo(0.5f).Within(0.001f));
            layer.Colour = Colour4.Red;
            layer.Position = new Vector2(20, 30);
            var changed = snapshot(1);
            Assert.That(changed.Rectangles[0].Quad, Is.Not.EqualTo(previousRectangles[0].Quad));
            Assert.That(changed.GetRectangleColour(0), Is.Not.EqualTo(previousColours[0]));
            Assert.That(previous.Rectangles, Is.EqualTo(previousRectangles));
            for (var i = 0; i < previousColours.Length; i++)
                Assert.That(previous.GetRectangleColour(i), Is.EqualTo(previousColours[i]));
            layer.Clear();
            Assert.That(snapshot(1).Rectangles, Is.Empty);
            Assert.That(previous.Rectangles, Is.EqualTo(previousRectangles));
            Assert.That(snapshot(2).Rectangles, Is.Empty);
            Assert.That(snapshot(0).Rectangles, Is.Empty);
        });

        BmsReplayVisualizer.PlotLayer.PlotDrawNode snapshot(int slot)
            => (BmsReplayVisualizer.PlotLayer.PlotDrawNode)generate(layer, ++frame, slot, false)!;
    }
}
