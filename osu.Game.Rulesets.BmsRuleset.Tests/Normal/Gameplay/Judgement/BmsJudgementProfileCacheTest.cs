using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public partial class BmsJudgementProfileCacheTest
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
    public void SelectedProfilesKeepSharedChartAndOtherAttemptsIsolated()
    {
        var decoded = new BmsDecodedBeatmap { RawLines = ["#BPM 120", "#RANK 2", "#00018:01"] };
        decoded.CopyFrom(BmsChartParser.Parse(decoded.RawLines));
        var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
        var note = beatmap.HitObjects[0];
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        var sourceWindows = note.HitWindows;
        var first = new BmsEngineChart(beatmap, GameplayProfile.FromMods([new BmsModNoBad()]));
        var second = new BmsEngineChart(beatmap, GameplayProfile.FromMods([new BmsModLr2()]));
        var original = new BmsEngineChart(beatmap);
        Assert.That(first.TimingFor(note).HeadWindows.Entries.Any(window => window.Result == BmsResults.BAD), Is.False);
        Assert.That(second.Profile.CreateHitWindows(note, second.TimingFor(note), NoteParts.HEAD).WindowFor(HitResult.Perfect), Is.EqualTo(18));
        Assert.That(original.Profile.CreateHitWindows(note, original.TimingFor(note), NoteParts.HEAD).WindowFor(HitResult.Perfect), Is.EqualTo(15));
        Assert.That(note.HitWindows, Is.SameAs(sourceWindows));
        Assert.That(note.CloneForConversion().Beatmap, Is.SameAs(beatmap));

        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2()];
        score.ApplyBeatmap(beatmap);
        Assert.That(note.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(18));
        score.Mods.Value = [];
        score.ApplyBeatmap(beatmap);
        Assert.That(note.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(15));
        Assert.That(second.Profile.CreateHitWindows(note, second.TimingFor(note), NoteParts.HEAD).WindowFor(HitResult.Perfect), Is.EqualTo(18));
    }
}
