#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Events;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsMineBatchLifecycle : BmsPlayerTestScene
{
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Func<Drawable, ulong, int, bool, DrawNode?> generate = typeof(Drawable)
        .GetMethod("GenerateDrawNodeSubtree", flags)!.CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
    private static readonly Func<DrawNode, IDrawable> sourceOf = typeof(DrawNode).GetProperty("Source", flags)!.GetMethod!
        .CreateDelegate<Func<DrawNode, IDrawable>>();
    private static readonly Func<DrawNode, DrawColourInfo> colourOf = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
        .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
    private static readonly PropertyInfo forceBatch = typeof(osu.Framework.Graphics.Containers.CompositeDrawable).GetProperty("ForceLocalVertexBatch")!;
    private BmsTestSkins.SkinKind skin;
    private BmsColumnHitObjectContainer column = null!;
    private ulong frame;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(4999, BmsAction.Key2),
            new BmsReplayFrame(5200), new BmsReplayFrame(60000)], skin);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2 };
        for (var i = 0; i < 128; i++)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 5000 + i * .5, Column = 2, LandmineDamagePercent = .001 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 5010, Column = 2 });
        beatmap.HitObjects.Add(new BmsLongNote { StartTime = 5030.25, Duration = 1000, Column = 2 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 30000, Column = 3 });
        beatmap.HitObjects.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void DenseMinesMatchNativeGeometryOrderAndRetainedBuffers(BmsTestSkins.SkinKind selectedSkin)
    {
        DrawNode retained = null!;
        State[] expected = [];
        State[] retainedStates = [];
        prepare(selectedSkin);
        foreach (var scale in new[] { 1f, 1.37f })
        {
            AddStep("native mines at height scale " + scale, () =>
            {
                Playfield.Stage.SetNoteHeightScale(scale);
                forceBatch.SetValue(column, true);
            });
            AddUntilStep("all native mines ready", () => !column.BatchMinesActive
                && Playfield.AllColumnAliveObjects().Count(d => d.HitObject is BmsLandmine && d.IsPresent) == 128);
            AddStep("capture native order and geometry", () =>
            {
                Playfield.UpdateSubTreeMasking();
                expected = read(generate(column, ++frame, 0, true)!);
                Assert.That(expected.Count(s => s.Note is BmsLandmine), Is.EqualTo(128));
                forceBatch.SetValue(column, false);
            });
            AddUntilStep("dense mines batched again", () => column.BatchMinesActive && residentMines() == 1);
            AddStep("compare all buffer slots", () =>
            {
                Playfield.UpdateSubTreeMasking();
                for (var slot = 0; slot < 3; slot++)
                {
                    var node = generate(column, ++frame, slot, true)!;
                    Assert.That(mines(node), Has.Count.EqualTo(127));
                    compare(expected, read(node));
                }
                retained = generate(column, ++frame, 0, true)!;
                retainedStates = read(retained);
            });
        }
        AddStep("reload active mine skin", () =>
        {
            retained = generate(column, ++frame, 0, true)!;
            retainedStates = read(retained);
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!
                .Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
            _ = generate(column, ++frame, 1, true);
            Assert.That(read(retained), Is.EqualTo(retainedStates));
        });
        AddWaitStep("allow replacement sprite to update", 3);
        AddStep("restore native comparison after skin reload", () => forceBatch.SetValue(column, true));
        AddUntilStep("replacement native mines ready", () => !column.BatchMinesActive && residentMines() == 128);
        AddStep("capture replacement skin reference", () =>
        {
            Playfield.UpdateSubTreeMasking();
            expected = read(generate(column, ++frame, 0, true)!);
            forceBatch.SetValue(column, false);
        });
        AddUntilStep("replacement mine template active", () => column.BatchMinesActive && residentMines() == 1);
        seek(6000);
        AddStep("expired mines removed from snapshots", () =>
        {
            for (var slot = 0; slot < 3; slot++) Assert.That(mines(generate(column, ++frame, slot, true)!), Is.Empty);
        });
        seek(4800);
        AddUntilStep("rewound batch restored", () => column.BatchMinesActive && residentMines() == 1);
        AddStep("restore native reference after replay rebuild", () => forceBatch.SetValue(column, true));
        AddUntilStep("rewound native mines ready", () => !column.BatchMinesActive && residentMines() == 128);
        AddStep("capture rewound native geometry and order", () =>
        {
            Playfield.UpdateSubTreeMasking();
            var restored = read(generate(column, ++frame, 0, true)!);
            Assert.That(restored, Has.Length.EqualTo(expected.Length));
            // Rebuilding reactivates native equal-time children; their insertion order can change.
            var objects = Playfield.Beatmap.HitObjects.ToArray();
            compare(expected.OrderBy(state => Array.IndexOf(objects, state.Note)).ToArray(),
                restored.OrderBy(state => Array.IndexOf(objects, state.Note)).ToArray());
            expected = restored;
            forceBatch.SetValue(column, false);
        });
        AddUntilStep("rewound mine batch active again", () => column.BatchMinesActive && residentMines() == 1);
        AddStep("rewound snapshots preserve native geometry", () =>
        {
            Playfield.UpdateSubTreeMasking();
            compare(expected, read(generate(column, ++frame, 2, true)!));
        });
    }

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void PartiallyVisibleMinesMatchNativeOpacity(BmsTestSkins.SkinKind selectedSkin)
    {
        State[] expected = [];
        prepare(selectedSkin);
        AddStep("enable soft constant during fade", () =>
        {
            var scroll = Playfield.ScrollController;
            var duration = BmsGameplayScrollController.ComputeScrollTime(scroll.ScrollSpeed) * scroll.ScrollRangeScale;
            scroll.SoftConstant.Value = true;
            scroll.SoftConstantFadeIn.Value = -200;
            Player.GameplayClockContainer.Seek(4800 + 5000 - Playfield.DisplayTime - (duration - 100) * scroll.PlaybackRate);
            forceBatch.SetValue(column, true);
        });
        AddUntilStep("native mines partially visible", () => !column.BatchMinesActive && residentMines() == 128
            && Math.Abs(Playfield.ScrollController.GetSoftConstantAlpha(5000) - .5f) < .00001);
        AddStep("capture partial native opacity", () =>
        {
            Playfield.UpdateSubTreeMasking();
            expected = read(generate(column, ++frame, 0, true)!);
            Assert.That(expected.Count(s => s.Note is BmsLandmine), Is.EqualTo(128));
            Assert.That(expected.Where(s => s.Note is BmsLandmine).All(s => s.Colour.Colour.MinAlpha is > 0 and < 1), Is.True);
            forceBatch.SetValue(column, false);
        });
        AddUntilStep("partial mine batch active", () => column.BatchMinesActive && residentMines() == 1);
        AddStep("compare partial opacity in all buffer slots", () =>
        {
            Playfield.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++) compare(expected, read(generate(column, ++frame, slot, true)!));
            Playfield.ScrollController.SoftConstant.Value = false;
            Playfield.ScrollController.SoftConstantFadeIn.Value = 0;
        });
    }

    [Test]
    public void HeldDenseMinesApplyResultsAndRewindWithoutResidentDrawables()
    {
        var health = 0d;
        double[] damageTimes = [];
        prepare(BmsTestSkins.SkinKind.Argon);
        AddAssert("avoided drawable lifecycles", () => residentMines() == 1);
        seek(5100);
        AddAssert("all held crossings reach score", () => mineResults() == 128);
        AddAssert("logical damage times preserved", () => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
            .Where(e => e.Source.Kind == BmsJudgementSourceKind.Landmine)
            .All(e => e.TimingObservations.Single().ActualTime == e.Source.StartTime));
        seek(4800);
        AddAssert("rewind removes all mine results", () => mineResults() == 0);
        AddUntilStep("rewound mines have no resident representations", () => residentMines() == 1);
        seek(5100);
        AddAssert("replayed crossings apply once", () => mineResults() == 128);
        AddStep("capture batched damage", () =>
        {
            health = Player.HealthProcessor.Health.Value;
            damageTimes = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
                .Where(e => e.Source.Kind == BmsJudgementSourceKind.Landmine).Select(e => e.TimingObservations.Single().ActualTime).ToArray();
            forceBatch.SetValue(column, true);
        });
        seek(4800);
        AddUntilStep("native mines restored before crossing", () => !column.BatchMinesActive && residentMines() == 128);
        seek(5100);
        AddAssert("native damage equals batched damage", () => Player.HealthProcessor.Health.Value == health
            && ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
                .Where(e => e.Source.Kind == BmsJudgementSourceKind.Landmine).Select(e => e.TimingObservations.Single().ActualTime).SequenceEqual(damageTimes));
    }

    [Test]
    public void GradientColumnsRetainNativeMines()
    {
        prepare(BmsTestSkins.SkinKind.Argon);
        AddStep("apply column gradient", () => column.Colour = ColourInfo.GradientVertical(Colour4.Red, Colour4.Blue));
        AddUntilStep("gradient restores native mine representations", () => !column.BatchMinesActive && residentMines() == 128);
        AddStep("remove column gradient", () => column.Colour = Colour4.White);
        AddUntilStep("static mine batch resumes", () => column.BatchMinesActive && residentMines() == 1);
    }

    [Test]
    public void SwitchingToAnimatedMineSkinRestoresNativeRepresentations()
    {
        ISkin[] original = [];
        prepare(BmsTestSkins.SkinKind.Legacy);
        AddStep("switch static mine skin to animation", () =>
        {
            var source = (SkinProvidingContainer)((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource;
            original = source.AllSources.ToArray();
            replace(source, BmsTestLegacySkin.CreateSkinSource(this, animatedMines: true).AllSources.ToArray());
        });
        AddUntilStep("animated mines use original drawable path", () => !column.BatchMinesActive && residentMines() == 128
            && Playfield.AllColumnAliveObjects().Where(d => d.HitObject is BmsLandmine)
                .All(d => d.ChildrenOfType<BmsResolvedNotePiece>().Any()));
        AddStep("restore static mine skin", () => replace((SkinProvidingContainer)((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, original));
        AddUntilStep("static mine representations batched again", () => column.BatchMinesActive && residentMines() == 1);

        static void replace(SkinProvidingContainer source, ISkin[] skins)
        {
            typeof(SkinProvidingContainer).GetMethod("SetSources", flags)!.Invoke(source, [skins]);
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!.Invoke(source, null);
        }
    }

    private void prepare(BmsTestSkins.SkinKind selectedSkin)
    {
        AddStep("load dense mine player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("finish entrance animations", () =>
        {
            // Native and batched captures span frames; player scaling continues after its fade ends.
            Player.FinishTransforms();
            Player.GameplayClockContainer.FinishTransforms();
        });
        seek(4800);
        AddUntilStep("mine template loaded and batch active", () =>
        {
            column = (BmsColumnHitObjectContainer)((BmsColumn)Playfield.Stage.Columns[2]).HitObjectContainer;
            return column.BatchMinesActive && residentMines() == 1;
        });
    }

    private int residentMines() => Playfield.AllColumnAliveObjects().Count(d => d.HitObject is BmsLandmine);
    private int mineResults() => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count(e => e.Source.Kind == BmsJudgementSourceKind.Landmine);

    private void seek(double time)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("clock reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private static List<BmsColumnHitObjectContainer.MineSnapshot> mines(DrawNode node)
        => (List<BmsColumnHitObjectContainer.MineSnapshot>)node.GetType().GetField("Mines", flags)!.GetValue(node)!;

    private readonly record struct State(BmsHitObject Note, Texture? Texture, Quad Quad, DrawColourInfo Colour);

    private static State[] read(DrawNode root)
    {
        var result = new List<State>();
        visit(root);
        return result.ToArray();

        void visit(DrawNode node)
        {
            if (node.GetType().GetMethod("prepareMineChildren", flags) is { } prepareChildren
                && node.GetType().GetField("interleavedMines", flags)!.GetValue(node) is true)
            {
                prepareChildren.Invoke(node, null);
                foreach (var child in (List<DrawNode>)node.GetType().GetField("mineChildren", flags)!.GetValue(node)!) visit(child);
                return;
            }
            if (node.GetType().Name == "MineRunDrawNode")
            {
                // The run reads its owning buffer's immutable mine list.
                var list = (List<BmsColumnHitObjectContainer.MineSnapshot>)node.GetType().GetField("Mines", flags)!.GetValue(node)!;
                var sprite = (BmsColumnHitObjectContainer.SpriteSnapshot)node.GetType().GetField("Sprite", flags)!.GetValue(node)!;
                var first = (int)node.GetType().GetField("First", flags)!.GetValue(node)!;
                var end = (int)node.GetType().GetField("End", flags)!.GetValue(node)!;
                for (var i = first; i < end; i++)
                {
                    var mine = list[i];
                    var quad = new Quad(sprite.Quad.TopLeft + mine.Offset, sprite.Quad.TopRight + mine.Offset,
                        sprite.Quad.BottomLeft + mine.Offset, sprite.Quad.BottomRight + mine.Offset);
                    var colour = sprite.Colour;
                    colour.Colour = colour.Colour.MultiplyAlpha(mine.Alpha);
                    result.Add(new State(mine.Note, sprite.Texture, quad, colour));
                }
                return;
            }
            if (BmsSpriteDrawNodeSnapshot.IsSprite(node))
            {
                var drawable = sourceOf(node) as Drawable;
                while (drawable != null && drawable is not DrawableBmsHitObject) drawable = drawable.Parent;
                if (drawable is DrawableBmsHitObject note)
                    result.Add(new State(note.HitObject, BmsSpriteDrawNodeSnapshot.ReadTexture(node), BmsSpriteDrawNodeSnapshot.ReadQuad(node), colourOf(node)));
            }
            if (node is ICompositeDrawNode { Children: { } children }) foreach (var child in children) visit(child);
        }
    }

    private static void compare(State[] expected, State[] actual)
    {
        Assert.That(actual.Select(s => s.Note), Is.EqualTo(expected.Select(s => s.Note)), "draw order");
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That(actual[i].Texture, Is.SameAs(expected[i].Texture));
            Assert.That(actual[i].Colour.Blending, Is.EqualTo(expected[i].Colour.Blending));
            Assert.That((actual[i].Colour.Colour.TopLeft.ToVector() - expected[i].Colour.Colour.TopLeft.ToVector()).Length, Is.LessThan(.00001),
                $"{expected[i].Note.GetType().Name} at {expected[i].Note.StartTime}: native {expected[i].Colour.Colour.TopLeft}, batched {actual[i].Colour.Colour.TopLeft}");
            Assert.That((actual[i].Colour.Colour.BottomRight.ToVector() - expected[i].Colour.Colour.BottomRight.ToVector()).Length, Is.LessThan(.00001));
            Assert.That(Vector2.Distance(actual[i].Quad.TopLeft, expected[i].Quad.TopLeft), Is.LessThan(.001));
            Assert.That(Vector2.Distance(actual[i].Quad.BottomRight, expected[i].Quad.BottomRight), Is.LessThan(.001));
        }
    }
}
