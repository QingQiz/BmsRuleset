using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestStageHudBoundsControlStageTransform()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }))
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1000, 600),
        };
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(100, 600);

        var stageHud = new BmsStageHud
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(600, 250),
            Size = new Vector2(200, 300),
        };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);

        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.PositionOffset, Is.EqualTo(new Vector2(200, 100)));
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(2).Within(0.001f));
            Assert.That(playfield.Stage.Scale.Y, Is.EqualTo(1).Within(0.001f));
            Assert.That(playfield.Stage.HudViewportHeight, Is.EqualTo(300).Within(0.001f));
            Assert.That(playfield.Stage.HasHudTransform, Is.True);
            Assert.That(playfield.Stage.X, Is.EqualTo(200).Within(0.001f));
            Assert.That(playfield.Stage.Y, Is.EqualTo(100).Within(0.001f));
            Assert.That(playfield.Stage.Masking, Is.True);
        });
    }

    [Test]
    public void TestDiagonalStageHudResizePreservesAspectRatio()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }))
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1000, 600),
        };
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(100, 600);

        var stageHud = new BmsStageHud
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(200, 1200),
        };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.Scale, Is.EqualTo(new Vector2(2)));
            Assert.That(playfield.Stage.HudViewportHeight, Is.EqualTo(600).Within(0.001f));
            Assert.That(playfield.Stage.Masking, Is.False);
        });
    }

    [Test]
    public void TestStageHudSizeTracksDifferentEditorAndGameplayCanvasSizes()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var hudCanvas = new Container { Size = new Vector2(800, 600) };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }))
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1000, 600),
        };
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(100, 600);

        var stageHud = new BmsStageHud
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Size = new Vector2(0.75f, 0.5f),
        };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(hudCanvas, root);
        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, hudCanvas);
        controller.Register(stageHud);

        Assert.That(playfield.Stage.Scale.X, Is.EqualTo(6).Within(0.001f));

        // Reloading the editor-authored layout into the wider gameplay canvas retains the relative
        // width instead of carrying over the editor's 600-pixel frame width.
        var runtimeStageHud = (BmsStageHud)stageHud.CreateSerialisedInfo().CreateInstance();
        setDrawableParent(runtimeStageHud, root);
        controller.Register(runtimeStageHud);

        Assert.Multiple(() =>
        {
            Assert.That(runtimeStageHud.Width, Is.EqualTo(0.75f));
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(7.5f).Within(0.001f));
        });
    }

    [Test]
    public void TestStageHudMigratesAbsoluteSizeToRelativeSize()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }))
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1000, 600),
        };
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(100, 600);

        var stageHud = new BmsStageHud { Size = new Vector2(750, 300) };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(stageHud.Size.X, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(stageHud.Size.Y, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(7.5f).Within(0.001f));
            Assert.That(playfield.Stage.Scale.Y, Is.EqualTo(1).Within(0.001f));
            Assert.That(playfield.Stage.HudViewportHeight, Is.EqualTo(300).Within(0.001f));
        });
    }

    [Test]
    public void TestVerticalOnlyStageHudResizeCropsViewport()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }))
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1000, 600),
        };
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(100, 600);

        var stageHud = new BmsStageHud
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Position = new Vector2(450, 150),
            Size = new Vector2(100, 300),
        };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.Scale, Is.EqualTo(Vector2.One));
            Assert.That(playfield.Stage.Height, Is.EqualTo(300).Within(0.001f));
            Assert.That(playfield.Stage.HudViewportHeight, Is.EqualTo(300).Within(0.001f));
            Assert.That(playfield.Stage.Masking, Is.True);
        });
    }

    [Test]
    public void TestStageHudIgnoresInvalidBounds()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }))
        {
            RelativeSizeAxes = Axes.None,
            Size = new Vector2(1000, 600),
        };
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = Vector2.Zero;

        var stageHud = new BmsStageHud { Size = new Vector2(200, 300) };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.HasHudTransform, Is.False);
            Assert.That(float.IsFinite(playfield.Stage.Scale.X), Is.True);
            Assert.That(float.IsFinite(playfield.Stage.Scale.Y), Is.True);
        });
    }

    [Test]
    public void TestStageHudContainerRegeneratesMissingHud()
    {
        var controller = createController();
        var container = new TestSerialisableDrawableContainer();

        controller.RegisterContainer(container);

        Assert.That(container.Components.OfType<BmsStageHud>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void TestStageHudContainerRemovesDuplicateHuds()
    {
        var controller = createController();
        var container = new TestSerialisableDrawableContainer();
        var first = new BmsStageHud();
        var second = new BmsStageHud();

        container.Add(first);
        container.Add(second);
        controller.RegisterContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(container.Components.OfType<BmsStageHud>().Count(), Is.EqualTo(1));
            Assert.That(container.Components.OfType<BmsStageHud>().Single(), Is.SameAs(first));
        });
    }

    [Test]
    public void TestLiveDuplicateStageHudIsNotRemovedSynchronously()
    {
        var controller = createController();
        var container = new TestSerialisableDrawableContainer();

        controller.RegisterContainer(container);
        var duplicate = new BmsStageHud();
        container.Add(duplicate);

        Assert.That(container.Components, Does.Contain(duplicate));
    }

    [Test]
    public void TestRegisteringLiveDuplicateDoesNotReplaceFirstStageHud()
    {
        var controller = createController();
        var container = new TestSerialisableDrawableContainer();

        controller.RegisterContainer(container);
        var original = container.Components.OfType<BmsStageHud>().Single();
        var duplicate = new BmsStageHud();
        container.Add(duplicate);

        Assert.That(container.Components, Does.Contain(duplicate));

        controller.Register(duplicate);
        controller.RegisterContainer(container);

        Assert.Multiple(() =>
        {
            Assert.That(container.Components.OfType<BmsStageHud>().Count(), Is.EqualTo(1));
            Assert.That(container.Components.OfType<BmsStageHud>().Single(), Is.SameAs(original));
        });
    }

    [Test]
    public void TestStageHudContainerRegeneratesRemovedHud()
    {
        var controller = createController();
        var container = new TestSerialisableDrawableContainer();
        var hud = new BmsStageHud();

        container.Add(hud);
        controller.RegisterContainer(container);
        container.Remove(hud, true);

        Assert.Multiple(() =>
        {
            Assert.That(container.Components.OfType<BmsStageHud>().Count(), Is.EqualTo(1));
            Assert.That(container.Components.OfType<BmsStageHud>().Single(), Is.Not.SameAs(hud));
        });
    }

    private static BmsStageHudController createController() => new(new BmsPlayfield(attachBeatmap(new BmsBeatmap
    {
        TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
        LayoutVariant = BmsLayoutVariant.Bme7K,
    })));

    private static void setDrawableParent(Drawable child, CompositeDrawable parent)
    {
        var property = typeof(Drawable).GetProperty(nameof(Drawable.Parent), BindingFlags.Instance | BindingFlags.Public);
        Assert.That(property, Is.Not.Null);

        property!.SetValue(child, parent);
    }

    private static void setAutoSizeAxes(CompositeDrawable drawable, Axes axes)
    {
        var property = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.AutoSizeAxes), BindingFlags.Instance | BindingFlags.Public);
        Assert.That(property, Is.Not.Null);

        property!.SetValue(drawable, axes);
    }

    private sealed partial class TestSerialisableDrawableContainer : CompositeDrawable, ISerialisableDrawableContainer
    {
        private readonly BindableList<ISerialisableDrawable> components = new();

        public IBindableList<ISerialisableDrawable> Components => components;

        public void Reload()
        {
        }

        public void Add(ISerialisableDrawable drawable) => components.Add(drawable);

        public void Remove(ISerialisableDrawable component, bool disposeImmediately) => components.Remove(component);
    }
}
