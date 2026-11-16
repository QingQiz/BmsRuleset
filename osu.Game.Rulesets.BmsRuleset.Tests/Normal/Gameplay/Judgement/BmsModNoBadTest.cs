using System.Linq;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsModNoBadTest
{
    [Test]
    public void TestOnlyBadIsRemoved(
        [Values(BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7K, BmsLayoutVariant.Popn9K)]
        BmsLayoutVariant layout,
        [Values(0, 1)] int column,
        [Values(0, 1, 2, 3, 4)] int rank,
        [Values(false, true)] bool tail)
    {
        var original = BmsEngineRules.GetTiming(layout, column, rank, tail);
        var table = BmsEngineRules.GetTiming(layout, column, rank, tail, BmsJudgementModifiers.NoBad);

        Assert.That(table.WindowFor(BmsResults.EMPTY_POOR), Is.EqualTo(original.WindowFor(BmsResults.EMPTY_POOR)));
        Assert.That(table.Windows.Entries.Where(row => row.Result != BmsResults.BAD),
            Is.EqualTo(original.Windows.Entries.Where(row => row.Result != BmsResults.BAD)));
        Assert.That(table.PassiveDeadline, Is.EqualTo(original.PassiveDeadline));

        for (var offset = -550d; offset <= 550; offset += 0.5)
        {
            var result = original.HitAt(offset);
            Assert.That(table.HitAt(offset), Is.EqualTo(result == HitResult.Ok ? HitResult.None : result), $"offset {offset}");
            Assert.That(table.EmptyPoorAt(offset),
                Is.EqualTo(original.WindowFor(BmsResults.EMPTY_POOR) is { } miss && miss.Contains(Time.FromMilliseconds(offset)) && result is HitResult.None or HitResult.Ok),
                $"empty POOR at {offset}");
            Assert.That(table.PastDeadline(offset), Is.EqualTo(original.PastDeadline(offset)), $"passive POOR at {offset}");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestCombinationWithConstraintsIsOrderIndependent(bool noGreat)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2 };
        var note = new BmsNote { Beatmap = beatmap, StartTime = 1000, Column = 1 };
        beatmap.HitObjects.Add(note);
        var firstProfile = GameplayProfile.FromMods([new BmsModNoBad(), noGreat ? new BmsModNoGreat() : new BmsModNoGood()]);
        var secondProfile = GameplayProfile.FromMods([noGreat ? new BmsModNoGreat() : new BmsModNoGood(), new BmsModNoBad()]);
        var first = new BmsEngineChart(beatmap, firstProfile).TimingFor(note);
        var second = new BmsEngineChart(beatmap, secondProfile).TimingFor(note);

        Assert.That(first.HeadWindows.Entries, Is.EqualTo(second.HeadWindows.Entries));
        Assert.That(first.HeadWindows.ResultFor(60_000), Is.EqualTo(BmsResults.EMPTY_POOR));
        Assert.That(first.HeadWindows.ResultFor(20_000), Is.EqualTo(noGreat ? BmsResults.EMPTY_POOR : BmsResults.GREAT));
        Assert.That(first.HeadWindows.ResultFor(0), Is.EqualTo(BmsResults.PGREAT));
    }

    [Test]
    public void TestModCanBeResolvedFromAcronym()
    {
        Assert.That(new BmsRuleset().CreateModFromAcronym("NB"), Is.TypeOf<BmsModNoBad>());
    }
}
