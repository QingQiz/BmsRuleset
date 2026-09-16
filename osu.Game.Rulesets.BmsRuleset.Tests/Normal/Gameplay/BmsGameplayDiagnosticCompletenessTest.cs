using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public class BmsGameplayDiagnosticCompletenessTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void AcceptsCombinedAndSyntheticEndpoints(bool separateTail)
    {
        var ln = new BmsLongNote { StartTime = 1000, Duration = 500 };
        var head = new BmsLongNoteEndpointResult(ln, BmsLongNoteEndpointKind.Head, 1000, 1, HitResult.Perfect);
        var tail = new BmsLongNoteEndpointResult(ln, BmsLongNoteEndpointKind.Tail, 1500, 1, HitResult.Perfect);
        var parent = new BmsLongNoteJudgementResult(ln, ln.CreateJudgement(), separateTail ? [head] : [head, tail]);
        var synthetic = ln.CreateSyntheticEndpoint(ln.EndTime);
        var tailResult = new BmsLongNoteJudgementResult(synthetic, synthetic.CreateJudgement(), [tail]);

        Assert.That(BmsGameplayDiagnosticScene.CheckJudgementCompleteness([ln], separateTail ? [parent, tailResult] : [parent]), Is.Null);
    }

    [Test]
    public void RejectsHeadOnlyEvenWhenSourceWasJudged()
    {
        var ln = new BmsLongNote { StartTime = 1000, Duration = 500 };
        var head = new BmsLongNoteJudgementResult(ln, ln.CreateJudgement(),
            [new BmsLongNoteEndpointResult(ln, BmsLongNoteEndpointKind.Head, 1000, 1, HitResult.Perfect)]);
        Assert.That(BmsGameplayDiagnosticScene.CheckJudgementCompleteness([ln], [head]), Does.Contain("both endpoints"));
    }
}
