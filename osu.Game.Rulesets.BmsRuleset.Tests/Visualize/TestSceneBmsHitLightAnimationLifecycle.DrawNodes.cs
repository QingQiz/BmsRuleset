#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsHitLightAnimationLifecycle
{
    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void FlattenedHitLightsPreserveSpriteSnapshots(BmsTestSkins.SkinKind skin)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var readSource = typeof(DrawNode).GetProperty("Source", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, IDrawable>>();
        var readColour = typeof(DrawNode).GetProperty("DrawColourInfo", flags)!.GetMethod!
            .CreateDelegate<Func<DrawNode, DrawColourInfo>>();
        var canFlatten = typeof(CompositeDrawable).GetProperty("CanBeFlattened", flags)!.GetMethod!
            .CreateDelegate<Func<CompositeDrawable, bool>>();
        var localBatch = typeof(CompositeDrawable).GetProperty(nameof(CompositeDrawable.ForceLocalVertexBatch))!;
        var masking = typeof(CompositeDrawable).GetProperty("Masking", flags | BindingFlags.Public)!;
        var frame = 0UL;
        BmsColumn column = null!;
        BmsHitExplosion[] pulses = [];
        CompositeDrawable[] wrappers = [];

        AddStep("choose hit light skin", () => skinKind = skin);
        AddStep("load hit light player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(1000);
        AddStep("trigger overlapping note and hold lights", () =>
        {
            column = (BmsColumn)Playfield.Stage.Columns[1];
            localBatch.SetValue(column.HitExplosionArea, true);
            column.TriggerHitExplosion(BmsHitLightStyle.Note);
            column.TriggerHitExplosion(BmsHitLightStyle.Hold);
        });
        AddUntilStep("both lights loaded", () =>
        {
            pulses = column.HitExplosionArea.AliveChildren.OfType<BmsHitExplosion>().ToArray();
            return pulses.Length == 2 && pulses.All(pulse => pulse.ChildrenOfType<LegacyBmsHitExplosion>().Any(light => light.IsLoaded));
        });
        seek(1080);
        AddStep("capture both hit light wrappers", () => wrappers = pulses.Cast<CompositeDrawable>()
            .Concat(pulses.SelectMany(pulse => pulse.ChildrenOfType<LegacyBmsHitExplosion>())).ToArray());
        AddStep("compare static or animated lights in all buffers", () =>
        {
            var animations = pulses.SelectMany(pulse => pulse.ChildrenOfType<Drawable>()).OfType<IFramedAnimation>().ToArray();
            Assert.That(animations.Length, Is.EqualTo(skin == BmsTestSkins.SkinKind.Legacy ? 1 : 0));
            for (var index = 0; index < (skin == BmsTestSkins.SkinKind.Legacy ? 3 : 1); index++)
            {
                foreach (var animation in animations)
                    animation.GotoAndStop(index);
                column.HitExplosionArea.UpdateSubTree();
                compare();
            }
        });
        AddStep("compare changed geometry tint and alpha", () =>
        {
            var previous = generate(column.HitExplosionArea, ++frame, 0, true);
            var previousSnapshot = snapshot(previous, out _);
            foreach (var wrapper in wrappers)
            {
                wrapper.Colour = Colour4.Cyan;
                wrapper.Alpha = .6f;
                wrapper.Scale = new Vector2(.9f, .85f);
                wrapper.Rotation = .1f;
                wrapper.Position += new Vector2(2, -3);
            }
            column.HitExplosionArea.UpdateSubTreeMasking();
            var changed = snapshot(generate(column.HitExplosionArea, ++frame, 1, true), out _);
            Assert.That(changed, Is.Not.EqualTo(previousSnapshot));
            Assert.That(snapshot(previous, out _), Is.EqualTo(previousSnapshot), "Another draw buffer changed an existing snapshot.");
            compare();
        });
        AddStep("masking proxies and local batches retain wrapper nodes", () =>
        {
            foreach (var wrapper in wrappers)
            {
                localBatch.SetValue(wrapper, true);
                Assert.That(canFlatten(wrapper), Is.False);
                localBatch.SetValue(wrapper, false);
                masking.SetValue(wrapper, true);
                Assert.That(canFlatten(wrapper), Is.False);
                masking.SetValue(wrapper, false);
                wrapper.CreateProxy();
                Assert.That(canFlatten(wrapper), Is.False);
            }
        });

        void compare()
        {
            Assert.That(wrappers.All(canFlatten), Is.True);
            column.HitExplosionArea.UpdateSubTreeMasking();
            for (var slot = 0; slot < 3; slot++)
            {
                var flattened = snapshot(generate(column.HitExplosionArea, ++frame, slot, true), out var flattenedWrappers);
                Assert.That(flattened, Has.Length.EqualTo(2));
                Assert.That(flattenedWrappers, Is.Zero);
                foreach (var wrapper in wrappers)
                    localBatch.SetValue(wrapper, true);
                var composite = snapshot(generate(column.HitExplosionArea, ++frame, slot, true), out var compositeWrappers);
                Assert.That(compositeWrappers, Is.EqualTo(wrappers.Length));
                Assert.That(composite, Is.EqualTo(flattened), $"Buffer {slot} changed the hit lights.");
                foreach (var wrapper in wrappers)
                    localBatch.SetValue(wrapper, false);
            }
        }

        object[] snapshot(DrawNode? node, out int wrapperCount)
        {
            var states = new List<object>();
            var count = 0;
            if (node != null)
                visit(node);
            wrapperCount = count;
            return states.ToArray();

            void visit(DrawNode child)
            {
                if (readSource(child) is CompositeDrawable wrapper && wrappers.Contains(wrapper))
                    count++;
                if (BmsSpriteDrawNodeSnapshot.IsSprite(child))
                    states.Add((readSource(child), BmsSpriteDrawNodeSnapshot.ReadTexture(child), BmsSpriteDrawNodeSnapshot.ReadQuad(child), readColour(child)));
                if (child is ICompositeDrawNode { Children: { } children })
                    foreach (var nested in children)
                        visit(nested);
            }
        }
    }
}
