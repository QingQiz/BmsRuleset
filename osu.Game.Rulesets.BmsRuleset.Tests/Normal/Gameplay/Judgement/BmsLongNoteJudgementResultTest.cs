using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsLongNoteJudgementResultTest
{
    [Test]
    public void TestEndpointDerivesExpectedTimeAndOffset()
    {
        var source = createLongNote();
        var head = new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1.5, HitResult.Great);
        var tail = new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Tail, 1490, 1.5, HitResult.Great);

        Assert.Multiple(() =>
        {
            Assert.That(head.ExpectedTime, Is.EqualTo(1000));
            Assert.That(head.TimeOffset, Is.EqualTo(12));
            Assert.That(head.GameplayRate, Is.EqualTo(1.5));
            Assert.That(tail.ExpectedTime, Is.EqualTo(1500));
            Assert.That(tail.TimeOffset, Is.EqualTo(-10));
        });
    }

    [Test]
    public void TestAcceptsHeadAndTailFromSameSource()
    {
        var source = createLongNote();
        var endpoints = new[]
        {
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1, HitResult.Great),
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Tail, 1490, 1, HitResult.Great),
        };

        var result = new BmsLongNoteJudgementResult(source, source.CreateJudgement(), endpoints);

        Assert.That(result.EndpointResults, Is.EqualTo(endpoints));
    }

    [Test]
    public void TestRejectsDuplicateEndpointKinds()
    {
        var source = createLongNote();
        var endpoints = new[]
        {
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1, HitResult.Great),
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1015, 1, HitResult.Great),
        };

        Assert.Throws<System.ArgumentException>(() => new BmsLongNoteJudgementResult(source, source.CreateJudgement(), endpoints));
    }

    [Test]
    public void TestRejectsMixedSources()
    {
        var source = createLongNote();
        var other = createLongNote();
        var endpoints = new[]
        {
            new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1, HitResult.Great),
            new BmsLongNoteEndpointResult(other, BmsLongNoteEndpointKind.Tail, 1490, 1, HitResult.Great),
        };

        Assert.Throws<System.ArgumentException>(() => new BmsLongNoteJudgementResult(source, source.CreateJudgement(), endpoints));
    }

    [Test]
    public void TestDrawableResultAcceptsEndpointsOnlyOnce()
    {
        var source = createLongNote();
        var endpoint = new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1, HitResult.Great);
        var result = new BmsLongNoteJudgementResult(source, source.CreateJudgement());

        result.SetEndpointResults([endpoint]);
        result.Type = HitResult.Great;

        Assert.Multiple(() =>
        {
            Assert.That(result.EndpointResults, Is.EqualTo(new[] { endpoint }));
            Assert.Throws<System.InvalidOperationException>(() => result.SetEndpointResults([endpoint]));
        });
    }

    [Test]
    public void TestDrawableResultAcceptsReplayedEndpointsAfterFrameworkReset()
    {
        var source = createLongNote();
        var first = new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1012, 1, HitResult.Great);
        var replayed = new BmsLongNoteEndpointResult(source, BmsLongNoteEndpointKind.Head, 1017, 1, HitResult.Great);
        var result = new BmsLongNoteJudgementResult(source, source.CreateJudgement());
        result.SetEndpointResults([first]);
        result.Type = HitResult.Great;

        result.Type = HitResult.None;
        result.SetEndpointResults([replayed]);

        Assert.That(result.EndpointResults, Is.EqualTo(new[] { replayed }));
    }

    private static BmsLongNote createLongNote() => new()
    {
        StartTime = 1000,
        Duration = 500,
    };
}
