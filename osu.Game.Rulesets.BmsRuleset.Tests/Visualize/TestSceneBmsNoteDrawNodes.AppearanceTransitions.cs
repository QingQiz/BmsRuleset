#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsNoteDrawNodes
{
    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void AppearanceTransitionsPreserveIntrinsicChildValues(BmsTestSkins.SkinKind selectedSkin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var layer = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
        var refresh = typeof(DrawableBmsHitObject).GetMethod("refreshSinglePieceVisual", flags)!;
        var restore = typeof(DrawableBmsHitObject).GetMethod("restoreSinglePieceWrapper", flags)!;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var frame = 0UL;
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
        AddStep("preserve intrinsic appearance across owner transitions", () =>
        {
            foreach (var note in notes)
            {
                var controller = (BmsNoteVisual)layer.GetValue(note)!;
                var sprite = controller.Drawable;
                var intrinsicScale = new Vector2(.8f, .9f);
                note.ApplyNoteHeightScale(1);
                note.UpdateSoftConstantVisibility(1);
                sprite.Scale = intrinsicScale;
                sprite.Alpha = .7f;
                note.UpdateSubTree();
                assertDirect(intrinsicScale, .7f);
                assertFormerComposition();

                note.ApplyNoteHeightScale(1.37f);
                note.UpdateSoftConstantVisibility(.4f);
                Assert.That(controller.Parent, Is.SameAs(note));
                Assert.That(sprite.Parent, Is.SameAs(controller));
                Assert.That(sprite.Scale, Is.EqualTo(intrinsicScale));
                Assert.That(sprite.Alpha, Is.EqualTo(.7f));
                assertFormerComposition(expectDirect: false);
                note.ApplyNoteHeightScale(1);
                note.UpdateSoftConstantVisibility(1);
                assertDirect(intrinsicScale, .7f);
                assertFormerComposition();

                sprite.Scale = Vector2.One;
                sprite.Alpha = 1;
                note.ApplyNoteHeightScale(1.37f);
                note.UpdateSoftConstantVisibility(.4f);
                assertWrapped(Vector2.One, 1);
                assertFormerComposition(expectDirect: false);
                note.ApplyNoteHeightScale(1);
                note.UpdateSoftConstantVisibility(1);
                assertDirect(Vector2.One, 1);

                // Returning to identity must preserve the child's own animation start values.
                sprite.Scale = intrinsicScale;
                sprite.Alpha = .7f;
                sprite.ScaleTo(new Vector2(.3f, .5f), 1000);
                sprite.FadeTo(.6f, 1000);
                refresh.Invoke(note, null);
                sprite.UpdateSubTree();
                assertDirect(intrinsicScale, .7f);
                sprite.FinishTransforms();
                refresh.Invoke(note, null);
                assertDirect(new Vector2(.3f, .5f), .6f);
                sprite.ClearTransforms();

                bool[] changes = [true, false];
                foreach (var changeScale in changes)
                {
                    sprite.Scale = Vector2.One;
                    sprite.Alpha = 1;
                    note.ApplyNoteHeightScale(1.37f);
                    note.UpdateSoftConstantVisibility(.4f);
                    assertWrapped(Vector2.One, 1);
                    if (changeScale) sprite.Scale = intrinsicScale;
                    else sprite.Alpha = .7f;
                    assertWrapped(changeScale ? intrinsicScale : Vector2.One, changeScale ? 1 : .7f);
                    assertFormerComposition(expectDirect: false);
                    note.ApplyNoteHeightScale(1);
                    note.UpdateSoftConstantVisibility(1);
                    assertDirect(changeScale ? intrinsicScale : Vector2.One, changeScale ? 1 : .7f);
                    assertFormerComposition();
                }

                void assertDirect(Vector2 scale, float alpha)
                {
                    Assert.That(controller.Parent, Is.Null);
                    Assert.That(sprite.Parent, Is.SameAs(note));
                    Assert.That(sprite.Scale, Is.EqualTo(scale));
                    Assert.That(sprite.Alpha, Is.EqualTo(alpha));
                }

                void assertWrapped(Vector2 scale, float alpha)
                {
                    Assert.That(controller.Parent, Is.SameAs(note));
                    Assert.That(sprite.Parent, Is.SameAs(controller));
                    Assert.That(controller.Scale, Is.EqualTo(new Vector2(1, 1.37f)));
                    Assert.That(controller.Alpha, Is.EqualTo(.4f));
                    Assert.That(sprite.Scale, Is.EqualTo(scale));
                    Assert.That(sprite.Alpha, Is.EqualTo(alpha));
                }

                void assertFormerComposition(bool expectDirect = true)
                {
                    var matrix = sprite.DrawInfo.Matrix;
                    var inverse = sprite.DrawInfo.MatrixInverse;
                    var colour = sprite.DrawColourInfo;
                    var buffers = Enumerable.Range(0, 3)
                                            .Select(slot => BmsSpriteDrawNodeSnapshot.ReadQuad(generate(sprite, ++frame, slot, false)!)).ToArray();
                    restore.Invoke(note, null);
                    controller.UpdateSubTree();
                    Assert.That(sprite.DrawInfo.Matrix, Is.EqualTo(matrix));
                    Assert.That(sprite.DrawInfo.MatrixInverse, Is.EqualTo(inverse));
                    Assert.That(sprite.DrawColourInfo, Is.EqualTo(colour));
                    for (var slot = 0; slot < 3; slot++)
                        Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(generate(sprite, ++frame, slot, false)!), Is.EqualTo(buffers[slot]));
                    refresh.Invoke(note, null);
                    Assert.That(controller.Parent == null, Is.EqualTo(expectDirect));
                }
            }
        });
    }
}
