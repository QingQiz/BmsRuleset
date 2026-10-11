#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Sprites;
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

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsHitLightAnimationLifecycle : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)], BmsTestSkins.SkinKind.Legacy);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = [new BmsNote { StartTime = 60000, Column = 1 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void AnimatedHitLightsReloadReuseAndDisposeWithTheirOwners()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var isDisposed = typeof(Drawable).GetProperty("IsDisposed", flags)!.GetMethod!.CreateDelegate<Func<Drawable, bool>>();
        var reload = typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!;
        BmsHitExplosion pulse = null!;
        Drawable oldAnimation = null!;
        Drawable finalAnimation = null!;
        BmsColumn column = null!;
        AddStep("load legacy hit light player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(1000);
        AddStep("trigger animated pulse", () =>
        {
            column = (BmsColumn)Playfield.Stage.Columns[1];
            column.TriggerHitExplosion(BmsHitLightStyle.Note);
        });
        AddUntilStep("animated pulse loaded", () =>
        {
            pulse = column.HitExplosionArea.AliveChildren.OfType<BmsHitExplosion>().SingleOrDefault()!;
            return pulse != null && animation().IsLoaded;
        });
        AddStep("check first pulse frames", checkFrames);
        AddStep("reload active hit light", () =>
        {
            oldAnimation = animation();
            reload.Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("active animation replaced", () => animation().IsLoaded && !ReferenceEquals(animation(), oldAnimation));
        AddStep("check active replacement", () => { Assert.That(isDisposed(oldAnimation), Is.True); checkFrames(); });
        seek(1300);
        AddUntilStep("pulse returned to pool", () => !pulse.IsInUse);
        AddStep("reload idle hit light", () =>
        {
            oldAnimation = animation();
            reload.Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        // Hit-light wrappers refresh pending skins when their pooled owner is used again.
        AddStep("reuse pulse", () => column.TriggerHitExplosion(BmsHitLightStyle.Note));
        AddUntilStep("same owner has refreshed animation", () => pulse.IsInUse && animation().IsLoaded && !ReferenceEquals(animation(), oldAnimation));
        AddStep("check reused frames", () => { Assert.That(isDisposed(oldAnimation), Is.True); checkFrames(); });
        AddStep("unload hit light player", () => { finalAnimation = animation(); Stack.Exit(); });
        AddUntilStep("pooled owner released", () => isDisposed(pulse));
        AddAssert("animation released", () => isDisposed(finalAnimation));

        Drawable animation() => pulse.ChildrenOfType<LegacyBmsHitExplosion>().Single().ChildrenOfType<Drawable>()
            .Single(drawable => drawable is IFramedAnimation);

        void checkFrames()
        {
            var drawable = animation();
            var framed = (IFramedAnimation)drawable;
            Assert.That(framed.FrameCount, Is.EqualTo(3));
            for (var index = 0; index < framed.FrameCount; index++)
            {
                framed.GotoAndStop(index);
                pulse.UpdateSubTree();
                Assert.That(framed.CurrentFrameIndex, Is.EqualTo(index));
                Assert.That((drawable as Sprite ?? drawable.ChildrenOfType<Sprite>().Single()).Texture, Is.Not.Null);
            }
        }
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("simulation reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }
}
