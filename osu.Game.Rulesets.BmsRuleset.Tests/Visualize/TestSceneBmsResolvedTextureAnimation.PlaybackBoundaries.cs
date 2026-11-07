#nullable enable

using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsResolvedTextureAnimation
{
    [Test]
    public void FixedFramesAdvanceAndWrapAtSixtyFpsBoundaries()
    {
        var renderer = new DummyRenderer();
        var textures = new[] { renderer.CreateTexture(20, 10), renderer.CreateTexture(40, 30) };
        var animation = new BmsResolvedTextureAnimation(textures);
        var manual = new ManualClock();
        var clock = new FramedClock(manual);
        Container host = null!;
        AddStep("load fixed frame boundaries", () =>
        {
            Clear();
            Add(host = new Container { Size = new Vector2(100), Clock = clock, Child = animation });
        });
        AddUntilStep("animation loaded", () => animation.IsLoaded);
        AddStep("advance across frame and loop boundaries", () =>
        {
            foreach (var (time, index) in new[] { (0d, 0), (16.666, 0), (1000 / 60d, 1), (33.333, 1), (2000 / 60d, 0), (50.001, 1), (10d, 0), (-10d, 0) })
            {
                manual.CurrentTime = time;
                clock.ProcessFrame();
                host.UpdateSubTree();
                Assert.That(animation.Texture, Is.SameAs(textures[index]), $"at {time:R}");
            }
        });
    }
}
