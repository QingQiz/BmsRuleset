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
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsNoteDrawNodes
{
    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void SinglePieceLayoutChangesMatchPublicCompositionState(BmsTestSkins.SkinKind selectedSkin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var layer = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
        var refresh = typeof(DrawableBmsHitObject).GetMethod("refreshSinglePieceVisual", flags)!;
        var padding = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.Padding))!;
        var childSize = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.RelativeChildSize))!;
        var childOffset = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.RelativeChildOffset))!;
        var masking = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.Masking))!;
        var batch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
        var autoSize = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.AutoSizeAxes))!;
        DrawableBmsHitObject[] notes = [];
        BmsNoteVisual[] controllers = [];

        AddStep("load composition references", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze before static notes", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("default static components mounted", () =>
        {
            if (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) >= .001) return false;
            notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
                .Where(note => note.HitObject is BmsNote or BmsLandmine).ToArray();
            controllers = notes.Select(note => (BmsNoteVisual)layer.GetValue(note)!).ToArray();
            return notes.Length == 2 && controllers.All(visual => visual.Parent == null && visual.Drawable is BmsResolvedNoteSprite { IsLoaded: true });
        });
        AddStep("repeated changes retain the required visual operations", () =>
        {
            foreach (var (note, visual) in notes.Zip(controllers))
            {
                for (var i = 0; i < 128; i++)
                {
                    note.Y = -400.25f + i * 17.125f;
                    compare(note, visual);
                }

                (Action Change, Action Restore)[] mutations =
                [
                    (() => visual.Alpha = .4f, () => visual.Alpha = 1),
                    (() => visual.Scale = new Vector2(1, 1.37f), () => visual.Scale = Vector2.One),
                    (() => visual.Position = new Vector2(2, -3), () => visual.Position = Vector2.Zero),
                    (() => visual.Rotation = 17, () => visual.Rotation = 0),
                    (() => visual.Shear = new Vector2(.3f, -.2f), () => visual.Shear = Vector2.Zero),
                    (() => visual.Anchor = Anchor.Centre, () => visual.Anchor = Anchor.TopLeft),
                    (() => visual.Origin = Anchor.BottomRight, () => visual.Origin = Anchor.TopLeft),
                    (() => visual.Margin = new MarginPadding(3), () => visual.Margin = default),
                    (() => visual.Colour = ColourInfo.GradientHorizontal(Colour4.Red, Colour4.White), () => visual.Colour = Colour4.White),
                    (() => masking.SetValue(visual, true), () => masking.SetValue(visual, false)),
                    (() => batch.SetValue(visual, true), () => batch.SetValue(visual, false)),
                    (() => padding.SetValue(visual, new MarginPadding(3)), () => padding.SetValue(visual, default(MarginPadding))),
                    (() => childSize.SetValue(visual, new Vector2(.75f, 1.25f)), () => childSize.SetValue(visual, Vector2.One)),
                    (() => childOffset.SetValue(visual, new Vector2(.2f, -.1f)), () => childOffset.SetValue(visual, Vector2.Zero)),
                    (() => visual.Height = 3, () => visual.Height = 0),
                    (() => visual.Width = .7f, () => visual.Width = 1),
                    // With no parent, converting X at a factor of one leaves Width
                    // unchanged and may emit no invalidation; refresh must still see it.
                    (() => visual.RelativeSizeAxes = Axes.None, () => { visual.RelativeSizeAxes = Axes.X; visual.Width = 1; }),
                ];
                foreach (var (change, restore) in mutations)
                for (var repeat = 0; repeat < 3; repeat++)
                {
                    change();
                    compare(note, visual);
                    Assert.That(visual.Parent, Is.SameAs(note));
                    restore();
                    compare(note, visual);
                    Assert.That(visual.Parent, Is.Null);
                }
                autoSize.SetValue(visual, Axes.Y);
                compare(note, visual);
                autoSize.SetValue(visual, Axes.None);
                visual.Height = 0;
                compare(note, visual);
                using var proxy = visual.CreateProxy();
                compare(note, visual);
                Assert.That(visual.Parent, Is.SameAs(note));
            }
        });

        void compare(DrawableBmsHitObject note, BmsNoteVisual visual)
        {
            // Determine the expected parent from the public operations, independently
            // of the previous mounting and the controller's notification history.
            var direct = visual.Alpha == 1 && visual.Scale == Vector2.One
                         && visual.Position == Vector2.Zero && visual.Rotation == 0 && visual.Shear == Vector2.Zero
                         && visual.Anchor == Anchor.TopLeft && visual.Origin == Anchor.TopLeft
                         && visual.RelativeSizeAxes == Axes.X && visual.Size == new Vector2(1, 0)
                         && visual.Margin.Equals(default(MarginPadding)) && visual.Padding.Equals(default(MarginPadding))
                         && !visual.Masking && !visual.HasProxy && !visual.ForceLocalVertexBatch
                         && visual.Colour.Equals((ColourInfo)Colour4.White)
                         && visual.RelativeChildSize == Vector2.One && visual.RelativeChildOffset == Vector2.Zero
                         && note.RelativeChildSize == Vector2.One && note.RelativeChildOffset == Vector2.Zero
                         && note.Padding.Equals(default(MarginPadding)) && note.DrawHeight == 0
                         && note.DrawColourInfo.Colour.HasSingleColour;
            refresh.Invoke(note, null);
            Assert.That(visual.Parent == null, Is.EqualTo(direct));
            Assert.That(visual.Drawable.Parent, Is.SameAs(direct ? (CompositeDrawable)note : visual));
        }
    }
}
