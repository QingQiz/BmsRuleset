using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public partial class BmsReplayFrameTest
{
    [Test]
    public void TestReplayFrameRoundTripsThroughJson([Values] BmsJudgementAlgorithm algorithm)
    {
        var original = new BmsReplayFrame(1234, BmsAction.Key1, BmsAction.Scratch)
        {
            BranchDecisions = "2:1",
            JudgementAlgorithm = algorithm,
        };

        var deserialised = original.Serialize().Deserialize<BmsReplayFrame>();

        Assert.That(deserialised.Time, Is.EqualTo(original.Time));
        Assert.That(deserialised.Actions, Is.EqualTo(original.Actions));
        Assert.That(deserialised.BranchDecisions, Is.EqualTo(original.BranchDecisions));
        Assert.That(deserialised.JudgementAlgorithm, Is.EqualTo(algorithm));
    }

    [Test]
    public void TestLegacyFrameHasNoAlgorithm()
    {
        var frame = """{"time": 1234, "actions": []}""".Deserialize<BmsReplayFrame>();

        Assert.That(frame.JudgementAlgorithm, Is.Null);
    }

    [Test]
    public void TestAlgorithmMetadataPreventsFrameDeduplication()
    {
        var legacy = new BmsReplayFrame(1000, BmsAction.Key1);
        var combo = new BmsReplayFrame(1000, BmsAction.Key1) { JudgementAlgorithm = BmsJudgementAlgorithm.Combo };
        var duration = new BmsReplayFrame(1000, BmsAction.Key1) { JudgementAlgorithm = BmsJudgementAlgorithm.Duration };

        Assert.That(combo.IsEquivalentTo(legacy), Is.False);
        Assert.That(combo.IsEquivalentTo(duration), Is.False);
    }

    [Test]
    public void TestRecorderStoresSessionAlgorithmInFirstFrame([Values] BmsJudgementAlgorithm algorithm)
    {
        using var recorder = new TestRecorder(new Score(), algorithm) { Clock = new FramedClock(new ManualClock()) };
        var first = recorder.CreateFrame([BmsAction.Key1], null);
        var second = recorder.CreateFrame([], first);

        Assert.That(first.JudgementAlgorithm, Is.EqualTo(algorithm));
        Assert.That(first.Actions, Is.EqualTo((BmsAction[])[BmsAction.Key1]));
        Assert.That(second.JudgementAlgorithm, Is.Null);
        Assert.That(second.Actions, Is.Empty);
    }

    private partial class TestRecorder(Score score, BmsJudgementAlgorithm algorithm) : BmsReplayRecorder(score, algorithm)
    {
        public BmsReplayFrame CreateFrame(List<BmsAction> actions, ReplayFrame previousFrame)
            => (BmsReplayFrame)HandleFrame(Vector2.Zero, actions, previousFrame);
    }
}
