using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents.Bga;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public class BmsBgaVideoFactoryTest
{
    [TestCase("bga.mpg")]
    [TestCase("BGA.MPEG")]
    [TestCase("movie\\clip.mpg")]
    public void TestMpegExtensionsUseFallback(string path)
    {
        Assert.That(BmsBgaVideoFactory.UsesMpegFallback(path), Is.True);
    }

    [TestCase("bga.mp4")]
    [TestCase("bga.webm")]
    [TestCase("bga.avi")]
    public void TestFrameworkExtensionsDoNotUseFallback(string path)
    {
        Assert.That(BmsBgaVideoFactory.UsesMpegFallback(path), Is.False);
    }
}
