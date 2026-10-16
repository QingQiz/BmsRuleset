#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsHitLightDataLifecycle : BmsPlayerTestScene
{
    private const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Func<Drawable, ulong, int, bool, DrawNode?> generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
        .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
    private static readonly PropertyInfo forceNative = typeof(CompositeDrawable).GetProperty("ForceLocalVertexBatch")!;
    private BmsColumn column = null!;
    private BmsHitExplosionLayer area = null!;
    private ulong frame;
    private BmsTestSkins.SkinKind skin;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)], skin);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8,
            HitObjects = [new BmsNote { StartTime = 60000, Column = 1 }] };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StaticPulsesMatchNativeSpritesAndPreserveBufferSnapshots(bool transformed)
    {
        State[] previous = [];
        DrawNode retained = null!;
        prepare(BmsTestSkins.SkinKind.Argon);
        AddStep("trigger overlapping static pulses", () =>
        {
            if (transformed)
            {
                area.Scale = new Vector2(.9f, .85f);
                area.Rotation = .1f;
                area.Position += new Vector2(2, -3);
            }
            for (var i = 0; i < 128; i++) column.TriggerHitExplosion(i % 2 == 0 ? BmsHitLightStyle.Note : BmsHitLightStyle.Hold);
        });
        seek(1080);
        AddStep("capture compact pulse buffers", () =>
        {
            Assert.That(area.Pulses, Has.Count.EqualTo(128));
            Assert.That(area.AliveChildren.OfType<BmsHitExplosion>(), Is.Empty);
            area.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++) Assert.That(read(generate(area, ++frame, slot, true)!), Has.Length.EqualTo(128));
            retained = generate(area, ++frame, 0, true)!;
            previous = read(retained);
            // Restoring the ordinary path must preserve the current envelope phase.
            forceNative.SetValue(area, true);
            area.UpdateSubTree();
            area.UpdateSubTreeMasking();
            var native = read(generate(area, ++frame, 1, true)!);
            compare(previous, native);
            Assert.That(read(retained), Is.EqualTo(previous));
        });
        seek(1200);
        AddUntilStep("all native pulses expire at original end", () => !area.AliveChildren.OfType<BmsHitExplosion>().Any());
        AddStep("restore compact pulses", () => forceNative.SetValue(area, false));
        AddStep("trigger changed pulse position", () => column.TriggerHitExplosion(BmsHitLightStyle.Hold));
        seek(1280);
        AddStep("changed source does not mutate another buffer", () =>
        {
            retained = generate(area, ++frame, 0, true)!;
            previous = read(retained);
            area.Colour = Colour4.Cyan;
            _ = generate(area, ++frame, 2, true);
            Assert.That(read(retained), Is.EqualTo(previous));
        });
        seek(1500);
        AddAssert("compact pulses expire", () => area.Pulses.Count == 0);
    }

    [Test]
    public void ReloadToAnimationPreservesLiveEnvelopeAndReturnsToStaticPulses()
    {
        ISkin[] original = [];
        prepare(BmsTestSkins.SkinKind.Argon);
        AddStep("start static pulse", () => column.TriggerHitExplosion(BmsHitLightStyle.Note));
        seek(1080);
        AddStep("switch active pulse to animated legacy skin", () =>
        {
            Assert.That(area.Pulses, Has.Count.EqualTo(1));
            var source = (SkinProvidingContainer)((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource;
            original = source.AllSources.ToArray();
            replace(source, BmsTestLegacySkin.CreateSkinSource(this).AllSources.ToArray());
        });
        AddUntilStep("live pulse uses animated native component", () => area.Pulses.Count == 0
            && area.AliveChildren.OfType<BmsHitExplosion>().SingleOrDefault() is { } pulse
            && pulse.ChildrenOfType<LegacyBmsHitExplosion>().Any());
        AddAssert("reload retains envelope rather than restarting", () => area.AliveChildren.OfType<BmsHitExplosion>().Single().Alpha == 1);
        seek(1190);
        AddAssert("converted pulse keeps original end", () => Math.Abs(area.AliveChildren.OfType<BmsHitExplosion>().Single().Alpha - 1f / 12) < .001);
        seek(1200);
        AddUntilStep("converted pulse expired", () => !area.AliveChildren.OfType<BmsHitExplosion>().Any());
        AddStep("restore static source", () => replace((SkinProvidingContainer)((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, original));
        AddWaitStep("allow replacement templates to load", 3);
        AddStep("trigger after restoring static skin", () => column.TriggerHitExplosion(BmsHitLightStyle.Note));
        AddUntilStep("static pulse uses compact storage again", () => area.Pulses.Count == 1);

        static void replace(SkinProvidingContainer source, ISkin[] skins)
        {
            typeof(SkinProvidingContainer).GetMethod("SetSources", flags)!.Invoke(source, [skins]);
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!.Invoke(source, null);
        }
    }

    private void prepare(BmsTestSkins.SkinKind selectedSkin)
    {
        AddStep("load hit light player", () => { skin = selectedSkin; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(1000);
        AddStep("find light layer", () => { column = (BmsColumn)Playfield.Stage.Columns[1]; area = (BmsHitExplosionLayer)column.HitExplosionArea; });
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("clock reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private readonly record struct State(Texture? Texture, Quad Quad, DrawColourInfo Colour);

    private static State[] read(DrawNode node)
    {
        var states = new List<State>();
        visit(node);
        return states.ToArray();

        void visit(DrawNode child)
        {
            if (child.GetType().DeclaringType == typeof(BmsHitExplosionLayer) && child.GetType().Name == "PulseDrawNode")
            {
                var pulses = (List<BmsHitExplosionLayer.Pulse>)child.GetType().GetField("Pulses", flags)!.GetValue(child)!;
                var time = (double)child.GetType().GetField("time", flags)!.GetValue(child)!;
                var vertical = (Vector2)child.GetType().GetField("vertical", flags)!.GetValue(child)!;
                foreach (var pulse in pulses)
                {
                    var sprite = (BmsHitExplosionLayer.SpriteState)child.GetType().GetField(pulse.Style == BmsHitLightStyle.Note ? "Normal" : "Hold", flags)!.GetValue(child)!;
                    var offset = -vertical * pulse.Offset;
                    var quad = new Quad(sprite.Quad.TopLeft + offset, sprite.Quad.TopRight + offset, sprite.Quad.BottomLeft + offset, sprite.Quad.BottomRight + offset);
                    var colour = sprite.Colour;
                    colour.Colour = colour.Colour.MultiplyAlpha(BmsHitExplosion.AlphaAt(time - pulse.StartTime));
                    states.Add(new State(sprite.Texture, quad, colour));
                }
            }
            if (BmsSpriteDrawNodeSnapshot.IsSprite(child))
                states.Add(new State(BmsSpriteDrawNodeSnapshot.ReadTexture(child), BmsSpriteDrawNodeSnapshot.ReadQuad(child),
                    BmsSpriteDrawNodeSnapshot.Read<DrawColourInfo>(child, "DrawColourInfo")));
            if (child is ICompositeDrawNode { Children: { } children }) foreach (var nested in children) visit(nested);
        }
    }

    private static void compare(State[] expected, State[] actual)
    {
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        for (var i = 0; i < actual.Length; i++)
        {
            Assert.That(actual[i].Texture, Is.SameAs(expected[i].Texture));
            Assert.That(Vector2.Distance(actual[i].Quad.TopLeft, expected[i].Quad.TopLeft), Is.LessThan(.001));
            Assert.That(Vector2.Distance(actual[i].Quad.BottomRight, expected[i].Quad.BottomRight), Is.LessThan(.001));
            Assert.That((actual[i].Colour.Colour.TopLeft.ToVector() - expected[i].Colour.Colour.TopLeft.ToVector()).Length, Is.LessThan(.00001));
            Assert.That(actual[i].Colour.Blending, Is.EqualTo(expected[i].Colour.Blending));
        }
    }
}
