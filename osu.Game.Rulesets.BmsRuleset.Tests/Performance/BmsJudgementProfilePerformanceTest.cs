using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Performance;

[TestFixture, NonParallelizable, Explicit("Opt-in CPU measurements."), Category("Performance")]
public class BmsJudgementProfilePerformanceTest
{
    [TestCase("none")]
    [TestCase("nb")]
    [TestCase("ng")]
    [TestCase("ne")]
    public void RepeatedProfileQueries(string modifier)
    {
        const int count = 200000;
        IApplicableToJudgementWindow[] mods = modifier switch
        {
            "nb" => [new BmsModNoBad()], "ng" => [new BmsModNoGood()], "ne" => [new BmsModNoGreat()], _ => [],
        };
        BmsJudgementProfileProvider.SetActiveWindowMods(mods);
        try
        {
            BmsJudgementPerformanceTest.Measure("Profile-" + modifier, count, () =>
            {
                var checksum = 0d;
                return new BmsJudgementPerformanceTest.Measurement(() =>
                {
                    for (var i = 0; i < count; i++)
                    {
                        var table = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, i % 8, .75, (i & 1) == 0);
                        checksum += table.SlowWindowFor(HitResult.Ok);
                    }
                }, () =>
                {
                    Assert.That(checksum, Is.EqualTo(42187500));
                    return new { Checksum = checksum };
                });
            });
        }
        finally { BmsJudgementProfileProvider.ClearActiveWindowMods(mods); }
    }
}
