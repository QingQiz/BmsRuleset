using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsHitErrorMeterTest
{
    [Test]
    public void TestAsymmetricWindowsUseSymmetricDomain()
    {
        var domain = BmsHitErrorMeter.CreateDomain(BmsLayoutVariant.Bme7K, 0.75);

        Assert.Multiple(() =>
        {
            Assert.That(domain.FastOffset, Is.EqualTo(-500));
            Assert.That(domain.SlowOffset, Is.EqualTo(500));
            Assert.That(domain.RelativePosition(domain.FastOffset), Is.Zero);
            Assert.That(domain.RelativePosition(domain.SlowOffset), Is.EqualTo(1));
            Assert.That(domain.RelativePosition(0), Is.EqualTo(0.5f));

            var scratchWindows = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 0, 0.75, tail: false);
            Assert.That(domain.RelativePosition(-scratchWindows.FastWindowFor(HitResult.Ok)), Is.GreaterThan(0));
            Assert.That(domain.RelativePosition(scratchWindows.SlowWindowFor(HitResult.Ok)), Is.LessThan(1));
        });
    }

    [Test]
    public void TestLongNoteUsesBothEndpointOffsets()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var longNote = new BmsLongNote
        {
            Beatmap = beatmap,
            StartTime = 1000,
            Duration = 500,
            Column = 1,
        };
        var result = new BmsLongNoteJudgementResult(longNote, longNote.CreateJudgement(),
        [
            new BmsLongNoteEndpointResult(longNote, BmsLongNoteEndpointKind.Head, 988, 1, HitResult.Perfect),
            new BmsLongNoteEndpointResult(longNote, BmsLongNoteEndpointKind.Tail, 1519, 1, HitResult.Great),
        ])
        {
            Type = HitResult.Great,
        };

        var observations = BmsHitErrorMeter.GetTimingObservations(result);

        Assert.Multiple(() =>
        {
            Assert.That(observations, Has.Count.EqualTo(2));
            Assert.That(observations[0], Is.EqualTo(new BmsHitErrorTimingObservation(-12, HitResult.Perfect)));
            Assert.That(observations[1], Is.EqualTo(new BmsHitErrorTimingObservation(19, HitResult.Great)));
        });
    }

    [Test]
    public void TestPoorUsesBadWindowEdgeMatchingTimingDirectionOnlyWhenEnabled()
    {
        var windows = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, 0.75, tail: false);
        var badWindowFastEdge = -windows.FastWindowFor(HitResult.Ok);
        var badWindowSlowEdge = windows.SlowWindowFor(HitResult.Ok);
        var fastPoor = new BmsHitErrorTimingObservation(-281, HitResult.Meh);
        var slowPoor = new BmsHitErrorTimingObservation(281, HitResult.Meh);

        Assert.Multiple(() =>
        {
            Assert.That(badWindowFastEdge, Is.EqualTo(-220));
            Assert.That(badWindowSlowEdge, Is.EqualTo(280));
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(fastPoor, badWindowFastEdge, badWindowSlowEdge, showPoor: false), Is.Null);
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(fastPoor, badWindowFastEdge, badWindowSlowEdge, showPoor: true), Is.EqualTo(badWindowFastEdge));
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(slowPoor, badWindowFastEdge, badWindowSlowEdge, showPoor: true), Is.EqualTo(badWindowSlowEdge));
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(
                new BmsHitErrorTimingObservation(-12, HitResult.Perfect), badWindowFastEdge, badWindowSlowEdge, showPoor: false), Is.EqualTo(-12));
        });
    }

    [TestCase(HitResult.Perfect, true)]
    [TestCase(HitResult.Great, true)]
    [TestCase(HitResult.Good, true)]
    [TestCase(HitResult.Ok, true)]
    [TestCase(HitResult.Meh, false)]
    [TestCase(HitResult.Miss, false)]
    public void TestPoorAndEmptyPoorDoNotAffectMovingAverage(HitResult result, bool expected)
    {
        Assert.That(BmsHitErrorMeter.AffectsMovingAverage(result), Is.EqualTo(expected));
    }
}
