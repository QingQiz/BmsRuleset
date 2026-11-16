using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

[TestFixture]
public class BmsHitErrorMeterTest
{
    [Test]
    public void TestAsymmetricWindowsUseSymmetricDomain()
    {
        var domain = BmsHitErrorMeter.CreateDomain(createWindows(0.75, BmsJudgementModifiers.None, 0, 1));

        Assert.Multiple(() =>
        {
            Assert.That(domain.FastOffset, Is.EqualTo(-500));
            Assert.That(domain.SlowOffset, Is.EqualTo(500));
            Assert.That(domain.RelativePosition(domain.FastOffset), Is.Zero);
            Assert.That(domain.RelativePosition(domain.SlowOffset), Is.EqualTo(1));
            Assert.That(domain.RelativePosition(0), Is.EqualTo(0.5f));

            var scratchWindows = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, 0, 0.75, tail: false);
            Assert.That(domain.RelativePosition(-scratchWindows.FastWindowFor(HitResult.Ok)), Is.GreaterThan(0));
            Assert.That(domain.RelativePosition(scratchWindows.SlowWindowFor(HitResult.Ok)), Is.LessThan(1));
        });
    }

    [TestCase(false, false, 0, -165, 210)]
    [TestCase(false, false, 1, -165, 210)]
    [TestCase(false, false, 2, -165, 210)]
    [TestCase(false, true, 0, -500, 210)]
    [TestCase(false, true, 1, -500, 210)]
    [TestCase(false, true, 2, -500, 210)]
    [TestCase(true, false, 0, -112.5, 112.5)]
    [TestCase(true, false, 1, -45, 45)]
    [TestCase(true, false, 2, -15, 15)]
    [TestCase(true, true, 0, -500, 150)]
    [TestCase(true, true, 1, -500, 150)]
    [TestCase(true, true, 2, -500, 150)]
    public void TestPoorUsesVisibleWindowEdges(bool noBad, bool showEmptyPoor, int constraint, double expectedFast, double expectedSlow)
    {
        var modifiers = (noBad ? BmsJudgementModifiers.NoBad : BmsJudgementModifiers.None) | (constraint switch
        {
            1 => BmsJudgementModifiers.NoGood,
            2 => BmsJudgementModifiers.NoGreat,
            _ => BmsJudgementModifiers.None,
        });
        var windows = createWindows(0.75, modifiers, 1);

        var (fastEdge, slowEdge) = BmsHitErrorMeter.GetUntimedDisplayOffsets(windows, showEmptyPoor);
        var kind = BmsGameplayProfile.INSTANCE.TimingKindFor(HitResult.Meh);

        Assert.Multiple(() =>
        {
            Assert.That(fastEdge, Is.EqualTo(expectedFast));
            Assert.That(slowEdge, Is.EqualTo(expectedSlow));
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(-281, kind, fastEdge, slowEdge, showUntimed: false, showEmptyInput: true), Is.Null);
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(281, kind, fastEdge, slowEdge, showUntimed: false, showEmptyInput: true), Is.Null);
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(-281, kind, fastEdge, slowEdge, showUntimed: true, showEmptyInput: true), Is.EqualTo(expectedFast));
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(281, kind, fastEdge, slowEdge, showUntimed: true, showEmptyInput: true), Is.EqualTo(expectedSlow));
            Assert.That(BmsHitErrorMeter.GetDisplayOffset(
                -12, BmsGameplayProfile.INSTANCE.TimingKindFor(HitResult.Perfect), fastEdge, slowEdge, showUntimed: false, showEmptyInput: true), Is.EqualTo(-12));
        });
    }

    [TestCase(0, 187.5)]
    [TestCase(1, 150)]
    [TestCase(2, 150)]
    public void TestNoBadUsesOutermostSlowWindowAtEasyRank(int constraint, double expectedSlow)
    {
        var modifiers = (BmsJudgementModifiers.NoBad) | (constraint switch
        {
            1 => BmsJudgementModifiers.NoGood,
            2 => BmsJudgementModifiers.NoGreat,
            _ => BmsJudgementModifiers.None,
        });
        var windows = createWindows(1.25, modifiers, 1);

        var (fastEdge, slowEdge) = BmsHitErrorMeter.GetUntimedDisplayOffsets(windows, showEmptyInput: true);

        Assert.That(fastEdge, Is.EqualTo(-500));
        Assert.That(slowEdge, Is.EqualTo(expectedSlow));
    }

    [TestCase(HitResult.Perfect, true)]
    [TestCase(HitResult.Great, true)]
    [TestCase(HitResult.Good, true)]
    [TestCase(HitResult.Ok, true)]
    [TestCase(HitResult.Meh, false)]
    [TestCase(HitResult.Miss, false)]
    public void TestPoorAndEmptyPoorDoNotAffectMovingAverage(HitResult result, bool expected)
    {
        Assert.That(BmsGameplayProfile.INSTANCE.TimingKindFor(result) == ResultTimingKind.Timed, Is.EqualTo(expected));
    }
    private static IReadOnlyList<BmsHitErrorWindow> createWindows(double rate, BmsJudgementModifiers modifiers, params int[] columns)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, ExRank = rate / 0.75 * 100 };
        beatmap.HitObjects.AddRange(columns.Select(column => new BmsNote { Beatmap = beatmap, Column = column, StartTime = 1000 }));
        return BmsHitErrorMeter.CreateWindows(new BmsEngineChart(beatmap, new BmsGameplayProfile(modifiers)));
    }

}
