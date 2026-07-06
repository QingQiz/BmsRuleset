using NUnit.Framework;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.BmsRuleset.Replays;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public class BmsReplayFrameTest
{
    [Test]
    public void TestReplayFrameRoundTripsThroughJson()
    {
        var original = new BmsReplayFrame(1234, BmsAction.Key1, BmsAction.Scratch)
        {
            BranchDecisions = "2:1",
        };

        var deserialised = original.Serialize().Deserialize<BmsReplayFrame>();

        Assert.That(deserialised.Time, Is.EqualTo(original.Time));
        Assert.That(deserialised.Actions, Is.EqualTo(original.Actions));
        Assert.That(deserialised.BranchDecisions, Is.EqualTo(original.BranchDecisions));
    }
}
