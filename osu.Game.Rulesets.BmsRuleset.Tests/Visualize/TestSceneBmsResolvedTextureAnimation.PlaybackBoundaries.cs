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
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResolvedTextureAnimation
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void PlaybackMatchesFrameworkNearBoundariesAndAfterMutation(bool zeroLengthFirstFrame, bool fractionalFrameLengths)
    {
        var renderer = new DummyRenderer();
        var actual = new BmsResolvedTextureAnimation([]);
        var expected = new TextureAnimation();
        var sourceClock = new ManualClock { CurrentTime = 2000, IsRunning = true };
        var clock = new FramedClock(sourceClock);
        Container actualHost = null!;
        Container expectedHost = null!;
        var frame = 0UL;
        var comparisons = 0;
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        var firstLength = zeroLengthFirstFrame ? 0 : fractionalFrameLengths ? 1000 / 60d : 10;
        var secondLength = fractionalFrameLengths ? .1 : 25;
        var thirdLength = fractionalFrameLengths ? 1 / 3d : 5;
        addFrame(40, 20, firstLength);
        addFrame(20, 40, secondLength);
        addFrame(30, 15, thirdLength);

        AddStep("load independent frame selectors", () =>
        {
            Clear();
            AddRange([
                actualHost = new Container { Size = new Vector2(100), Clock = clock, Child = actual },
                expectedHost = new Container { Size = new Vector2(100), Clock = clock, Child = expected },
            ]);
        });
        AddUntilStep("both selectors loaded", () => actual.IsLoaded && expected.IsLoaded);
        AddStep("compare accumulated clock and rewinds", () =>
        {
            actual.Seek(0);
            expected.Seek(0);
            compare();
            for (var i = 0; i < 5000; i++)
            {
                sourceClock.CurrentTime += i % 97 == 0 ? -1.125 : .125;
                clock.ProcessFrame();
                compare();
            }
            foreach (var position in new[] { -100d, 0d, 10d, 35d, 40d, 1e6, 1e12, Math.Pow(2, 52) })
            {
                seek(position);
                foreach (var delta in new[] { .001, .125, 1d, 16.6666666667, 33.3333333333, -80d })
                {
                    sourceClock.CurrentTime += delta;
                    clock.ProcessFrame();
                    compare();
                }
            }
        });
        AddStep("compare translated boundaries, loop changes and appended frames", () =>
        {
            actual.IsPlaying = expected.IsPlaying = false;
            foreach (var looping in new[] { true, false, true })
            {
                actual.Loop = expected.Loop = looping;
                foreach (var cycle in new[] { 0d, 1d, 13d, 1e6, 1e12 })
                {
                    foreach (var boundary in new[] { 0d, firstLength, firstLength + secondLength, actual.Duration })
                    {
                        var value = cycle * actual.Duration + boundary;
                        seek(Math.BitDecrement(value));
                        seek(value);
                        seek(Math.BitIncrement(value));
                    }
                }
            }
            seek(17);
            addFrame(50, 10, 7);
            compare();
            seek(41);
            actual.ClearFrames();
            expected.ClearFrames();
            compare();
            addFrame(10, 20, 17);
            addFrame(30, 10, 31);
            foreach (var value in new[] { -1d, 0, 16.999, 17, 47.999, 48, 99 }) seek(value);
            TestContext.WriteLine($"PLAYBACK_BOUNDARY_REFERENCE comparisons={comparisons} buffers=3");
        });

        void addFrame(int width, int height, double duration)
        {
            var texture = renderer.CreateTexture(width, height);
            actual.AddFrame(texture, duration);
            expected.AddFrame(texture, duration);
        }

        void seek(double position)
        {
            actual.Seek(position);
            expected.Seek(position);
            compare();
        }

        void compare()
        {
            actualHost.UpdateSubTree();
            expectedHost.UpdateSubTree();
            Assert.That(actual.PlaybackPosition, Is.EqualTo(expected.PlaybackPosition));
            Assert.That(actual.CurrentFrameIndex, Is.EqualTo(expected.CurrentFrameIndex));
            Assert.That(actual.Size, Is.EqualTo(expected.Size));
            Assert.That(actual.FillAspectRatio, Is.EqualTo(expected.FillAspectRatio));
            var referenceSprite = expected.ChildrenOfType<Sprite>().Single();
            Assert.That(actual.Texture, Is.SameAs(referenceSprite.Texture));
            if (actual.Texture != null)
            {
                for (var slot = 0; slot < 3; slot++)
                {
                    var actualNode = generate(actual, ++frame, slot, true)!;
                    var expectedNode = generate(referenceSprite, ++frame, slot, true)!;
                    Assert.That(BmsSpriteDrawNodeSnapshot.ReadQuad(actualNode), Is.EqualTo(BmsSpriteDrawNodeSnapshot.ReadQuad(expectedNode)));
                }
            }
            comparisons++;
        }
    }
}
