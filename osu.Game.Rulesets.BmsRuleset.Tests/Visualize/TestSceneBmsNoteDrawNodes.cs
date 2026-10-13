#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Runtime;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsNoteDrawNodes : BmsPlayerTestScene
{
    private BmsTestSkins.SkinKind skin;
    private BmsRulesetConfigManager config => (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)], skin);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            HitObjects =
            [
                new BmsLongNote { StartTime = 5000, Duration = 2000, Column = 1 },
                new BmsNote { StartTime = 5000, Column = 2 },
                new BmsLandmine { StartTime = 5000, Column = 3, LandmineDamagePercent = 5 },
                new BmsNote { StartTime = 30000, Column = 2 },
            ],
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 5000, Column = 4 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void TestFlattenedNoteSnapshotsMatchCompositeNodes(BmsTestSkins.SkinKind skin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readSource = typeof(DrawNode).GetProperty("Source", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, IDrawable>>();
        Func<DrawNode, osu.Framework.Graphics.Primitives.Quad> readQuad = BmsSpriteDrawNodeSnapshot.ReadQuad;
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        Func<DrawNode, Texture?> readTexture = BmsSpriteDrawNodeSnapshot.ReadTexture;
        var readMasking = typeof(CompositeDrawable).GetNestedType("CompositeDrawableDrawNode", BindingFlags.NonPublic)!
                                                  .GetField("maskingInfo", flags)!;
        var canFlatten = typeof(CompositeDrawable).GetProperty("CanBeFlattened", flags)!.GetMethod!
            .CreateDelegate<Func<CompositeDrawable, bool>>();
        var localVertexBatch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
        var masking = typeof(CompositeDrawable).GetProperty("Masking", flags | BindingFlags.Public)!;
        var remove = typeof(CompositeDrawable).GetMethod("RemoveInternal", flags)!
            .CreateDelegate<Func<CompositeDrawable, Drawable, bool, bool>>();
        var add = typeof(CompositeDrawable).GetMethod("AddInternal", flags)!
            .CreateDelegate<Action<CompositeDrawable, Drawable>>();
        var readChildren = typeof(CompositeDrawable).GetProperty("InternalChildren", flags)!.GetMethod!
            .CreateDelegate<Func<CompositeDrawable, System.Collections.Generic.IReadOnlyList<Drawable>>>();
        var frame = 0UL;
        CompositeDrawable[] wrappers = [];
        var visualLayer = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
        var restoreVisual = typeof(DrawableBmsHitObject).GetMethod("restoreSinglePieceWrapper", flags)!
            .CreateDelegate<Action<DrawableBmsHitObject>>();

        AddStep("choose skin", () => this.skin = skin);
        AddStep("load player", () =>
        {
            config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze before first note", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("simulation reached note", () =>
            Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < 0.001);
        AddUntilStep("note skin layers loaded", () =>
        {
            wrappers = Playfield.ChildrenOfType<DrawableBmsHitObject>().Cast<CompositeDrawable>()
                                .Concat(Playfield.ChildrenOfType<BmsCachedSkinnableDrawable>())
                                .Concat(Playfield.ChildrenOfType<BmsResolvedNotePiece>())
                                .Concat(Playfield.ChildrenOfType<DrawableBmsHitObject>()
                                                 .Where(note => note.HitObject is BmsLongNote)
                                                 .Select(note => (CompositeDrawable)visualLayer.GetValue(note)!))
                                .Concat(Playfield.ChildrenOfType<BmsSegmentedLongNoteBody>().Where(body => !body.Masking))
                                .Where(wrapper => wrapper.IsLoaded).ToArray();
            return wrappers.OfType<DrawableBmsHitObject>().Count(note => note.IsPresent) == 4
                   && wrappers.OfType<BmsCachedSkinnableDrawable>().Any()
                   && (skin != BmsTestSkins.SkinKind.Legacy || wrappers.OfType<BmsSegmentedLongNoteBody>().Any())
                   && (wrappers.OfType<BmsResolvedNotePiece>().Any()
                       || Playfield.ChildrenOfType<BmsResolvedNoteSprite>().Any(sprite => sprite.IsLoaded));
        });
        AddStep("compare every draw buffer", assertSnapshots);
        AddStep("compare direct visual layers with former composition", () =>
        {
            foreach (var note in wrappers.OfType<DrawableBmsHitObject>().Where(note => note.HitObject is BmsNote or BmsLandmine))
            {
                // This comparison isolates removal of the former outer visibility layer.
                // Keep the skin controller mounted even when its appearance can be fused.
                var visual = (BmsNoteVisual)visualLayer.GetValue(note)!;
                localVertexBatch.SetValue(visual, true);
                restoreVisual(note);
                note.UpdateSoftConstantVisibility(0.4f);
                note.UpdateSubTree();
                Assert.That(visual.Parent, Is.SameAs(note));
                for (var slot = 0; slot < 3; slot++)
                {
                    var direct = snapshot(generate(note, ++frame, slot, true), out _, out _);
                    var originalLayer = new Container { RelativeSizeAxes = Axes.X, Alpha = visual.Alpha };
                    remove(note, visual, false);
                    visual.Alpha = 1;
                    originalLayer.Add(visual);
                    add(note, originalLayer);
                    note.UpdateSubTree();
                    var composed = snapshot(generate(note, ++frame, slot, true), out _, out _);
                    Assert.That(composed, Is.EqualTo(direct), $"Buffer {slot} changed direct visual state.");
                    originalLayer.Remove(visual, false);
                    remove(note, originalLayer, true);
                    add(note, visual);
                    visual.Alpha = 0.4f;
                    note.UpdateSubTree();
                }
                note.UpdateSoftConstantVisibility(1);
                localVertexBatch.SetValue(visual, false);
            }
        });
        AddStep("compare invisible marker with former composition", () =>
        {
            var note = wrappers.OfType<DrawableBmsInvisibleNote>().Single();
            var marker = note.ChildrenOfType<Container>().Single();
            Assert.That(marker.Parent, Is.SameAs(note));
            Assert.That(marker.Masking, Is.True);
            Assert.That(visualLayer.GetValue(note), Is.SameAs(marker));

            float[] heights = [0.5f, 1, 1.37f];
            float[] alphas = [1, 0.4f];
            foreach (var height in heights)
            foreach (var alpha in alphas)
            {
                Playfield.Stage.SetNoteHeightScale(height);
                note.UpdateSoftConstantVisibility(alpha);
                note.Alpha = 0.6f;
                note.UpdateSubTree();
                for (var slot = 0; slot < 3; slot++)
                {
                    var direct = snapshot(generate(note, ++frame, slot, true), out _, out _);
                    var originalMarker = new Container { RelativeSizeAxes = Axes.X, Scale = marker.Scale };
                    var originalLayer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Alpha = marker.Alpha,
                        Child = originalMarker,
                    };
                    remove(note, marker, false);
                    marker.Scale = Vector2.One;
                    marker.Alpha = 1;
                    originalMarker.Add(marker);
                    add(note, originalLayer);
                    note.UpdateSubTree();
                    var composed = snapshot(generate(note, ++frame, slot, true), out _, out _);
                    Assert.That(composed, Is.EqualTo(direct), $"Buffer {slot} changed invisible marker state at height {height}, alpha {alpha}.");
                    originalMarker.Remove(marker, false);
                    remove(note, originalLayer, true);
                    add(note, marker);
                    marker.Scale = new Vector2(1, height);
                    marker.Alpha = alpha;
                    note.UpdateSubTree();
                }
            }
            Playfield.Stage.SetNoteHeightScale(1);
            note.UpdateSoftConstantVisibility(1);
            note.Alpha = 1;
            note.UpdateSubTree();
        });
        AddStep("compare direct key pieces with former composition", () =>
        {
            foreach (var area in Playfield.ChildrenOfType<LegacyBmsKeyArea>())
            {
                var pieces = readChildren(area).ToArray();
                Assert.That(pieces, Has.Length.EqualTo(2));
                Assert.That(pieces.All(piece => piece.Parent == area), Is.True);
                for (var slot = 0; slot < 3; slot++)
                {
                    var direct = snapshot(generate(area, ++frame, slot, true), out _, out _);
                    var originalLayer = new Container
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        RelativeSizeAxes = Axes.Both,
                    };
                    foreach (var piece in pieces)
                    {
                        remove(area, piece, false);
                        originalLayer.Add(piece);
                    }
                    add(area, originalLayer);
                    area.UpdateSubTree();
                    var composed = snapshot(generate(area, ++frame, slot, true), out _, out _);
                    Assert.That(composed, Is.EqualTo(direct), $"Buffer {slot} changed key-area visual state.");
                    foreach (var piece in pieces)
                        originalLayer.Remove(piece, false);
                    remove(area, originalLayer, true);
                    foreach (var piece in pieces)
                        add(area, piece);
                    area.UpdateSubTree();
                }
            }
        });
        AddStep("compare changed appearance and geometry", () =>
        {
            foreach (var wrapper in wrappers.Where(wrapper => wrapper is DrawableBmsHitObject or BmsCachedSkinnableDrawable))
            {
                wrapper.Colour = Colour4.Cyan;
                wrapper.Alpha = 0.6f;
                wrapper.Scale = new Vector2(1.1f, 1.2f);
            }
            assertSnapshots();
        });
        AddStep("masking and proxies retain wrapper nodes", () =>
        {
            foreach (var wrapper in wrappers)
            {
                localVertexBatch.SetValue(wrapper, true);
                Assert.That(canFlatten(wrapper), Is.False);
                localVertexBatch.SetValue(wrapper, false);
                masking.SetValue(wrapper, true);
                Assert.That(canFlatten(wrapper), Is.False);
                masking.SetValue(wrapper, false);
                wrapper.CreateProxy();
                Assert.That(canFlatten(wrapper), Is.False);
            }
        });
        AddStep("restore invisible-note setting", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));

        void assertSnapshots()
        {
            Assert.That(wrappers.All(canFlatten), Is.True);
            Assert.That(wrappers.All(wrapper => wrapper.GetType().GetMethod("CreateDrawNode", flags)!.DeclaringType == typeof(CompositeDrawable)), Is.True);
            Playfield.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++)
            {
                var flattened = snapshot(generate(Playfield, ++frame, slot, false), out var flattenedWrappers, out var flattenedNotes);
                Assert.That(flattened, Is.Not.Empty);
                var marker = wrappers.OfType<DrawableBmsInvisibleNote>().Single().ChildrenOfType<Container>().Single(container => container.Masking);
                Assert.That(flattened.OfType<(IDrawable Source, MaskingInfo Masking, DrawColourInfo Colour)>()
                                     .Any(state => ReferenceEquals(state.Source, marker)), Is.True);
                Assert.That(flattenedWrappers, Is.Zero);
                Assert.That(flattenedNotes, Is.Zero);
                foreach (var wrapper in wrappers)
                    localVertexBatch.SetValue(wrapper, true);
                Playfield.UpdateSubTreeMasking();
                var composite = snapshot(generate(Playfield, ++frame, slot, true), out var compositeWrappers, out var compositeNotes);
                Assert.That(compositeWrappers, Is.GreaterThan(0));
                Assert.That(compositeNotes, Is.EqualTo(4));
                Assert.That(composite, Is.EqualTo(flattened), $"Buffer {slot} changed rendered sprite or masking state.");
                foreach (var wrapper in wrappers)
                    localVertexBatch.SetValue(wrapper, false);
                Playfield.UpdateSubTreeMasking();
            }
        }

        object[] snapshot(DrawNode? node, out int wrapperCount, out int noteCount)
        {
            var states = new List<object>();
            var count = 0;
            var notes = 0;
            if (node != null)
                visit(node);
            wrapperCount = count;
            noteCount = notes;
            return states.ToArray();

            void visit(DrawNode child)
            {
                if (BmsColumnSpriteBatchSnapshot.Read(child) is { } batch)
                {
                    states.AddRange(batch.Select(sprite => (object)((IDrawable)sprite.Sprite, sprite.Texture, sprite.Quad, sprite.Colour)));
                    return;
                }
                if (readSource(child) is CompositeDrawable wrapper && wrapper is not DrawableBmsHitObject && wrappers.Contains(wrapper))
                    count++;
                if (readSource(child) is DrawableBmsHitObject)
                    notes++;
                if (BmsSpriteDrawNodeSnapshot.IsSprite(child))
                    states.Add((readSource(child), readTexture(child), readQuad(child), readColour(child)));
                if (readSource(child) is CompositeDrawable { Masking: true } && readMasking.GetValue(child) is MaskingInfo mask)
                    states.Add((readSource(child), mask, readColour(child)));
                if (child is ICompositeDrawNode { Children: { } children })
                    foreach (var nested in children)
                        visit(nested);
            }
        }
    }
}
