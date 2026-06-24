using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteSegmentComposerTest
{

    [Test]
    public void TestMultiSliceAlwaysIncludesZeroOnce()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20, 30], 5, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void TestMultiSliceRepeatsOnlyNonZeroSlices()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20, 30], 95, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo(new[] { 0, 1, 2, 1, 2 }));
    }

    [Test]
    public void TestMultiSliceSelectsMinimumSequentialSlices()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20, 30], 25, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void TestSingleSliceRepeatsZeroWhenInsufficient()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10], 25, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(parts.Select(p => p.Height), Is.EqualTo(new[] { 10, 10, 10 }));
    }

    [Test]
    public void TestTailAtBottomPositionsFromBottomAndFlipsAllParts()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20], 25, false);

        Assert.That(parts.Select(p => p.Y), Is.EqualTo(new[] { 25, 15 }));
        Assert.That(parts[0].FlipY, Is.True);
        Assert.That(parts[1].FlipY, Is.True);
    }

    [Test]
    public void TestTailAtTopPositionsFromTop()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20], 25, true);

        Assert.That(parts.Select(p => p.Y), Is.EqualTo(new[] { 0, 10 }));
        Assert.That(parts.Any(p => p.FlipY), Is.False);
    }
}
