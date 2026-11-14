using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

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
        var modifiers = modifier switch
        {
            "nb" => BmsJudgementModifiers.NoBad, "ng" => BmsJudgementModifiers.NoGood,
            "ne" => BmsJudgementModifiers.NoGreat, _ => BmsJudgementModifiers.None,
        };
        BmsJudgementPerformanceTest.Measure("Profile-" + modifier, count, () =>
        {
            var checksum = 0d;
            return new BmsJudgementPerformanceTest.Measurement(() =>
            {
                for (var i = 0; i < count; i++)
                {
                    var table = BmsEngineRules.GetTiming(BmsLayoutVariant.Bme7K, i % 8, .75, (i & 1) == 0, modifiers);
                    checksum += table.PassiveDeadline / 1000.0;
                }
            }, () =>
            {
                Assert.That(checksum, Is.EqualTo(42187500));
                return new { Checksum = checksum };
            });
        });
    }
}
