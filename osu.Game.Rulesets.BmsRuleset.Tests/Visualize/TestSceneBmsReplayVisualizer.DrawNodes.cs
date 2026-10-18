#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsReplayVisualizer
{
    [Resolved]
    private IRenderer renderer { get; set; } = null!;

    [Test]
    public void TestGeometryPreparationWaitsForSnapshotAndKeepsSnapshotsIndependent()
    {
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        BmsReplayVisualizer.PlotLayer layer = null!;
        var requestedX = 0f;
        var preparations = 0;
        var frame = 0UL;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("add deferred layer", () => Player.HUDOverlay.Add(layer = new BmsReplayVisualizer.PlotLayer(() =>
        {
            preparations++;
            layer.Clear();
            layer.Add(new BmsReplayVisualizer.PlotRectangle(new RectangleF(requestedX, 0, 10, 20), Colour4.White));
            layer.Commit();
        }) { Size = new Vector2(240, 360) }));
        AddUntilStep("layer loaded", () => layer.IsLoaded);
        AddStep("prepare only when a snapshot is requested", () =>
        {
            Player.GameplayClockContainer.Stop();
            var before = preparations;
            for (var i = 0; i < 32; i++)
            {
                requestedX = i;
                layer.Invalidate(Invalidation.DrawNode);
                layer.UpdateSubTree();
            }
            Assert.That(preparations, Is.EqualTo(before));
            layer.UpdateSubTreeMasking();
            var previous = snapshot(0);
            Assert.That(preparations, Is.EqualTo(before + 1));
            var previousRectangle = previous.Rectangles.Single();
            var previousMatrix = previous.Matrix;
            Assert.That((previousRectangle.Quad * previous.Matrix).TopLeft, Is.EqualTo(layer.ToScreenSpace(new Vector2(31, 0))));
            snapshot(0);
            Assert.That(preparations, Is.EqualTo(before + 1), "preparing geometry must not leave the draw node invalid");

            requestedX = 64;
            layer.Invalidate(Invalidation.DrawNode);
            var next = snapshot(1);
            Assert.That(preparations, Is.EqualTo(before + 2));
            Assert.That((next.Rectangles.Single().Quad * next.Matrix).TopLeft, Is.EqualTo(layer.ToScreenSpace(new Vector2(64, 0))));
            Assert.That(previous.Rectangles.Single(), Is.EqualTo(previousRectangle));
            Assert.That(previous.Matrix, Is.EqualTo(previousMatrix));
        });

        BmsReplayVisualizer.PlotLayer.PlotDrawNode snapshot(int slot)
            => (BmsReplayVisualizer.PlotLayer.PlotDrawNode)generate(layer, ++frame, slot, false)!;
    }

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
                layer.Add(new BmsReplayVisualizer.PlotRectangle(new RectangleF(i % 20 * 10, i % 30 * 10, 10, 20),
                    i % 2 == 0 ? Colour4.Cyan : Colour4.White, i % 2 == 0));
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
            var previousMatrix = previous.Matrix;
            var previousColours = Enumerable.Range(0, previous.Rectangles.Count).Select(previous.GetRectangleColour).ToArray();
            Assert.That(previousColours[0].HasSingleColour, Is.EqualTo(!gradient));
            Assert.That(previousColours[0].TopLeft.Linear.A, Is.EqualTo(0.04f).Within(0.001f));
            Assert.That(previousColours[1].TopLeft.Linear.A, Is.EqualTo(0.5f).Within(0.001f));
            var expectedVertices = new List<TexturedVertex2D>();
            for (var i = 0; i < previous.Rectangles.Count; i++)
            {
                var rectangle = previous.Rectangles[i];
                renderer.DrawQuad(renderer.WhitePixel, rectangle.Quad, previousColours[i], vertexAction: expectedVertices.Add);
            }
            var actualVertices = new List<TexturedVertex2D>();
            previous.AddVertices(renderer, actualVertices.Add);
            Assert.That(actualVertices, Has.Count.EqualTo(previous.Rectangles.Count * 4));
            Assert.That(actualVertices, Is.EqualTo(expectedVertices), "batch vertices must match the framework's textured quads");
            layer.Colour = gradient ? ColourInfo.GradientHorizontal(Colour4.Red, Colour4.Yellow) : Colour4.Red;
            layer.Position = new Vector2(20, 30);
            layer.Rotation = 17;
            layer.Scale = new Vector2(0.75f, 1.25f);
            var changed = snapshot(1);
            Assert.That(changed.Rectangles[0].Quad, Is.EqualTo(previousRectangles[0].Quad));
            Assert.That(changed.Matrix, Is.Not.EqualTo(previousMatrix));
            Assert.That((changed.Rectangles[0].Quad * changed.Matrix).TopLeft,
                Is.EqualTo(layer.ToScreenSpace(changed.Rectangles[0].Bounds.TopLeft)));
            Assert.That(changed.GetRectangleColour(0), Is.Not.EqualTo(previousColours[0]));
            Assert.That(previous.Rectangles, Is.EqualTo(previousRectangles));
            Assert.That(previous.Matrix, Is.EqualTo(previousMatrix));
            for (var i = 0; i < previousColours.Length; i++)
                Assert.That(previous.GetRectangleColour(i), Is.EqualTo(previousColours[i]));
            layer.Colour = gradient ? Colour4.White : ColourInfo.GradientHorizontal(Colour4.Cyan, Colour4.White);
            var switched = snapshot(2);
            Assert.That(switched.Rectangles, Has.Count.EqualTo(6144));
            Assert.That(previous.Rectangles, Is.EqualTo(previousRectangles));
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
