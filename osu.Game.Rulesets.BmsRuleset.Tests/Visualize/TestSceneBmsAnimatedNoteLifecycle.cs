#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsAnimatedNoteLifecycle : BmsPlayerTestScene
{
    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)], BmsTestSkins.SkinKind.Legacy);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            HitObjects =
            [
                new BmsNote { StartTime = 5000, Column = 1 },
                new BmsNote { StartTime = 5000, Column = 6 },
                new BmsLongNote { StartTime = 5000, Duration = 2000, Column = 7 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(1f)]
    [TestCase(1.37f)]
    public void TestAnimatedNotesAndTailReloadReuseAndDispose(float height)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var isDisposed = typeof(Drawable).GetProperty("IsDisposed", flags)!.GetMethod!
            .CreateDelegate<Func<Drawable, bool>>();
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var reload = typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", flags)!;
        DrawableBmsHitObject[] notes = [];
        Drawable[] previousAnimations = [];
        Drawable[] finalAnimations = [];
        var frame = 0UL;

        AddStep("load animated note player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("freeze before animated notes", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
            Playfield.Stage.SetNoteHeightScale(height);
        });
        AddUntilStep("animated note and tail resources loaded", () =>
        {
            if (Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) >= .001)
                return false;
            notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().ToArray();
            return notes.Length == 3 && animations().Length == 3 && animations().All(a => a.IsLoaded);
        });
        AddStep("check active animation frames and buffers", checkFrames);
        AddStep("reload active animated skin", () =>
        {
            previousAnimations = animations();
            reload.Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("active resources replaced", () => animations().Length == 3
            && animations().All(a => a.IsLoaded && !previousAnimations.Contains(a)));
        AddStep("old active resources released", () =>
        {
            Assert.That(previousAnimations.All(isDisposed), Is.True);
            Playfield.Stage.SetNoteHeightScale(height);
            checkFrames();
        });
        AddStep("return animated representations to pool", () => Player.GameplayClockContainer.Seek(12000));
        AddUntilStep("representations idle", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 12000) < .001
            && notes.All(n => !n.IsInUse));
        AddStep("reload idle animated skin", () =>
        {
            previousAnimations = animations();
            reload.Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null);
        });
        AddUntilStep("idle resources preloaded", () => animations().Length == 3
            && animations().All(a => a.LoadState >= LoadState.Ready && !previousAnimations.Contains(a)));
        AddStep("rewind to reuse original objects", () =>
        {
            Playfield.Stage.SetNoteHeightScale(height);
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("original objects reused", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001
            && notes.All(n => n.IsInUse && n.IsPresent) && animations().All(a => a.IsLoaded));
        AddStep("check reused animation frames and buffers", () =>
        {
            Assert.That(previousAnimations.All(isDisposed), Is.True);
            checkFrames();
        });
        AddStep("unload player", () =>
        {
            finalAnimations = animations();
            Stack.Exit();
        });
        AddUntilStep("pooled owners released", () => notes.All(isDisposed));
        AddStep("animated children released", () => Assert.That(finalAnimations.All(isDisposed), Is.True));

        Drawable[] animations() => notes.SelectMany(n => n.ChildrenOfType<BmsResolvedNotePiece>())
            .SelectMany(p => p.ChildrenOfType<Drawable>()).Where(d => d is BmsResolvedTextureAnimation).ToArray();

        void checkFrames()
        {
            var children = animations();
            Assert.That(children, Has.Length.EqualTo(3));
            foreach (var child in children)
            {
                var manual = new ManualClock();
                var clock = new FramedClock(manual);
                var previousClock = child.Clock;
                child.Clock = clock;
                var frames = new HashSet<Texture>();
                for (var index = 0; index < 4; index++)
                {
                    manual.CurrentTime += 17;
                    clock.ProcessFrame();
                    // The parent scales using the prior frame; check after both sides of
                    // that existing update ordering have consumed the new frame.
                    for (var update = 0; update < 3; update++)
                        foreach (var note in notes) note.UpdateSubTree();
                    var sprite = (Sprite)child;
                    Assert.That(sprite.Texture, Is.Not.Null);
                    frames.Add(sprite.Texture);
                    for (var slot = 0; slot < 3; slot++)
                    {
                        var node = generate(sprite, ++frame, slot, false)!;
                        Assert.That(BmsSpriteDrawNodeSnapshot.ReadTexture(node), Is.SameAs(sprite.Texture));
                        var quad = BmsSpriteDrawNodeSnapshot.ReadQuad(node);
                        float[] coordinates = [quad.TopLeft.X, quad.TopLeft.Y, quad.TopRight.X, quad.TopRight.Y,
                            quad.BottomLeft.X, quad.BottomLeft.Y, quad.BottomRight.X, quad.BottomRight.Y];
                        Assert.That(coordinates.All(float.IsFinite), Is.True);
                    }
                }
                child.Clock = previousClock;
                Assert.That(frames, Has.Count.EqualTo(2));
            }
        }
    }
}
