using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.IO.Serialization;
using osu.Framework.Threading;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestStageHudControllerDisposalAfterStageDisposal()
    {
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
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
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
    public void TestDiagonalStageHudResizePreservesAspectRatio()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
            Assert.That(drawable.TestVisual.Scale, Is.EqualTo(new Vector2(1, 1.5f)));
        });
    }

    [Test]
    public void TestRepeatedMixedResizesKeepStageInsideHudBounds()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
    public void TestStageHudLayoutsAreIsolatedAcrossSaveAndRestore()
    {
        // A skin from before per-layout editing has only the ordinary drawable settings.
        var legacy = new BmsStageHud { Size = new Vector2(400, 0.75f) }.CreateSerialisedInfo();
        legacy.Settings.Remove("layouts");
        legacy.Settings["judgement_line_offset"] = 12f;
        legacy.Settings["proportional_width_reference"] = 320f;

        var hud = load(legacy, BmsLayoutVariant.Bme7K);
        hud.Anchor = Anchor.BottomRight;
        hud.Origin = Anchor.BottomRight;
        hud.UsesFixedAnchor = true;
        hud.Position = new Vector2(-40, -10);
        hud.Rotation = 2;
        hud.Scale = new Vector2(1.25f);
        hud.Size = new Vector2(480, 0.7f);
        hud.JudgementLineOffset.Value = 24;
        hud.LightPositionOffset.Value = 30;
        hud.NoteHeightScale.Value = 1.5f;
        var sevenKey = hud.CreateSerialisedInfo();
        Assert.That(sevenKey.Settings, Does.Not.ContainKey("proportional_width_reference"));

        var fiveKey = load(sevenKey, BmsLayoutVariant.Bms5K);
        Assert.Multiple(() =>
        {
            Assert.That(fiveKey.Size, Is.EqualTo(new Vector2(400, 0.75f)));
            Assert.That(fiveKey.JudgementLineOffset.Value, Is.EqualTo(12));
        });

        // Saving a preview without editing Stage must not freeze the inherited settings.
        var secondPlayer = load(sevenKey, BmsLayoutVariant.Bme7K2P);
        Assert.Multiple(() =>
        {
            Assert.That(secondPlayer.Size, Is.EqualTo(hud.Size));
            Assert.That(secondPlayer.JudgementLineOffset.Value, Is.EqualTo(24));
        });
        var leftAfterPreviewSave = load(secondPlayer.CreateSerialisedInfo(), BmsLayoutVariant.Bme7K);
        leftAfterPreviewSave.Width = 510;
        var rightAfterLeftEdit = load(leftAfterPreviewSave.CreateSerialisedInfo(), BmsLayoutVariant.Bme7K2P);
        Assert.That(rightAfterLeftEdit.Width, Is.EqualTo(510));

        secondPlayer.Width = 580;
        secondPlayer.NoteHeightScale.Value = 0.75f;
        var afterSecondPlayerEdit = secondPlayer.CreateSerialisedInfo();
        var restored = load(afterSecondPlayerEdit, BmsLayoutVariant.Bme7K);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Anchor, Is.EqualTo(hud.Anchor));
            Assert.That(restored.Origin, Is.EqualTo(hud.Origin));
            Assert.That(restored.UsesFixedAnchor, Is.True);
            Assert.That(restored.Position, Is.EqualTo(hud.Position));
            Assert.That(restored.Rotation, Is.EqualTo(hud.Rotation));
            Assert.That(restored.Scale, Is.EqualTo(hud.Scale));
            Assert.That(restored.Size, Is.EqualTo(hud.Size));
            Assert.That(restored.JudgementLineOffset.Value, Is.EqualTo(24));
            Assert.That(restored.LightPositionOffset.Value, Is.EqualTo(30));
            Assert.That(restored.NoteHeightScale.Value, Is.EqualTo(1.5f));
        });

        var rightAfterEdit = load(afterSecondPlayerEdit, BmsLayoutVariant.Bme7K2P);
        Assert.Multiple(() =>
        {
            Assert.That(rightAfterEdit.Width, Is.EqualTo(580));
            Assert.That(rightAfterEdit.NoteHeightScale.Value, Is.EqualTo(0.75f));
        });

        var reset = rightAfterEdit.CreateResetForLayout(BmsLayoutVariant.Bme7K2P);
        reset.UseLayout(BmsLayoutVariant.Bme7K2P);
        Assert.That(reset.Width, Is.EqualTo(480));

        // The editor's undo handler reapplies snapshots to existing drawables.
        secondPlayer.ApplySerialisedInfo(roundTrip(sevenKey));
        Assert.That(secondPlayer.Width, Is.EqualTo(480));
        secondPlayer.ApplySerialisedInfo(roundTrip(afterSecondPlayerEdit));
        Assert.That(secondPlayer.Width, Is.EqualTo(580));

        static BmsStageHud load(SerialisedDrawableInfo info, BmsLayoutVariant layout)
        {
            var result = (BmsStageHud)roundTrip(info).CreateInstance();
            result.UseLayout(layout);
            return result;
        }

        static SerialisedDrawableInfo roundTrip(SerialisedDrawableInfo info)
        {
            var converters = new Vector2Converter();
            return JsonConvert.DeserializeObject<SerialisedDrawableInfo>(JsonConvert.SerializeObject(info, converters), converters)!;
        }
    }

    [Test]
    public void TestStageHudWidthIsStableAcrossEditorAndGameplayCanvasSizes()
    {
        var root = new Container { Size = new Vector2(1000, 600) };
        var hudCanvas = new Container { Size = new Vector2(800, 600) };
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
        var playfield = createPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.RelativeSizeAxes = Axes.None;
        playfield.Size = new Vector2(1000, 600);
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
    public void TestStageHudRestoresSharedDividerWidthAsIndividualWidths()
    {
        var info = new BmsStageHud().CreateSerialisedInfo();
        info.Settings["stage_styles"] = new JObject { ["7"] = new JObject { ["SeparatorWidth"] = 0 } };

        var hud = (BmsStageHud)info.CreateInstance();
        var widths = hud.GetStageStyle(BmsLayoutVariant.Bme7K).SeparatorWidths;
        Assert.Multiple(() =>
        {
            Assert.That(widths.Keys, Is.EquivalentTo(Enumerable.Range(0, BmsLayout.BME7_KEY_COLUMNS - 1)));
            Assert.That(widths.Values, Is.All.Zero);
        });
    }

    [Test]
    public void TestStageHudContainerRegeneratesRemovedHud()
    {
        var controller = createController();
        var container = new TestSerialisableDrawableContainer();
        var otherLayout = new BmsStageHud();
        otherLayout.UseLayout(BmsLayoutVariant.Bme7K2P);
        otherLayout.Width = 500;
        otherLayout.StageStyles.Value = new Dictionary<int, BmsStageStyle>
        {
            [(int)BmsLayoutVariant.Bme7K] = new() { KeepTotalWidth = false },
            [(int)BmsLayoutVariant.Bme7K2P] = new() { Widths = { [0] = 76 } },
        };

        var hud = (BmsStageHud)otherLayout.CreateSerialisedInfo().CreateInstance();
        container.Add(hud);
        controller.RegisterContainer(container);
        runScheduledTasks(controller);
        hud.Position = new Vector2(120, -80);
        hud.Size = new Vector2(0.75f, 0.5f);
        hud.Scale = new Vector2(1.5f);

        container.Remove(hud, true);
        runScheduledTasks(controller);

        var replacement = container.Components.OfType<BmsStageHud>().Single();
        var restoredOtherLayout = (BmsStageHud)replacement.CreateSerialisedInfo().CreateInstance();
        restoredOtherLayout.UseLayout(BmsLayoutVariant.Bme7K2P);

        Assert.Multiple(() =>
        {
            Assert.That(container.Components.OfType<BmsStageHud>().Count(), Is.EqualTo(1));
            Assert.That(replacement, Is.Not.SameAs(hud));
            Assert.That(replacement.Position, Is.EqualTo(Vector2.Zero));
            Assert.That(replacement.Size, Is.EqualTo(Vector2.Zero));
            Assert.That(replacement.Scale, Is.EqualTo(Vector2.One));
            Assert.That(restoredOtherLayout.Width, Is.EqualTo(500));
            Assert.That(replacement.GetStageStyle(BmsLayoutVariant.Bme7K).KeepTotalWidth, Is.True);
            Assert.That(restoredOtherLayout.GetStageStyle(BmsLayoutVariant.Bme7K2P).Widths[0], Is.EqualTo(76));
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

    private static BmsStageHudController createController() => new(createPlayfield(attachBeatmap(new BmsBeatmap
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
        public Container TestVisual { get; } = new();

        public TestDrawableBmsHitObject()
        {
            Visuals.Add(TestVisual);
        }

        protected override void ApplyVisualHeightScale(float scale) => TestVisual.Scale = new Vector2(1, scale);

        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
        }
    }
}
