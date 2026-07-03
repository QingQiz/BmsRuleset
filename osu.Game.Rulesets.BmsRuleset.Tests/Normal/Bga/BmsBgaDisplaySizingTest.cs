using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga.Mpeg;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsBgaDisplaySizingTest
{
    [Test]
    public void TestFullCanvasImageFitsAspectInsideBgaComponent()
    {
        using var texture = new DummyRenderer().CreateTexture(320, 240);
        var sprite = createFullCanvasSprite(texture);

        Assert.Multiple(() =>
        {
            Assert.That(sprite.RelativeSizeAxes, Is.EqualTo(Axes.Both));
            Assert.That(sprite.Size, Is.EqualTo(new osuTK.Vector2(1)));
            Assert.That(sprite.FillMode, Is.EqualTo(FillMode.Fit));
        });
    }

    [Test]
    public void TestMpegVideoChildSpriteFitsAspect()
    {
        // The MPEG drawable is a CompositeDrawable whose child Sprite renders the texture.
        // Aspect-fit must be set on that child, not the parent — flipping the parent to Fit
        // would double-letterbox (the composite would letterbox, then the child again).
        var drawable = new BmsMpegVideoDrawable(new MemoryStream(), new StopwatchClock(), 0);

        var spriteField = typeof(BmsMpegVideoDrawable).GetField("sprite", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(spriteField, Is.Not.Null);

        var sprite = (Sprite)spriteField!.GetValue(drawable)!;
        Assert.That(sprite.FillMode, Is.EqualTo(FillMode.Fit));
    }

    [Test]
    public void TestAutoSizeToParentFillsParent()
    {
        var display = new BmsBgaDisplay { AutoSizeToParent = true };
        applyAutoSizeToParent(display);

        Assert.Multiple(() =>
        {
            Assert.That(display.RelativeSizeAxes, Is.EqualTo(Axes.Both));
            Assert.That(display.Size, Is.EqualTo(new osuTK.Vector2(1)));
        });
    }

    [Test]
    public void TestNonAutoSizeKeepsExplicitSize()
    {
        // AutoSizeToParent defaults to false; the constructor sets an explicit 640×480 size that
        // skin-edited layouts override. applyAutoSizeToParent must leave it untouched.
        var display = new BmsBgaDisplay();
        applyAutoSizeToParent(display);

        Assert.Multiple(() =>
        {
            Assert.That(display.RelativeSizeAxes, Is.EqualTo(Axes.None));
            Assert.That(display.Size, Is.EqualTo(new osuTK.Vector2(640, 480)));
        });
    }

    [Test]
    public void TestRehostedCloneSyncDoesNotChangeDepthAfterParenting()
    {
        var source = new BmsBgaDisplay
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.BottomLeft,
            Position = new osuTK.Vector2(12, 34),
            Scale = new osuTK.Vector2(2),
            Rotation = 45,
            Size = new osuTK.Vector2(320, 240),
            Depth = 12,
        };

        var clone = new BmsBgaDisplay { Depth = float.MaxValue };
        using var parent = new Container { Child = clone };

        var method = typeof(BmsBgaDisplay).GetMethod("syncRehostedDisplayState", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        Assert.DoesNotThrow(() => method!.Invoke(null, [source, clone]));
        Assert.That(clone.Depth, Is.EqualTo(float.MaxValue));
    }

    [Test]
    public void TestSourceDisposalDoesNotSynchronouslyRemoveRehostedHost()
    {
        using var source = new BmsBgaDisplay();
        using var parent = new Container();
        var host = createRehostedDisplayHost();

        parent.Add(host);
        setRehostedDisplayHost(source, host);

        source.Dispose();

        Assert.That(parent.Children, Does.Contain(host));
    }

    [Test]
    public void TestBgaDimDefaultsToSeventyPercent()
    {
        using var config = new BmsRulesetConfigManager(null, new BmsRuleset().RulesetInfo);

        Assert.That(config.Get<double>(BmsRulesetSetting.BgaDim), Is.EqualTo(0.7));
    }

    [TestCase(0, 1, 1)]
    [TestCase(0.7, 0.3, 1)]
    [TestCase(1, 0, 0)]
    public void TestBgaDimControlsDisplayBrightness(double dim, double expectedBrightness, double expectedAlpha)
    {
        var display = new BmsBgaDisplay();
        var method = typeof(BmsBgaDisplay).GetMethod("applyBgaDim", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);

        method!.Invoke(display, [dim]);
        var layers = getLayers(display);

        Assert.Multiple(() =>
        {
            Assert.That(layers.Alpha, Is.EqualTo(expectedAlpha).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.R, Is.EqualTo(expectedBrightness).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.G, Is.EqualTo(expectedBrightness).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.B, Is.EqualTo(expectedBrightness).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.A, Is.EqualTo(1).Within(0.0001f));
        });
    }

    [Test]
    public void TestBgaDimChangeIsForwardedToRehostedDisplay()
    {
        var source = new BmsBgaDisplay();
        var clone = new BmsBgaDisplay();

        setRehostedDisplay(source, clone);
        applyBgaDim(source, 0.2);

        var sourceLayers = getLayers(source);
        var cloneLayers = getLayers(clone);

        Assert.Multiple(() =>
        {
            Assert.That(sourceLayers.Alpha, Is.Zero);
            Assert.That(cloneLayers.Alpha, Is.EqualTo(1).Within(0.0001f));
            Assert.That(cloneLayers.Colour.TopLeft.SRGB.R, Is.EqualTo(0.8).Within(0.0001f));
            Assert.That(cloneLayers.Colour.TopLeft.SRGB.G, Is.EqualTo(0.8).Within(0.0001f));
            Assert.That(cloneLayers.Colour.TopLeft.SRGB.B, Is.EqualTo(0.8).Within(0.0001f));
        });
    }

    [Test]
    public void TestBgaDimBindableChangeUpdatesDisplayBrightness()
    {
        using var drawableRuleset = new BmsDrawableRuleset(new BmsRuleset(), new BmsBeatmap());
        var display = new BmsBgaDisplay();

        setDrawableRuleset(display, drawableRuleset);
        bindBgaDim(display);

        drawableRuleset.BgaDim.Value = 0.4;
        var layers = getLayers(display);

        Assert.Multiple(() =>
        {
            Assert.That(layers.Alpha, Is.EqualTo(1).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.R, Is.EqualTo(0.6).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.G, Is.EqualTo(0.6).Within(0.0001f));
            Assert.That(layers.Colour.TopLeft.SRGB.B, Is.EqualTo(0.6).Within(0.0001f));
        });
    }

    [Test]
    public void TestVideoBgaResourceLookupPrefersMp4OverChartVideoExtension()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(BmsBgaDisplaySizingTest)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "movie.mpg"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "movie.mp4"), [2]);
            File.WriteAllBytes(Path.Combine(directory, "movie.png"), [4]);

            using var store = createBgaResourceStore(directory);
            using var stream = invokeGetStream(store, "movie.mpg");

            Assert.That(stream.ReadByte(), Is.EqualTo(2));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void TestImageBgaResourceLookupDoesNotUseVideoFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(BmsBgaDisplaySizingTest)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "movie.bmp"), [1]);
            File.WriteAllBytes(Path.Combine(directory, "movie.mp4"), [2]);
            File.WriteAllBytes(Path.Combine(directory, "movie.png"), [4]);

            using var store = createBgaResourceStore(directory);
            using var stream = invokeGetStream(store, "movie.bmp");

            Assert.That(stream.ReadByte(), Is.EqualTo(4));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void TestBgaResourceLookupFallsBackToOriginalUnknownExtension()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(BmsBgaDisplaySizingTest)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllBytes(Path.Combine(directory, "movie.dat"), [3]);
            File.WriteAllBytes(Path.Combine(directory, "movie.mp4"), [2]);

            using var store = createBgaResourceStore(directory);
            using var stream = invokeGetStream(store, "movie.dat");

            Assert.That(stream.ReadByte(), Is.EqualTo(3));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void TestPreloadedBgaResourceServesResolvedFallbackFromCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{nameof(BmsBgaDisplaySizingTest)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var preferredPath = Path.Combine(directory, "movie.mp4");
            File.WriteAllBytes(Path.Combine(directory, "movie.mpg"), [1]);
            File.WriteAllBytes(preferredPath, [2]);

            using var store = createBgaResourceStore(directory);
            invokePreload(store, ["movie.mpg"]);
            File.Delete(preferredPath);

            using var stream = invokeGetStream(store, "movie.mpg");

            Assert.That(stream.ReadByte(), Is.EqualTo(2));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void applyBgaDim(BmsBgaDisplay display, double dim)
    {
        var method = typeof(BmsBgaDisplay).GetMethod("applyBgaDim", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);

        method!.Invoke(display, [dim]);
    }

    private static void applyAutoSizeToParent(BmsBgaDisplay display)
    {
        var method = typeof(BmsBgaDisplay).GetMethod("applyAutoSizeToParent", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);

        method!.Invoke(display, []);
    }

    private static void setRehostedDisplay(BmsBgaDisplay source, BmsBgaDisplay clone)
    {
        var field = typeof(BmsBgaDisplay).GetField("rehostedDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);

        field!.SetValue(source, clone);
    }

    private static Container createRehostedDisplayHost()
    {
        var type = typeof(BmsBgaDisplay).GetNestedType("RehostedDisplayHost", BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);

        return (Container)Activator.CreateInstance(type!)!;
    }

    private static void setRehostedDisplayHost(BmsBgaDisplay source, Container host)
    {
        var field = typeof(BmsBgaDisplay).GetField("rehostedDisplayHost", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);

        field!.SetValue(source, host);
    }

    private static void bindBgaDim(BmsBgaDisplay display)
    {
        var method = typeof(BmsBgaDisplay).GetMethod("bindBgaDim", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);

        method!.Invoke(display, []);
    }

    private static void setDrawableRuleset(BmsBgaDisplay display, BmsDrawableRuleset drawableRuleset)
    {
        var property = typeof(BmsBgaDisplay).GetProperty("drawableRuleset", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(property, Is.Not.Null);

        property!.SetValue(display, drawableRuleset);
    }

    private static Container getLayers(BmsBgaDisplay display)
    {
        var field = typeof(BmsBgaDisplay).GetField("layers", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);

        return (Container)field!.GetValue(display)!;
    }

    private static IDisposable createBgaResourceStore(string directory)
    {
        var type = typeof(BmsBgaDisplay).GetNestedType("BmsBgaResourceStore", BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);

        return (IDisposable)Activator.CreateInstance(type!, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, [directory, null], null)!;
    }

    private static Stream invokeGetStream(IDisposable store, string name)
    {
        var method = store.GetType().GetMethod("GetStream", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        var stream = method!.Invoke(store, [name]);
        Assert.That(stream, Is.Not.Null);

        return (Stream)stream!;
    }

    private static void invokePreload(IDisposable store, string[] names)
    {
        var method = store.GetType().GetMethod("Preload", BindingFlags.Instance | BindingFlags.Public);
        Assert.That(method, Is.Not.Null);

        method!.Invoke(store, [names]);
    }

    private static Sprite createFullCanvasSprite(Texture texture)
    {
        var method = typeof(BmsBgaDisplay).GetMethod("createFullCanvasSprite", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        return (Sprite)method!.Invoke(null, [texture])!;
    }
}
