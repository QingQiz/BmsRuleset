using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Audio.Resources;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsAudioFormatSupportTest
{
    [Test]
    public void ExtensionsFollowFallbackOrder()
    {
        string[] expected = ["wav", "flac", "ogg", "mp3"];
        Assert.That(BmsAudioFormatSupport.Extensions, Is.EqualTo(expected));
    }
}
