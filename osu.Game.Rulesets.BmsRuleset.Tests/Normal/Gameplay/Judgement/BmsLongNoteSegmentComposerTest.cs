using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteSegmentComposerTest
{

    [TestCase(false)]
    [TestCase(true)]
    public void TestLargerBodyCanBeTailAlignedAndClipped(bool tailAtTop)
    {
        const float visible_height = 25;
        const float content_height = 95;

        var direct = visibleGeometry(BmsLongNoteSegmentComposer.Compose([10, 20, 30], visible_height, tailAtTop), 0, visible_height);
        var contentOffset = tailAtTop ? 0 : visible_height - content_height;
        var clipped = visibleGeometry(BmsLongNoteSegmentComposer.Compose([10, 20, 30], content_height, tailAtTop), contentOffset, visible_height);

        Assert.That(clipped, Is.EqualTo(direct));
    }

    private static (int SegmentIndex, float Top, float Bottom, bool FlipY)[] visibleGeometry(
        IReadOnlyList<BmsLongNoteSegmentComposer.Part> parts, float contentOffset, float maskHeight)
    {
        return
        [
            .. parts.Select(part =>
                {
                    var partTop = contentOffset + (part.FlipY ? part.Y - part.Height : part.Y);
                    var partBottom = contentOffset + (part.FlipY ? part.Y : part.Y + part.Height);
                    return (part.SegmentIndex, Top: Math.Max(0, partTop), Bottom: Math.Min(maskHeight, partBottom), part.FlipY);
                })
                .Where(part => part.Bottom > part.Top)
        ];
    }

    [Test]
    public void TestMultiSliceAlwaysIncludesZeroOnce()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20, 30], 5, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo([0]));
    }

    [Test]
    public void TestMultiSliceRepeatsOnlyNonZeroSlices()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20, 30], 95, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo([0, 1, 2, 1, 2]));
    }

    [Test]
    public void TestMultiSliceSelectsMinimumSequentialSlices()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20, 30], 25, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo([0, 1]));
    }

    [Test]
    public void TestSingleSliceRepeatsZeroWhenInsufficient()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10], 25, false);

        Assert.That(parts.Select(p => p.SegmentIndex), Is.EqualTo([0, 0, 0]));
        Assert.That(parts.Select(p => p.Height), Is.EqualTo([10, 10, 10]));
    }

    [Test]
    public void TestTailAtBottomPositionsFromBottomAndFlipsAllParts()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20], 25, false);

        Assert.That(parts.Select(p => p.Y), Is.EqualTo([25, 15]));
        Assert.That(parts[0].FlipY, Is.True);
        Assert.That(parts[1].FlipY, Is.True);
    }

    [Test]
    public void TestTailAtTopPositionsFromTop()
    {
        var parts = BmsLongNoteSegmentComposer.Compose([10, 20], 25, true);

        Assert.That(parts.Select(p => p.Y), Is.EqualTo([0, 10]));
        Assert.That(parts.Any(p => p.FlipY), Is.False);
    }
}
