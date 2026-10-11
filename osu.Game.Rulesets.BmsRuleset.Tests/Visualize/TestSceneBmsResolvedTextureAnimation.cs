#nullable enable

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsResolvedTextureAnimation : OsuTestScene
{
    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, true)]
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    public void TestClockAndFramesMatchFramework(bool looping, bool seekBeforeLoad, bool zeroLengthFirstFrame)
    {
        var renderer = new DummyRenderer();
        var actual = new BmsResolvedTextureAnimation([]) { Loop = looping };
        var expected = new TextureAnimation { Loop = looping };
        var manualClock = new ManualClock { CurrentTime = 2000, Rate = 1, IsRunning = true };
        var clock = new FramedClock(manualClock);
        Container actualHost = null!;
        Container expectedHost = null!;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var frame = 0UL;

        addFrame(20, 10, zeroLengthFirstFrame ? 0 : 10);
        addFrame(40, 30, 25);
        addFrame(10, 20, 5);
        if (seekBeforeLoad)
        {
            actual.Seek(16);
            expected.Seek(16);
        }

        AddStep("load independent animation paths", () =>
        {
            Clear();
            AddRange([
                actualHost = new Container { Size = new Vector2(100), Clock = clock, Child = actual },
                expectedHost = new Container { Size = new Vector2(100), Clock = clock, Child = expected },
            ]);
        });
        AddUntilStep("animations loaded", () => actual.IsLoaded && expected.IsLoaded);
        AddStep("compare clock boundaries, rewind, pause and seek", () =>
        {
            compare();
            foreach (var delta in new double[] { 0, 9.999, 0.001, 24.999, 0.001, 5, 80, -13, -71, -100 })
                advance(delta);

            actual.IsPlaying = expected.IsPlaying = false;
            advance(71);
            advance(-121);
            foreach (var time in new double[] { 5, 0, -1, 40, 80, 39.999 })
            {
                actual.Seek(time);
                expected.Seek(time);
                compare();
            }

            foreach (var index in new int[] { 0, 2, 1, -1 })
            {
                actual.GotoFrame(index);
                expected.GotoFrame(index);
                compare();
            }
            actual.IsPlaying = expected.IsPlaying = true;
            advance(17);

            actualHost.Remove(actual, false);
            expectedHost.Remove(expected, false);
            manualClock = new ManualClock { CurrentTime = 30000, Rate = 1, IsRunning = true };
            clock = new FramedClock(manualClock);
            AddRange([
                actualHost = new Container { Size = new Vector2(100), Clock = clock, Child = actual },
                expectedHost = new Container { Size = new Vector2(100), Clock = clock, Child = expected },
            ]);
            compare();
            advance(10);

            manualClock = new ManualClock { CurrentTime = 10000, Rate = 1, IsRunning = true };
            clock = new FramedClock(manualClock);
            actual.Clock = expected.Clock = clock;
            compare();
            advance(7.5);
            actual.Clock = actualHost.Clock;
            expected.Clock = expectedHost.Clock;
            compare();
        });
        AddStep("compare custom axes and frame mutation", () =>
        {
            actual.IsPlaying = expected.IsPlaying = false;
            actual.Seek(0);
            expected.Seek(0);
            compare();
            actual.Width = expected.Width = 23;
            compare();
            actual.GotoFrame(1);
            expected.GotoFrame(1);
            compare();
            actual.Height = expected.Height = 31;
            compare();
            actual.Size = expected.Size = new Vector2(0.3f, 0.5f);
            foreach (var axes in new[] { Axes.X, Axes.Y, Axes.Both, Axes.None })
            {
                actual.RelativeSizeAxes = expected.RelativeSizeAxes = axes;
                compare();
            }
            actual.ClearFrames();
            expected.ClearFrames();
            compare();
            addFrame(10, 20, 17);
            compare();
            addFrame(200, 30, 31);
            actual.Seek(18);
            expected.Seek(18);
            compare();
        });

        void addFrame(int width, int height, double duration)
        {
            actual.AddFrame(renderer.CreateTexture(width, height), duration);
            expected.AddFrame(renderer.CreateTexture(width, height), duration);
        }

        void advance(double delta)
        {
            manualClock.CurrentTime += delta;
            clock.ProcessFrame();
            compare();
        }

        void compare()
        {
            actualHost.UpdateSubTree();
            expectedHost.UpdateSubTree();
            var context = $"clock={manualClock.CurrentTime}, loop={looping}, seekBeforeLoad={seekBeforeLoad}, zeroLengthFirstFrame={zeroLengthFirstFrame}";
            Assert.That(actual.PlaybackPosition, Is.EqualTo(expected.PlaybackPosition).Within(1e-9), context);
            Assert.That(actual.FrameCount, Is.EqualTo(expected.FrameCount), context);
            Assert.That(actual.Duration, Is.EqualTo(expected.Duration), context);
            Assert.That(actual.CurrentFrameIndex, Is.EqualTo(expected.CurrentFrameIndex), context);
            Assert.That(actual.Size, Is.EqualTo(expected.Size), context);
            Assert.That(actual.DrawSize, Is.EqualTo(expected.DrawSize), context);
            Assert.That(actual.FillAspectRatio, Is.EqualTo(expected.FillAspectRatio), context);
            var expectedSprite = expected.ChildrenOfType<Sprite>().Single();
            Assert.That(actual.Texture?.DisplaySize, Is.EqualTo(expectedSprite.Texture?.DisplaySize), context);
            if (actual.Texture == null)
                return;

            for (var slot = 0; slot < 3; slot++)
            {
                var actualNode = generate(actual, ++frame, slot, false)!;
                var expectedNode = generate(expectedSprite, ++frame, slot, false)!;
                Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(actualNode), Is.EqualTo(BmsSpriteDrawNodeSnapshot.ReadQuad(expectedNode)), context);
                Assert.That(BmsSpriteDrawNodeSnapshot.ReadTexture(actualNode)?.DisplaySize,
                    Is.EqualTo(BmsSpriteDrawNodeSnapshot.ReadTexture(expectedNode)?.DisplaySize), context);
            }
        }
    }
}
