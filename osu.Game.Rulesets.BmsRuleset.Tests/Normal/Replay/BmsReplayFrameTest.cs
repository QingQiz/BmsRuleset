#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Replays.Legacy;
using osu.Game.IO.Serialization;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Replays.Types;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Replay;

[TestFixture]
public partial class BmsReplayFrameTest
{
    [Test]
    public void TestManiaLaneStatesAndSameTimeRelease()
    {
        var native = new Score { Replay = { Frames = [new LaneMaskFrame(10, 127), new LaneMaskFrame(20.5, 64), new LaneMaskFrame(20.5, 65), new LaneMaskFrame(30, 0)] } };
        var info = new ScoreInfo(ruleset: new BmsRuleset().RulesetInfo) { TotalScore = 950000 };
        var converted = BmsManiaReplay.Convert(native, info, new Beatmap())!;
        var frames = converted.Replay.Frames.Cast<BmsReplayFrame>().ToArray();
        Assert.That(frames.Select(frame => frame.Time), Is.EqualTo(new[] { 10, 20.5, 20.5, 30 }));
        Assert.That(frames[0].Actions, Is.EqualTo(new[] { BmsAction.Key1, BmsAction.Key2, BmsAction.Key3, BmsAction.Key4, BmsAction.Key5, BmsAction.Key6, BmsAction.Key7 }));
        Assert.That(frames[1].Actions, Is.EqualTo(new[] { BmsAction.Key7 }));
        Assert.That(frames[2].Actions, Is.EqualTo(new[] { BmsAction.Key1, BmsAction.Key7 }));
        Assert.That(frames[3].Actions, Is.Empty);
        Assert.That(converted.ScoreInfo, Is.Not.SameAs(info));
        Assert.That(converted.ScoreInfo.TotalScore, Is.EqualTo(info.TotalScore));
        Assert.That(native.Replay.Frames, Has.All.TypeOf<LaneMaskFrame>());
    }

    private sealed class LaneMaskFrame(double time, int mask) : ReplayFrame(time), IConvertibleReplayFrame
    {
        public LegacyReplayFrame ToLegacy(IBeatmap beatmap) => new(Time, mask, null, ReplayButtonState.None);

        public void FromLegacy(LegacyReplayFrame currentFrame, IBeatmap beatmap, ReplayFrame? lastFrame = null) => throw new System.NotSupportedException();
    }

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
