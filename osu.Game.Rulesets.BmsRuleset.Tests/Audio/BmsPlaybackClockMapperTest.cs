using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Mixing.Pcm;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Audio;

[TestFixture]
public class BmsPlaybackClockMapperTest
{
    [TestCase(0.5, 88200)]
    [TestCase(1.0, 44100)]
    [TestCase(2.0, 22050)]
    public void ChartTimeMapsThroughFixedRate(double rate, long expectedFrame)
    {
        var mapper = new BmsPlaybackClockMapper(rate);
        mapper.Rebase(1000, 500);

        Assert.That(mapper.Map(2000, 0), Is.EqualTo(500 + expectedFrame));
    }

    [Test]
    public void GeneratedPositionClampsLateSubmission()
    {
        var mapper = new BmsPlaybackClockMapper(1);
        mapper.Rebase(0, 0);

        Assert.That(mapper.Map(10, 1000), Is.EqualTo(1000));
    }

    [Test]
    public void RebaseAfterPauseDoesNotRetainPausedOutputOffset()
    {
        var mapper = new BmsPlaybackClockMapper(1);
        mapper.Rebase(1000, 44100);

        // The device keeps rendering silence while the chart clock is frozen.
        mapper.Rebase(1000, 88200);

        Assert.That(mapper.Map(1100, 88200), Is.EqualTo(92610));
    }

    [TestCase(0.5, 88200)]
    [TestCase(1.0, 44100)]
    [TestCase(2.0, 22050)]
    public void SourceOffsetUsesProcessedTimeline(double rate, long expectedFrame)
    {
        var mapper = new BmsPlaybackClockMapper(rate);

        Assert.That(mapper.MapSourceOffset(1000), Is.EqualTo(expectedFrame));
    }
}
