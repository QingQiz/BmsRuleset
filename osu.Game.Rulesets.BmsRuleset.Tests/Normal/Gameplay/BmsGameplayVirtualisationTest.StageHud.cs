using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Settings.Components;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestStageHudControllerDisposalAfterStageDisposal()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        var controller = new BmsStageHudController(playfield);

        playfield.Stage.SetHudTransform(Vector2.Zero, Vector2.One, 500);
        playfield.Stage.Dispose();

        Assert.DoesNotThrow(controller.Dispose);
        Assert.That(playfield.Stage.HasHudTransform, Is.True);
    }

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
            Size = new Vector2(200, 0.5f),
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
            Assert.That(playfield.Stage.Masking, Is.False);
            Assert.That(playfield.Stage.ColumnArea.Masking, Is.True);
            Assert.That(playfield.Stage.Columns.All(column => !((BmsColumn)column).Masking), Is.True);
            Assert.That(playfield.Stage.Columns.All(column => !column.HitExplosionArea.Masking), Is.True);
            Assert.That(playfield.Stage.MeasureLineArea.Masking, Is.True);
        });
    }

    [Test]
    public void TestStageHudCanScaleWidthRelativeToOriginalStage()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(150, 600);

        var stageHud = new BmsStageHud();
        stageHud.ProportionalWidthReference.Value = 100;
        var controller = new BmsStageHudController(playfield);
        controller.Register(stageHud);
        controller.ApplyStageTransform(new Vector2(200, 600), Vector2.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(2).Within(0.001f));
            Assert.That(playfield.Stage.DrawWidth * playfield.Stage.Scale.X, Is.EqualTo(300).Within(0.001f));
        });

        stageHud.ProportionalWidthReference.Value = 0;
        controller.ApplyStageTransform(new Vector2(200, 600), Vector2.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(200f / 150).Within(0.001f));
            Assert.That(playfield.Stage.DrawWidth * playfield.Stage.Scale.X, Is.EqualTo(200).Within(0.001f));
        });
    }

    [Test]
    public void TestProportionalStageHudBoundsFollowCurrentStageWidth()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(150, 600);

        var stageHud = new BmsStageHud { Width = 200 };
        stageHud.ProportionalWidthReference.Value = 100;
        var controller = new BmsStageHudController(playfield);
        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(stageHud.Width, Is.EqualTo(300).Within(0.001f));
            Assert.That(stageHud.ProportionalWidthReference.Value, Is.EqualTo(150));
        });
    }

    [Test]
    public void TestWidthScalingCheckboxCapturesCurrentStageWidth()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.Stage.RelativeSizeAxes = Axes.None;
        setAutoSizeAxes(playfield.Stage, Axes.None);
        playfield.Stage.Size = new Vector2(150, 600);

        var stageHud = new BmsStageHud();
        var controller = new BmsStageHudController(playfield);
        controller.Register(stageHud);
        var setting = stageHud.CreateSettingsControls().OfType<StageWidthScalingCheckbox>().Single();
        var checkbox = setting.ChildrenOfType<OsuCheckbox>().Single();

        checkbox.Current.Value = true;
        Assert.That(stageHud.ProportionalWidthReference.Value, Is.EqualTo(150));

        checkbox.Current.Value = false;
        Assert.That(stageHud.ProportionalWidthReference.Value, Is.Zero);
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
            Size = new Vector2(100, 1),
            Scale = new Vector2(2),
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
    public void TestHorizontalResizeAfterDiagonalResizePreservesNoteHeightScale()
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
            Size = new Vector2(100, 1),
            Scale = new Vector2(2),
        };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        Assert.That(playfield.Stage.Scale, Is.EqualTo(new Vector2(2)));

        stageHud.Width = 150;
        controller.ApplyStageTransform(new Vector2(300, 1200), new Vector2(500, 600));

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(3).Within(0.001f));
            Assert.That(playfield.Stage.Scale.Y, Is.EqualTo(2).Within(0.001f));
            Assert.That(playfield.Stage.HudViewportHeight, Is.EqualTo(600).Within(0.001f));
            Assert.That(playfield.Stage.Masking, Is.False);
        });
    }

    [Test]
    public void TestStageHudNoteHeightScalePreservesBounds()
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
            Size = new Vector2(100, 1),
        };
        stageHud.JudgementLineOffset.Value = 20;
        stageHud.LightPositionOffset.Value = 30;
        stageHud.NoteHeightScale.Value = 1.5f;
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.Scale, Is.EqualTo(Vector2.One));
            Assert.That(playfield.Stage.HudViewportHeight, Is.EqualTo(600).Within(0.001f));
            Assert.That(playfield.Stage.HitTargetPositionOffset, Is.EqualTo(20));
            Assert.That(playfield.Stage.LightPositionOffset, Is.EqualTo(30));
            Assert.That(playfield.Stage.NoteHeightScale, Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void TestNoteHeightScaleOnlyScalesNoteVisual()
    {
        var drawable = new TestDrawableBmsHitObject();

        drawable.ApplyNoteHeightScale(1.5f);

        Assert.Multiple(() =>
        {
            Assert.That(drawable.Scale, Is.EqualTo(Vector2.One));
            Assert.That(drawable.TestNoteContainer.Scale, Is.EqualTo(new Vector2(1, 1.5f)));
        });
    }

    [Test]
    public void TestRepeatedMixedResizesKeepStageInsideHudBounds()
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

        var stageHud = new BmsStageHud { Size = new Vector2(100, 1) };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, root);
        controller.Register(stageHud);

        apply(new Vector2(170, 1020), 1.7f);
        apply(new Vector2(300, 1020), 1.7f);
        apply(new Vector2(500, 1380), 2.3f);
        apply(new Vector2(500, 920), 2.3f);
        apply(new Vector2(700, 920), 2.3f);

        void apply(Vector2 hudSize, float contentScale)
        {
            stageHud.Scale = new Vector2(contentScale);
            controller.ApplyStageTransform(hudSize, Vector2.Zero);

            Assert.Multiple(() =>
            {
                Assert.That(playfield.Stage.DrawWidth * playfield.Stage.Scale.X, Is.EqualTo(hudSize.X).Within(0.001f));
                Assert.That(playfield.Stage.HudViewportHeight * playfield.Stage.Scale.Y, Is.EqualTo(hudSize.Y).Within(0.001f));
            });
        }
    }

    [Test]
    public void TestStageHudWidthIsStableAcrossEditorAndGameplayCanvasSizes()
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
            Size = new Vector2(600, 0.5f),
        };
        var controller = new BmsStageHudController(playfield);

        setDrawableParent(hudCanvas, root);
        setDrawableParent(playfield, root);
        setDrawableParent(stageHud, hudCanvas);
        controller.Register(stageHud);

        Assert.That(playfield.Stage.Scale.X, Is.EqualTo(6).Within(0.001f));

        // Reloading the editor-authored layout into a wider gameplay canvas must not rescale its width.
        var runtimeStageHud = (BmsStageHud)stageHud.CreateSerialisedInfo().CreateInstance();
        setDrawableParent(runtimeStageHud, root);
        controller.Register(runtimeStageHud);

        Assert.Multiple(() =>
        {
            Assert.That(runtimeStageHud.Width, Is.EqualTo(600));
            Assert.That(playfield.Stage.Scale.X, Is.EqualTo(6).Within(0.001f));
        });

        var legacyRelativeStageHud = new BmsStageHud
        {
            Width = 0.3716294f,
            Height = 0.5f,
        };
        setDrawableParent(legacyRelativeStageHud, root);
        controller.Register(legacyRelativeStageHud);

        Assert.That(legacyRelativeStageHud.Width, Is.EqualTo(600).Within(0.001f));
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
            Size = new Vector2(100, 0.5f),
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
            Assert.That(playfield.Stage.Masking, Is.False);
            Assert.That(playfield.Stage.ColumnArea.Masking, Is.True);
            Assert.That(playfield.Stage.Columns.All(column => !((BmsColumn)column).Masking), Is.True);
            Assert.That(playfield.Stage.Columns.All(column => !column.HitExplosionArea.Masking), Is.True);
            Assert.That(playfield.Stage.MeasureLineArea.Masking, Is.True);
            Assert.That(stageHud.JudgementLineOffset.MinValue, Is.EqualTo(-80));
            Assert.That(stageHud.JudgementLineOffset.MaxValue, Is.EqualTo(220));
            Assert.That(stageHud.LightPositionOffset.MinValue, Is.Zero);
            Assert.That(stageHud.LightPositionOffset.MaxValue, Is.EqualTo(300));
        });
    }

    [Test]
    public void TestLegacyStageSideImagesKeepSkinWidthWhenStageShrinks()
    {
        const float stage_scale = 0.25f;
        var imageScale = LegacyBmsStageBackground.HorizontalScaleFor(stage_scale);

        Assert.That(stage_scale * imageScale, Is.EqualTo(1).Within(0.001f));
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

        var stageHud = new BmsStageHud { Size = new Vector2(200, 0.5f) };
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
        runScheduledTasks(controller);

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
        runScheduledTasks(controller);

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
        runScheduledTasks(controller);
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
        runScheduledTasks(controller);
        var original = container.Components.OfType<BmsStageHud>().Single();
        var duplicate = new BmsStageHud();
        container.Add(duplicate);

        Assert.That(container.Components, Does.Contain(duplicate));

        controller.Register(duplicate);
        controller.RegisterContainer(container);
        runScheduledTasks(controller);

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
        var hud = new BmsStageHud
        {
            Position = new Vector2(120, -80),
            Size = new Vector2(0.75f, 0.5f),
            Scale = new Vector2(1.5f),
        };

        container.Add(hud);
        controller.RegisterContainer(container);
        runScheduledTasks(controller);
        container.Remove(hud, true);
        runScheduledTasks(controller);

        var replacement = container.Components.OfType<BmsStageHud>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(container.Components.OfType<BmsStageHud>().Count(), Is.EqualTo(1));
            Assert.That(replacement, Is.Not.SameAs(hud));
            Assert.That(replacement.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(replacement.Size, Is.EqualTo(Vector2.Zero));
            Assert.That(replacement.Scale, Is.EqualTo(Vector2.One));
        });
    }

    [TestCase(-10, -5, -10, -5)]
    [TestCase(90, 95, 90, 90)]
    [TestCase(-30, -25, -10, -10)]
    [TestCase(110, 120, 90, 90)]
    [TestCase(30, 40, 30, 40)]
    public void TestHudEditorBoundsClamp(float x, float y, float expectedX, float expectedY)
    {
        var bounds = new TestSerialisableDrawableContainer { Size = new Vector2(100) };
        var hud = new TestHudComponent
        {
            Position = new Vector2(x, y),
            Size = new Vector2(20),
        };
        setDrawableParent(hud, bounds);

        hud.ClampToEditorBounds();

        Assert.That(hud.Position, Is.EqualTo(new Vector2(expectedX, expectedY)));
    }

    [Test]
    public void TestOversizedHudEditorBoundsClampKeepsVisibleComponentPosition()
    {
        var bounds = new TestSerialisableDrawableContainer { Size = new Vector2(100) };
        var hud = new TestHudComponent
        {
            Position = new Vector2(10),
            Size = new Vector2(120),
        };
        setDrawableParent(hud, bounds);

        hud.ClampToEditorBounds();

        Assert.That(hud.Position, Is.EqualTo(new Vector2(10)));
    }

    [Test]
    public void TestHudEditorBoundsClampPreservesRelativePositionAxes()
    {
        var bounds = new TestSerialisableDrawableContainer { Size = new Vector2(100) };
        var hud = new TestHudComponent
        {
            RelativePositionAxes = Axes.Both,
            Position = new Vector2(0.95f),
            Size = new Vector2(20),
        };
        setDrawableParent(hud, bounds);

        hud.ClampToEditorBounds();

        Assert.Multiple(() =>
        {
            Assert.That(hud.RelativePositionAxes, Is.EqualTo(Axes.Both));
            Assert.That(hud.Position, Is.EqualTo(new Vector2(0.9f)));
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

    private static void runScheduledTasks(Drawable drawable)
    {
        var property = typeof(Drawable).GetProperty("Scheduler", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null);

        ((Scheduler)property!.GetValue(drawable)!).Update();
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

    private sealed partial class TestHudComponent : BmsHudComponent
    {
        public TestHudComponent()
        {
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopLeft;
        }
    }

    private sealed partial class TestDrawableBmsHitObject : DrawableBmsHitObject
    {
        protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Note;

        public Container TestNoteContainer => NoteContainer;

        public TestDrawableBmsHitObject()
        {
            NoteContainer = new Container();
        }

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
        }
    }
}
