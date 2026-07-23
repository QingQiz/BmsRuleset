using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsAudioResourceStoreTest
{
    [Test]
    public void ExtensionsFollowFallbackOrder()
    {
        string[] expected = ["wav", "flac", "ogg", "mp3"];
        Assert.That(BmsAudioResourceStore.Extensions, Is.EqualTo(expected));
    }
}
