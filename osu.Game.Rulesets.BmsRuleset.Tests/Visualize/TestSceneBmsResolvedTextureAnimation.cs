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
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsResolvedTextureAnimation : OsuTestScene
{
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(7)]
    public void FixedFramesMatchFrameworkAcrossPlaybackAndClockChanges(int frameCount)
    {
        var renderer = new DummyRenderer();
        var textures = Enumerable.Range(0, frameCount).Select(i => renderer.CreateTexture(20 + i * 10, 10 + i * 7)).ToArray();
        var actual = new BmsResolvedTextureAnimation(textures);
        var expected = new TextureAnimation();
        expected.AddFrames(textures);
        var manualClock = new ManualClock { CurrentTime = 2000, Rate = 1, IsRunning = true };
        var clock = new FramedClock(manualClock);
        Container actualHost = null!;
        Container expectedHost = null!;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var frame = 0UL;

        AddStep("load fixed animation paths", () =>
        {
            Clear();
            AddRange([
                actualHost = new Container { Size = new Vector2(100), Clock = clock, Child = actual },
                expectedHost = new Container { Size = new Vector2(100), Clock = clock, Child = expected },
            ]);
        });
        AddUntilStep("animations loaded", () => actual.IsLoaded && expected.IsLoaded);
        AddStep("compare playback, looping and rewind", () =>
        {
            compare();
            foreach (var delta in new[] { 0d, 10, 7, 15, 1, 20, 80, -13, -71, -100, 17 })
                advance(delta);
            for (var i = 0; i < 500; i++)
                advance(i % 29 == 0 ? -1.125 : .125);
        });
        AddStep("compare rehosting and clock changes", () =>
        {
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
            var expectedSprite = expected.ChildrenOfType<Sprite>().Single();
            Assert.That(actual.Texture, Is.SameAs(expectedSprite.Texture), $"clock={manualClock.CurrentTime}");
            Assert.That(actual.Size, Is.EqualTo(expected.Size));
            Assert.That(actual.DrawSize, Is.EqualTo(expected.DrawSize));
            Assert.That(actual.FillAspectRatio, Is.EqualTo(expected.FillAspectRatio));
            for (var slot = 0; slot < 3; slot++)
            {
                var actualNode = generate(actual, ++frame, slot, false)!;
                var expectedNode = generate(expectedSprite, ++frame, slot, false)!;
                Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(actualNode), Is.EqualTo(BmsSpriteDrawNodeSnapshot.ReadQuad(expectedNode)));
            }
        }
    }
}
