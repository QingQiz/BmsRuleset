using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture, NonParallelizable]
public class BmsJudgementProfileCacheTest
{
    [Test]
    public void AlternatingThreadsLayoutsRatesAndEndpointsPreserveTables()
    {
        var requests = (from layout in new[] { BmsLayoutVariant.Bms5K, BmsLayoutVariant.Bme7KDouble, BmsLayoutVariant.Pms9K }
                        from rate in new[] { .25, .75, 1.3580245 }
                        from column in new[] { 0, 1, 15 }
                        from tail in new[] { false, true }
                        select (layout, rate, column, tail)).ToArray();
        var expected = requests.Select(r => BmsJudgementProfileProvider.GetTable(r.layout, r.column, r.rate, r.tail)).ToArray();
        Parallel.For(0, 4, worker =>
        {
            for (var i = 0; i < requests.Length * 4; i++)
            {
                var index = (i + worker) % requests.Length;
                var r = requests[index];
                Assert.That(BmsJudgementProfileProvider.GetTable(r.layout, r.column, r.rate, r.tail), Is.SameAs(expected[index]));
            }
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void RepeatedModTablesReuseStorageAcrossNotesButFollowInputChanges(int kind)
    {
        IApplicableToJudgementWindow mod = kind switch { 0 => new BmsModNoBad(), 1 => new BmsModNoGood(), _ => new BmsModNoGreat() };
        var original = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, .75, false);
        var tail = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, .75, true);
        var first = mod.ApplyToJudgementWindow(original);
        var other = mod.ApplyToJudgementWindow(tail);
        Assert.That(other, Is.Not.SameAs(first));
        Assert.That(first.SlowWindowFor(HitResult.Ok), Is.EqualTo(original.SlowWindowFor(HitResult.Ok)));
        Assert.That(other.SlowWindowFor(HitResult.Ok), Is.EqualTo(tail.SlowWindowFor(HitResult.Ok)));
        for (var i = 0; i < 32; i++) mod.ApplyToJudgementWindow(original);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 256; i++) mod.ApplyToJudgementWindow(original);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.That(allocated, Is.Zero);
        Assert.That(mod.ApplyToJudgementWindow(original), Is.SameAs(first));
    }

    [Test]
    public void WarmProfileStillUsesCurrentModListAndClearsOnlyItsOwner()
    {
        IApplicableToJudgementWindow[] noBad = [new BmsModNoBad()];
        IApplicableToJudgementWindow[] noGood = [new BmsModNoGood()];
        BmsJudgementWindowTable table() => BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, 1, .75, false);
        var original = table();
        try
        {
            BmsJudgementProfileProvider.SetActiveWindowMods(noBad);
            Assert.That(table().ResultForOffset(180), Is.EqualTo(HitResult.None));
            BmsJudgementProfileProvider.SetActiveWindowMods(noGood);
            BmsJudgementProfileProvider.ClearActiveWindowMods(noBad);
            Assert.That(table().ResultForOffset(80), Is.EqualTo(HitResult.Ok));
            Assert.That(table().ResultForOffset(180), Is.EqualTo(HitResult.Ok));
            BmsJudgementProfileProvider.ClearActiveWindowMods(noGood);
            Assert.That(table(), Is.SameAs(original));
        }
        finally
        {
            BmsJudgementProfileProvider.ClearActiveWindowMods(noBad);
            BmsJudgementProfileProvider.ClearActiveWindowMods(noGood);
        }
    }
}
