#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsNoteDrawNodes
{
    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void SinglePieceHierarchyRetainsIndependentSkinOperations(BmsTestSkins.SkinKind selectedSkin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var layer = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
        var refresh = typeof(DrawableBmsHitObject).GetMethod("refreshSinglePieceVisual", flags)!;
        DrawableBmsHitObject[] notes = [];

        AddStep("load real static skin components", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("stop before notes", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("static skin components loaded", () =>
        {
            notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
                             .Where(note => note.HitObject is BmsNote or BmsLandmine).ToArray();
            return Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001
                   && notes.Length == 2
                   && notes.All(note => ((BmsNoteVisual)layer.GetValue(note)!).Drawable is BmsResolvedNoteSprite { IsLoaded: true });
        });
        AddStep("preserve child colours and transform start values", () =>
        {
            foreach (var note in notes)
            {
                var controller = (BmsNoteVisual)layer.GetValue(note)!;
                var sprite = controller.Drawable;
                var wrapperGradient = ColourInfo.GradientHorizontal(Color4.White, Color4.Red);
                controller.Colour = wrapperGradient;
                refresh.Invoke(note, null);
                assertWrapped();
                Assert.That(controller.Colour, Is.EqualTo(wrapperGradient));
                controller.Colour = Colour4.White;
                refresh.Invoke(note, null);
                Assert.That(controller.Parent, Is.Null);

                note.ApplyNoteHeightScale(1.37f);
                note.UpdateSoftConstantVisibility(.4f);
                assertWrapped();

                ColourInfo[] colours =
                [
                    new Color4(.3f, .6f, .9f, .7f),
                    ColourInfo.GradientHorizontal(new Color4(.3f, .6f, .9f, .7f), new Color4(.8f, .2f, .4f, .3f)),
                ];
                foreach (var colour in colours)
                {
                    sprite.Colour = colour;
                    refresh.Invoke(note, null);
                    assertWrapped();
                    var expected = note.DrawColourInfo.Colour;
                    expected.ApplyChild(controller.Colour.MultiplyAlpha(.4f));
                    expected.ApplyChild(colour);
                    Assert.That(sprite.DrawColourInfo.Colour, Is.EqualTo(expected));
                    sprite.Colour = Colour4.White;
                    refresh.Invoke(note, null);
                    assertWrapped();
                }

                controller.Colour = colours[1];
                refresh.Invoke(note, null);
                assertWrapped();
                Assert.That(controller.Colour, Is.EqualTo(colours[1]));
                controller.Colour = Colour4.White;
                refresh.Invoke(note, null);
                assertWrapped();

                // Height remains on the wrapper so child animations start from their intrinsic values.
                sprite.ScaleTo(new Vector2(.8f, .9f), 1000);
                refresh.Invoke(note, null);
                assertWrapped();
                sprite.UpdateSubTree();
                Assert.That(sprite.Scale, Is.EqualTo(Vector2.One), "The child transform must start at its intrinsic scale.");
                sprite.FinishTransforms();
                Assert.That(sprite.Scale, Is.EqualTo(new Vector2(.8f, .9f)));
                sprite.Scale = Vector2.One;
                refresh.Invoke(note, null);
                assertWrapped();

                sprite.FadeTo(.7f, 1000);
                refresh.Invoke(note, null);
                assertWrapped();
                sprite.UpdateSubTree();
                Assert.That(sprite.Alpha, Is.EqualTo(1), "The child fade must start at its intrinsic opacity.");
                sprite.FinishTransforms();
                Assert.That(sprite.Alpha, Is.EqualTo(.7f));
                sprite.Alpha = 1;
                refresh.Invoke(note, null);
                assertWrapped();

                controller.FadeTo(.6f, 1000);
                refresh.Invoke(note, null);
                assertWrapped();
                controller.UpdateSubTree();
                Assert.That(controller.Alpha, Is.EqualTo(.4f));
                controller.FinishTransforms();
                Assert.That(controller.Alpha, Is.EqualTo(.6f));
                Assert.That(sprite.Alpha, Is.EqualTo(1));

                void assertWrapped()
                {
                    Assert.That(controller.Parent, Is.SameAs(note));
                    Assert.That(sprite.Parent, Is.SameAs(controller));
                    Assert.That(sprite.Scale, Is.EqualTo(Vector2.One));
                    Assert.That(sprite.Alpha, Is.EqualTo(1));
                }
            }
        });
    }
}
