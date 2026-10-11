#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsColumnSpriteBatchLifecycle : BmsPlayerTestScene
{
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Func<Drawable, ulong, int, bool, DrawNode?> generate = typeof(Drawable)
        .GetMethod("GenerateDrawNodeSubtree", flags)!.CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
    private static readonly PropertyInfo forceBatch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
    private static readonly FieldInfo visualLayer = typeof(DrawableBmsHitObject).GetField("visualLayer", flags)!;
    private BmsTestSkins.SkinKind skin;
    private DrawableBmsHitObject[] notes = [];
    private BmsColumnHitObjectContainer column = null!;
    private ulong frame;
    private int comparisons;

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
                new BmsNote { StartTime = 5000, Column = 2 },
                new BmsNote { StartTime = 5050, Column = 2 },
                new BmsNote { StartTime = 5100, Column = 2 },
                new BmsNote { StartTime = 5150, Column = 2 },
                new BmsLongNote { StartTime = 5000, Duration = 2000, Column = 3 },
                new BmsNote { StartTime = 5000, Column = 3 },
                new BmsNote { StartTime = 5050, Column = 3 },
                new BmsNote { StartTime = 15000, Column = 3 },
                new BmsNote { StartTime = 15050, Column = 3 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void MixedColumnsKeepFlatteningAcrossOrderChangesAndPoolReuse(BmsTestSkins.SkinKind selectedSkin)
    {
        var readAlive = typeof(CompositeDrawable).GetProperty("AliveInternalChildren", flags)!.GetMethod!
            .CreateDelegate<Func<CompositeDrawable, IReadOnlyList<Drawable>>>();
        BmsColumnHitObjectContainer mixed = null!;
        DrawableBmsHitObject[] original = [];
        DrawableBmsHitObject[] future = [];

        PreparePlayer(selectedSkin);
        AddUntilStep("mixed column skins loaded", () =>
        {
            original = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
                .Where(n => n.HitObject.Column == 3 && n.HitObject.StartTime < 10000).ToArray();
            if (original.Length != 3 || original.Any(n => !n.IsLoaded || !n.IsPresent)) return false;
            mixed = (BmsColumnHitObjectContainer)original[0].Parent;
            return original.Count(n => n.SingleSpriteForBatch is { IsLoaded: true }) == 2;
        });
        AddStep("both chronological orders use the original flattened parent tree", () =>
        {
            var longNote = original.Single(n => n.HitObject is BmsLongNote);
            foreach (var firstIsStatic in new[] { true, false })
            {
                foreach (var note in original) note.Alpha = .8f;
                // The hit-object container sorts by the bound start time, not Depth.
                longNote.StartTimeBindable.Value = firstIsStatic ? 4900 : 5100;
                mixed.UpdateSubTree();
                Assert.That(readAlive(mixed).First().GetType() == longNote.GetType(),
                    Is.EqualTo(!firstIsStatic), "The fixture must exercise both the static and long-note prefixes.");
                compareMixedParent();
            }
            longNote.StartTimeBindable.Value = 5000;
        });
        AddStep("retire mixed notes", () => Player.GameplayClockContainer.Seek(9000));
        AddUntilStep("original mixed notes returned to pool", () => original.All(n => !n.IsInUse)
            && Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 9000) < .001);
        AddStep("activate later static notes", () => Player.GameplayClockContainer.Seek(14800));
        AddUntilStep("later static sprites loaded", () =>
        {
            future = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>()
                .Where(n => n.HitObject.Column == 3 && n.HitObject.StartTime >= 15000).ToArray();
            return future.Length == 2 && future.All(n => n.SingleSpriteForBatch is { IsLoaded: true })
                   && Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 14800) < .001;
        });
        AddStep("static-only column resumes batch snapshots", () =>
        {
            Assert.That(future.All(n => n.Parent == mixed), Is.True);
            foreach (var note in future) note.Alpha = .8f;
            Playfield.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++)
                Assert.That(BmsColumnSpriteBatchSnapshot.Read(generate(mixed, ++frame, slot, false)!), Has.Length.EqualTo(2));
        });
        AddStep("rewind to original mixed notes", () => Player.GameplayClockContainer.Seek(4800));
        AddUntilStep("mixed bindings restored", () => original.All(n => n.IsInUse && n.IsPresent)
            && Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddStep("reused long note restores original flattening", () =>
        {
            foreach (var note in original) note.Alpha = .8f;
            compareMixedParent();
        });

        void compareMixedParent()
        {
            Playfield.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++)
            {
                var parent = generate((Drawable)mixed.Parent, ++frame, slot, false)!;
                Assert.That(ContainsColumnNode(parent), Is.False, "Mixed visuals should flatten before attempting a partial snapshot.");
                var states = ReadStates(parent);
                forceBatch.SetValue(mixed, true);
                try
                {
                    var reference = generate((Drawable)mixed.Parent, ++frame, (slot + 1) % 3, true)!;
                    Assert.That(ReadStates(reference), Is.EqualTo(states));
                    Assert.That(ReadStates(parent), Is.EqualTo(states));
                }
                finally { forceBatch.SetValue(mixed, false); }
            }
        }
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void LateWrappedNoteFallsBackWithoutChangingDrawState(BmsTestSkins.SkinKind selectedSkin)
    {
        var readAlive = typeof(CompositeDrawable).GetProperty("AliveInternalChildren", flags)!.GetMethod!
            .CreateDelegate<Func<CompositeDrawable, IReadOnlyList<Drawable>>>();
        PreparePlayer(selectedSkin);
        AddStep("compare successful and late-fallback captures", () =>
        {
            var ordered = readAlive(column).OfType<DrawableBmsHitObject>().ToArray();
            Assert.That(ordered, Has.Length.EqualTo(4));
            // Only the final note changes composition; the actual framework order
            // must retain a static prefix before discovering this wrapped child.
            var wrapper = (BmsNoteVisual)visualLayer.GetValue(ordered[^1])!;
            wrapper.Height = 3;
            ordered[^1].UpdateSubTree();
            Playfield.UpdateSubTreeMasking();
            Assert.That(ordered[0].SingleSpriteForBatch, Is.Not.Null);
            Assert.That(ordered[^1].SingleSpriteForBatch, Is.Null);
            SpriteState[] expected;
            forceBatch.SetValue(column, true);
            try { expected = ReadStates(generate((Drawable)column.Parent, ++frame, 0, false)!); }
            finally { forceBatch.SetValue(column, false); }
            Assert.That(expected.Count(state => Sprites().Contains(state.Sprite)), Is.EqualTo(4));
            for (var slot = 0; slot < 3; slot++)
            {
                var root = generate((Drawable)column.Parent, ++frame, slot, false)!;
                Assert.That(ContainsColumnNode(root), Is.True);
                Assert.That(ReadStates(root), Is.EqualTo(expected));
            }
            wrapper.Height = 0;
            ordered[^1].UpdateSubTree();
            CompareBuffers(4);
        });
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void MultipleNotesPreserveOrderAcrossReloadAndPoolReuse(BmsTestSkins.SkinKind selectedSkin)
    {
        var disposed = typeof(Drawable).GetProperty("IsDisposed", flags)!.GetMethod!.CreateDelegate<Func<Drawable, bool>>();
        var reload = typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!;
        var depth = typeof(CompositeDrawable).GetMethod("ChangeInternalChildDepth", flags)!
            .CreateDelegate<Action<CompositeDrawable, Drawable, float>>();
        DrawNode retained = null!;
        SpriteState[] retainedStates = [];
        BmsResolvedNoteSprite[] oldSprites = [];
        BmsResolvedNoteSprite[] finalSprites = [];

        PreparePlayer(selectedSkin);
        AddStep("compare ordered multi-note snapshots", () =>
        {
            for (var iteration = 0; iteration < 16; iteration++)
            {
                for (var index = 0; index < notes.Length; index++)
                {
                    depth(column, notes[index], iteration % 2 == 0 ? index : -index);
                    notes[index].Y = -100 + index * 2;
                    var sprite = notes[index].SingleSpriteForBatch!;
                    sprite.Colour = index % 2 == 0 ? Colour4.Red : Colour4.Blue;
                    sprite.Blending = index % 2 == 0 ? BlendingParameters.Mixture : BlendingParameters.Additive;
                }
                CompareBuffers(4);
            }
        });
        AddStep("wrapped columns flatten into the normal parent tree", () =>
        {
            Playfield.Stage.SetNoteHeightScale(1.37f);
            foreach (var note in notes) note.UpdateSubTree();
            Playfield.UpdateSubTreeMasking();
            var root = generate((Drawable)column.Parent, ++frame, 0, true)!;
            Assert.That(ContainsColumnNode(root), Is.False);
            Assert.That(ReadStates(root).Count(s => Sprites().Contains(s.Sprite)), Is.EqualTo(4));
            Playfield.Stage.SetNoteHeightScale(1);
            foreach (var note in notes) note.UpdateSubTree();
            CompareBuffers(4);
        });
        AddStep("retain snapshot before active reload", () =>
        {
            oldSprites = Sprites();
            Playfield.UpdateSubTreeMasking();
            retained = generate(column, ++frame, 0, false)!;
            retainedStates = ReadStates(retained);
            reload.Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
            _ = generate(column, ++frame, 1, false);
            Assert.That(ReadStates(retained), Is.EqualTo(retainedStates));
        });
        AddUntilStep("new active sprites attached", () => Sprites().Length == 4 && Sprites().All(s => s.IsLoaded && !oldSprites.Contains(s)));
        AddStep("new buffer observes reload without rewriting old buffer", () =>
        {
            Assert.That(oldSprites.All(disposed), Is.True);
            foreach (var note in notes) note.Alpha = .8f;
            CompareBuffers(4);
        });
        AddStep("return notes to pool", () => Player.GameplayClockContainer.Seek(12000));
        AddUntilStep("all original notes idle", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 12000) < .001
            && notes.All(n => !n.IsInUse));
        AddStep("empty columns remove prior snapshots", () =>
        {
            for (var slot = 0; slot < 3; slot++)
            {
                var node = generate(column, ++frame, slot, false);
                Assert.That(node, Is.Null, "An empty column should use the original flattened path.");
            }
            oldSprites = Sprites();
            reload.Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("idle sprites replaced", () => Sprites().Length == 4
            && Sprites().All(s => s.LoadState >= LoadState.Ready && !oldSprites.Contains(s)));
        AddStep("rewind to reuse original notes", () => Player.GameplayClockContainer.Seek(4800));
        AddUntilStep("original notes rebound", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001
            && notes.All(n => n.IsInUse && n.IsPresent && n.SingleSpriteForBatch is { IsLoaded: true }));
        AddStep("reused notes populate all buffers", () =>
        {
            Assert.That(oldSprites.All(disposed), Is.True);
            foreach (var note in notes) note.Alpha = .8f;
            CompareBuffers(4);
            finalSprites = Sprites();
            TestContext.WriteLine($"COLUMN_SPRITE_BATCH_LIFECYCLE comparisons={comparisons} notes={notes.Length} buffers=3");
            Stack.Exit();
        });
        AddUntilStep("owners and final sprites released", () => notes.All(disposed) && finalSprites.All(disposed));
    }

    private void PreparePlayer(BmsTestSkins.SkinKind selectedSkin)
    {
        AddStep("load multi-note player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze before notes", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(4800); });
        AddUntilStep("static notes loaded", () =>
        {
            if (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) >= .001) return false;
            notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().Where(n => n.HitObject.Column == 2).ToArray();
            if (notes.Length != 4 || notes.Any(n => n.SingleSpriteForBatch is not { IsLoaded: true })) return false;
            column = (BmsColumnHitObjectContainer)notes[0].Parent;
            return true;
        });
        AddStep("prepare partially opaque notes", () =>
        {
            foreach (var note in notes) note.Alpha = .8f;
            CompareBuffers(4);
        });
    }

    private BmsResolvedNoteSprite[] Sprites()
        => notes.Select(n => ((BmsNoteVisual)visualLayer.GetValue(n)!).Drawable).OfType<BmsResolvedNoteSprite>().ToArray();

    private void CompareBuffers(int expectedCount)
    {
        Playfield.UpdateSubTreeMasking();
        for (var slot = 0; slot < 3; slot++)
        {
            var node = generate(column, ++frame, slot, false)!;
            var batch = BmsColumnSpriteBatchSnapshot.Read(node);
            Assert.That(batch, Has.Length.EqualTo(expectedCount));
            var snapshot = ReadStates(node);
            forceBatch.SetValue(column, true);
            try
            {
                var reference = generate(column, ++frame, (slot + 1) % 3, false)!;
                Assert.That(ReadStates(reference), Is.EqualTo(snapshot), "The sequence must match framework child depth ordering.");
                Assert.That(ReadStates(node), Is.EqualTo(snapshot), "Another slot rewrote the retained snapshot.");
            }
            finally { forceBatch.SetValue(column, false); }
            comparisons++;
        }
    }

    private readonly record struct SpriteState(Sprite Sprite, Texture? Texture, Quad Quad, DrawColourInfo Colour,
                                               RectangleF Rectangle, RectangleF Coords, IShader Shader);

    private static bool ContainsColumnNode(DrawNode node)
        => node.GetType().DeclaringType == typeof(BmsColumnHitObjectContainer)
           || node is ICompositeDrawNode { Children: { } children } && children.Any(ContainsColumnNode);

    private static SpriteState[] ReadStates(DrawNode root)
    {
        var readSource = typeof(DrawNode).GetProperty("Source", flags)!.GetMethod!.CreateDelegate<Func<DrawNode, IDrawable>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!.CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var states = new List<SpriteState>();
        Visit(root);
        return states.ToArray();

        void Visit(DrawNode node)
        {
            if (BmsColumnSpriteBatchSnapshot.Read(node) is { } batch)
            {
                states.AddRange(batch.Select(s => new SpriteState(s.Sprite, s.Texture, s.Quad, s.Colour, s.Rectangle, s.TextureCoords, s.Shader)));
                return;
            }
            if (node is SpriteDrawNode)
                states.Add(new SpriteState((Sprite)readSource(node), BmsSpriteDrawNodeSnapshot.ReadTexture(node), BmsSpriteDrawNodeSnapshot.ReadQuad(node),
                    readColour(node), BmsSpriteDrawNodeSnapshot.Read<RectangleF>(node, "DrawRectangle"),
                    BmsSpriteDrawNodeSnapshot.Read<RectangleF>(node, "TextureCoords"),
                    (IShader)typeof(TexturedShaderDrawNode).GetField("textureShader", flags)!.GetValue(node)!));
            if (node is ICompositeDrawNode { Children: { } children }) foreach (var child in children) Visit(child);
        }
    }
}
