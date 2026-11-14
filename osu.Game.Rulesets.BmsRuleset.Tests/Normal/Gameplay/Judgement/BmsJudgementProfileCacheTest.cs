using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsJudgementProfileCacheTest
{
    [Test]
    public void ConcurrentConfigurationsShareDefinitionsWithoutLeakingMods()
    {
        var requests = (from layout in new[] { BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Popn9K }
                        from rate in new[] { .25, .75, 1.3580245 }
                        from column in new[] { 0, 1, 15 }
                        from tail in new[] { false, true }
                        from modifiers in new[] { BmsJudgementModifiers.None, BmsJudgementModifiers.NoBad, BmsJudgementModifiers.Lr2oraja | BmsJudgementModifiers.NoGood }
                        select (layout, rate, column, tail, modifiers)).ToArray();
        var expected = requests.Select(r => BmsEngineRules.GetTiming(r.layout, r.column, r.rate, r.tail, r.modifiers)).ToArray();
        Parallel.For(0, 4, worker =>
        {
            for (var i = 0; i < requests.Length; i++)
            {
                var index = (i + worker) % requests.Length;
                var r = requests[index];
                Assert.That(BmsEngineRules.GetTiming(r.layout, r.column, r.rate, r.tail, r.modifiers), Is.SameAs(expected[index]));
            }
        });
    }

    [Test]
    public void ConversionOwnsModSelectionAndKeepsSourceAndOtherAttemptsIsolated()
    {
        var decoded = new BmsDecodedBeatmap { RawLines = ["#BPM 120", "#RANK 2", "#00018:01"] };
        decoded.CopyFrom(BmsChartParser.Parse(decoded.RawLines));
        var firstConverter = new BmsBeatmapConverter(decoded, new BmsRuleset());
        ((IApplicableToBeatmapConverter)new BmsModNoBad()).ApplyToBeatmapConverter(firstConverter);
        var secondConverter = new BmsBeatmapConverter(decoded, new BmsRuleset());
        ((IApplicableToBeatmapConverter)new BmsModLr2()).ApplyToBeatmapConverter(secondConverter);
        var first = (BmsBeatmap)firstConverter.Convert();
        var second = (BmsBeatmap)secondConverter.Convert();
        var original = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();

        foreach (var beatmap in new[] { first, second, original })
        {
            foreach (var note in beatmap.HitObjects)
                note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        }

        Assert.That(first.HitObjects[0].HitWindows.WindowFor(HitResult.Ok), Is.EqualTo(210));
        Assert.That(BmsEngineRules.GetTiming(first.HitObjects[0], false).WindowFor(BmsResults.BAD), Is.Null);
        Assert.That(second.HitObjects[0].HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(original.HitObjects[0].HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(15));
        Assert.That(decoded.JudgementModifiers, Is.EqualTo(BmsJudgementModifiers.None));
        Assert.That(first.HitObjects[0].CloneForConversion().Beatmap, Is.SameAs(first));
    }
}
