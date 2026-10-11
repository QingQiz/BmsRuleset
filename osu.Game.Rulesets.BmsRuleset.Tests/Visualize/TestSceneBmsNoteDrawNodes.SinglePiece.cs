#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsNoteDrawNodes
{
    [TestCase(BmsTestSkins.SkinKind.Argon, false)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, false)]
    [TestCase(BmsTestSkins.SkinKind.Argon, true)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, true)]
    public void SinglePieceHierarchyMatchesFormerComposition(BmsTestSkins.SkinKind selectedSkin, bool nonDefaultAppearance)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var layerField = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
        var refresh = typeof(DrawableBmsHitObject).GetMethod("refreshSinglePieceVisual", flags)!;
        var forceBatch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
        var padding = typeof(CompositeDrawable).GetProperty("Padding", flags | BindingFlags.Public)!;
        var childSize = typeof(CompositeDrawable).GetProperty("RelativeChildSize", flags | BindingFlags.Public)!;
        var disposed = typeof(Drawable).GetProperty("IsDisposed", flags)!.GetMethod!
            .CreateDelegate<Func<Drawable, bool>>();
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var frame = 0UL;
        DrawableBmsHitObject[] notes = [];
        BmsNoteVisual[] controllers = [];
        BmsResolvedNoteSprite[] previousSprites = [];
        Drawable[] finalSprites = [];

        AddStep("load single-piece notes", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("stop before notes", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddUntilStep("static skin components loaded", () =>
        {
            notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
                             .Where(note => note.HitObject is BmsNote or BmsLandmine).ToArray();
            if (notes.Length != 2) return false;
            controllers = notes.Select(note => (BmsNoteVisual)layerField.GetValue(note)!).ToArray();
            return controllers.All(visual => visual.Drawable is BmsResolvedNoteSprite { IsLoaded: true });
        });
        AddStep("compare moving default and fallback geometries", () =>
        {
            for (var repeat = 0; repeat < 32; repeat++)
            {
                Playfield.Stage.Rotation = repeat / 4f;
                Playfield.Stage.Shear = new Vector2(.3f, -.2f);
                foreach (var note in notes)
                {
                    note.Y = -400.25f + repeat * 17.125f;
                    compare(note, expectDirect: true);
                    note.ApplyNoteHeightScale(1.37f);
                    compare(note, expectDirect: false, update: false);
                    var sprite = ((BmsNoteVisual)layerField.GetValue(note)!).Drawable;
                    sprite.Position = new Vector2(2.25f, -3.5f);
                    compare(note, expectDirect: false, update: false);
                    sprite.Position = Vector2.Zero;
                    compare(note, expectDirect: false, update: false);
                    sprite.Scale = new Vector2(1.1f, .8f);
                    compare(note, expectDirect: false, update: false);
                    Assert.That(sprite.Scale, Is.EqualTo(new Vector2(1.1f, .8f)));
                    sprite.Scale = Vector2.One;
                    compare(note, expectDirect: false, update: false);
                    padding.SetValue(note, new MarginPadding { Left = 2, Right = 3, Top = 1, Bottom = 4 });
                    childSize.SetValue(note, new Vector2(.75f, 1.25f));
                    compare(note, expectDirect: false, update: false);
                    note.ApplyNoteHeightScale(1);
                    compare(note, expectDirect: false, update: false);
                    padding.SetValue(note, default(MarginPadding));
                    childSize.SetValue(note, Vector2.One);
                    compare(note, expectDirect: true);
                    note.UpdateSoftConstantVisibility(.4f);
                    compare(note, expectDirect: false);
                    sprite.Alpha = .7f;
                    compare(note, expectDirect: false);
                    Assert.That(sprite.Alpha, Is.EqualTo(.7f));
                    sprite.Alpha = 1;
                    compare(note, expectDirect: false);
                    padding.SetValue(note, new MarginPadding { Left = 2, Right = 3, Top = 1, Bottom = 4 });
                    childSize.SetValue(note, new Vector2(.75f, 1.25f));
                    compare(note, expectDirect: false);
                    note.UpdateSoftConstantVisibility(1);
                    compare(note, expectDirect: false);
                    padding.SetValue(note, default(MarginPadding));
                    childSize.SetValue(note, Vector2.One);
                    note.Colour = ColourInfo.GradientHorizontal(Color4.Red, Color4.White);
                    compare(note, expectDirect: false);
                    note.Colour = Colour4.White;
                    compare(note, expectDirect: true);
                }
            }
        });
        AddStep("prepare lifecycle appearance", () =>
        {
            if (!nonDefaultAppearance) return;
            Playfield.Stage.SetNoteHeightScale(1.37f);
            foreach (var note in notes) note.UpdateSubTree();
        });
        AddStep("continuous appearance preserves wrapped composition", () =>
        {
            if (!nonDefaultAppearance) return;
            for (var repeat = 0; repeat <= 64; repeat++)
            foreach (var note in notes)
            {
                var controller = (BmsNoteVisual)layerField.GetValue(note)!;
                var sprite = controller.Drawable;
                note.UpdateSoftConstantVisibility(repeat / 64f);
                Assert.That(controller.Parent, Is.SameAs(note));
                Assert.That(sprite.Parent, Is.SameAs(controller));
                Assert.That(controller.Scale, Is.EqualTo(new Vector2(1, 1.37f)));
                Assert.That(controller.Alpha, Is.EqualTo(repeat / 64f));
                Assert.That(sprite.Scale, Is.EqualTo(Vector2.One));
                Assert.That(sprite.Alpha, Is.EqualTo(1));
                if (repeat > 0) compare(note, expectDirect: false, update: false);
            }
        });
        AddStep("reload skin controllers", () =>
        {
            if (nonDefaultAppearance)
            {
                foreach (var note in notes)
                {
                    note.UpdateSoftConstantVisibility(.4f);
                    compare(note, expectDirect: false);
                }
            }
            previousSprites = controllers.Select(visual => (BmsResolvedNoteSprite)visual.Drawable).ToArray();
            Assert.That(controllers.All(visual => (visual.Parent == null) == !nonDefaultAppearance), Is.True);
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!
                .Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("replacement components loaded", () => controllers.All(visual =>
            visual.Drawable is BmsResolvedNoteSprite { IsLoaded: true } sprite && !previousSprites.Contains(sprite)));
        AddStep("reloaded visual composition preserved", () =>
        {
            Assert.That(previousSprites.All(disposed), Is.True);
            // Skin reload recreates the HUD's height setting. Reapply the test layout so
            // the new resources are compared under the same non-default geometry.
            if (nonDefaultAppearance) Playfield.Stage.SetNoteHeightScale(1.37f);
            foreach (var note in notes)
            {
                compare(note, expectDirect: !nonDefaultAppearance);
                assertLifecycleAppearance(note);
            }
        });
        AddStep("return notes to pool", () => Player.GameplayClockContainer.Seek(12000));
        AddUntilStep("representations idle", () =>
            Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 12000) < .001 && notes.All(note => !note.IsInUse));
        AddStep("change skin while representations idle", () =>
        {
            previousSprites = controllers.Select(visual => (BmsResolvedNoteSprite)visual.Drawable).ToArray();
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!
                .Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("idle controllers preloaded new skins", () => controllers.All(visual =>
            visual.Drawable.LoadState >= LoadState.Ready && !previousSprites.Contains(visual.Drawable)));
        AddStep("rewind to reuse representations", () =>
        {
            if (nonDefaultAppearance) Playfield.Stage.SetNoteHeightScale(1.37f);
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("original representations reused", () =>
            Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001 && notes.All(note => note.IsInUse && note.IsPresent));
        AddUntilStep("reused static sprites attached to expected parent", () => controllers.All(visual =>
            visual.Drawable.IsLoaded && (visual.Parent == null) == !nonDefaultAppearance));
        AddStep("reused components preserve every buffer", () =>
        {
            foreach (var note in notes)
            {
                compare(note, expectDirect: !nonDefaultAppearance);
                assertLifecycleAppearance(note);
            }
        });
        AddStep("unload player through screen lifetime", () =>
        {
            finalSprites = controllers.Select(visual => visual.Drawable).ToArray();
            Stack.Exit();
        });
        AddUntilStep("owner released", () => notes.All(disposed));
        AddStep("controllers disposed with owner", () =>
        {
            Assert.That(controllers.All(disposed), Is.True);
            Assert.That(finalSprites.All(disposed), Is.True);
        });

        void assertLifecycleAppearance(DrawableBmsHitObject note)
        {
            if (!nonDefaultAppearance) return;
            var controller = (BmsNoteVisual)layerField.GetValue(note)!;
            Assert.That(controller.Scale, Is.EqualTo(new Vector2(1, 1.37f)));
            Assert.That(controller.Alpha, Is.EqualTo(1), "The column must reset the previous fade on reuse.");
            Assert.That(controller.Parent, Is.SameAs(note));
            Assert.That(controller.Drawable.Parent, Is.SameAs(controller));
            Assert.That(controller.Drawable.Scale, Is.EqualTo(Vector2.One));
            Assert.That(controller.Drawable.Alpha, Is.EqualTo(1));
        }

        void compare(DrawableBmsHitObject note, bool expectDirect, bool update = true)
        {
            var controller = (BmsNoteVisual)layerField.GetValue(note)!;
            if (update) note.UpdateSubTree();
            else refresh.Invoke(note, null);
            var sprite = controller.Drawable;
            Assert.That(controller.Parent == null, Is.EqualTo(expectDirect));
            Assert.That(sprite.Parent, Is.SameAs(expectDirect ? (CompositeDrawable)note : controller));
            var matrix = sprite.DrawInfo.Matrix;
            var inverse = sprite.DrawInfo.MatrixInverse;
            var colour = channels(sprite.DrawColourInfo.Colour);
            var buffers = Enumerable.Range(0, 3).Select(slot => BmsSpriteDrawNodeSnapshot.ReadQuad(generate(sprite, ++frame, slot, false)!)).ToArray();
            forceBatch.SetValue(controller, true);
            if (expectDirect)
            {
                // A scheduled skin refresh restores the exact former parent before replacing
                // its child; invoke that transition without replacing the resource here.
                typeof(DrawableBmsHitObject).GetMethod("restoreSinglePieceWrapper", flags)!.Invoke(note, null);
            }
            controller.UpdateSubTree();
            Assert.That(sprite.DrawInfo.Matrix, Is.EqualTo(matrix));
            Assert.That(sprite.DrawInfo.MatrixInverse, Is.EqualTo(inverse));
            Assert.That(channels(sprite.DrawColourInfo.Colour), Is.EqualTo(colour));
            for (var slot = 0; slot < 3; slot++)
                Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(generate(sprite, ++frame, slot, false)!), Is.EqualTo(buffers[slot]));
            forceBatch.SetValue(controller, false);
            refresh.Invoke(note, null);
        }

        static float[] channels(ColourInfo colour) =>
        [
            colour.TopLeft.SRGB.R, colour.TopLeft.SRGB.G, colour.TopLeft.SRGB.B, colour.TopLeft.SRGB.A,
            colour.TopRight.SRGB.R, colour.TopRight.SRGB.G, colour.TopRight.SRGB.B, colour.TopRight.SRGB.A,
            colour.BottomLeft.SRGB.R, colour.BottomLeft.SRGB.G, colour.BottomLeft.SRGB.B, colour.BottomLeft.SRGB.A,
            colour.BottomRight.SRGB.R, colour.BottomRight.SRGB.G, colour.BottomRight.SRGB.B, colour.BottomRight.SRGB.A,
        ];
    }
}
