#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsGameplayDiagnosticFrameBufferTest
{
    [Test]
    public void RecordsPreserveEveryFieldAcrossChunkBoundaries()
    {
        using var buffer = new BmsGameplayDiagnosticGame.FrameBuffer();
        var expected = Enumerable.Range(0, 40001).Select(i => new BmsGameplayDiagnosticGame.Frame(
            i, i - .5, i * .01, .1, .2, i * 128, i % 100, i % 2 == 0, i % 3 == 0)).ToArray();
        foreach (var frame in expected) buffer.Add(frame);
        Assert.That(buffer.Count, Is.EqualTo(expected.Length));
        Assert.That(buffer, Is.EqualTo(expected));
    }

    [Test]
    public void DisposalIsIdempotentAndRejectsWrites()
    {
        var buffer = new BmsGameplayDiagnosticGame.FrameBuffer();
        buffer.Add(default);
        buffer.Dispose();
        buffer.Dispose();
        Assert.That(buffer.Count, Is.Zero);
        Assert.Throws<ObjectDisposedException>(() => buffer.Add(default));
    }
}
