#nullable enable
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public partial class BmsReplayFrameTest
{
    [Test]
    public void TestReplayFrameContainsOnlyTimeAndActions()
    {
        var original = new BmsReplayFrame(1234.125, BmsAction.Key1, BmsAction.ScratchReverse);
        var deserialised = original.Serialize().Deserialize<BmsReplayFrame>();
        Assert.That(deserialised.Time, Is.EqualTo(original.Time));
        Assert.That(deserialised.Actions, Is.EqualTo(original.Actions));
    }

    [Test]
    public void TestRecorderStoresAlgorithmOnFirstInput([Values] BmsJudgementAlgorithm algorithm)
    {
        var score = new Score();
        using var recorder = new TestRecorder(score, algorithm) { Clock = new FramedClock(new ManualClock()) };
        var first = recorder.CreateFrame([BmsAction.Key1], null)!;
        var second = recorder.CreateFrame([], first)!;
        Assert.That(first.Algorithm, Is.EqualTo(algorithm));
        Assert.That(second.Algorithm, Is.EqualTo(BmsJudgementAlgorithm.Combo));
        Assert.That(score.ScoreInfo.Mods, Is.Empty);
        Assert.That(first.Actions, Is.EqualTo((BmsAction[])[BmsAction.Key1]));
        Assert.That(second.Actions, Is.Empty);
    }

    [Test]
    public void TestRecorderOmitsIdleSamplesAndPreservesSameTimeChanges()
    {
        var source = new ManualClock();
        var clock = new FramedClock(source);
        using var recorder = new TestRecorder(new Score(), BmsJudgementAlgorithm.Combo) { Clock = clock };
        var initial = recorder.CreateFrame([], null)!;
        source.CurrentTime = 1000.125;
        clock.ProcessFrame();
        Assert.That(recorder.CreateFrame([], initial), Is.Null);
        var press = recorder.CreateFrame([BmsAction.Key1], initial)!;
        Assert.That(press.Time, Is.EqualTo(1000.125));
        Assert.That(recorder.CreateFrame([BmsAction.Key1], press), Is.Null);
        var release = recorder.CreateFrame([], press)!;
        var repress = recorder.CreateFrame([BmsAction.Key1], release)!;
        Assert.That(release.Time, Is.EqualTo(press.Time));
        Assert.That(repress.Time, Is.EqualTo(press.Time));
        source.CurrentTime = 500;
        clock.ProcessFrame();
        Assert.That(recorder.CreateFrame([BmsAction.Key1], repress), Is.Null);
    }

    [Test]
    public void TestActionOrderChangesDoNotCreateAnOperation()
    {
        using var recorder = new TestRecorder(new Score(), BmsJudgementAlgorithm.Combo) { Clock = new FramedClock(new ManualClock()) };
        var previous = recorder.CreateFrame([BmsAction.Key1, BmsAction.Key2], null)!;
        Assert.That(recorder.CreateFrame([BmsAction.Key2, BmsAction.Key1], previous), Is.Null);
    }

    private partial class TestRecorder(Score score, BmsJudgementAlgorithm algorithm) : BmsReplayRecorder(score, algorithm)
    {
        public BmsReplayFrame? CreateFrame(List<BmsAction> actions, ReplayFrame? previousFrame)
            => (BmsReplayFrame?)HandleFrame(Vector2.Zero, actions, previousFrame);
    }
}
