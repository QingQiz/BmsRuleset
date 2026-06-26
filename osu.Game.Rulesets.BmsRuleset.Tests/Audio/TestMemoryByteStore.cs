using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Audio;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class TestMemoryByteStore
{

    [Test]
    public void Get_MissingReturnsNull()
    {
        var store = new MemoryByteStore(new Dictionary<string, byte[]>());

#pragma warning disable CS0472
        Assert.That(store.Get("missing.wav") == null, Is.True);
#pragma warning restore CS0472
    }

    [Test]
    public void Get_ReturnsInsertedBytes()
    {
        var dict = new Dictionary<string, byte[]>();
        var store = new MemoryByteStore(dict);
        var data = new byte[] { 1, 2, 3 };
        dict["x.wav"] = data;

        Assert.That(store.Get("x.wav"), Is.SameAs(data));
    }
}
