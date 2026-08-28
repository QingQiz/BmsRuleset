using System;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Conversion;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Conversion;

[TestFixture]
public class BmsForeignBeatmapConverterRegistryTest
{
    private readonly Beatmap source = new();

    [Test]
    public void TestAllowsSupportedForeignConversionForBms()
    {
        var mania = createManiaBeatmapInfo(7);
        var bms = new RulesetInfo { ShortName = Constant.SHORT_NAME, OnlineID = -1 };

        Assert.That(BmsForeignBeatmapConverterRegistry.AllowsGameplay(mania, bms, true), Is.True);
    }

    [Test]
    public void TestRejectsForeignConversionWhenDisabled()
    {
        var mania = createManiaBeatmapInfo(7);
        var bms = new RulesetInfo { ShortName = Constant.SHORT_NAME, OnlineID = -1 };

        Assert.That(BmsForeignBeatmapConverterRegistry.AllowsGameplay(mania, bms, false), Is.False);
    }

    [Test]
    public void TestDoesNotExtendForeignConversionToOtherTargetRulesets()
    {
        var mania = createManiaBeatmapInfo(7);
        var catchRuleset = new RulesetInfo { ShortName = "fruits", OnlineID = 2 };

        Assert.That(BmsForeignBeatmapConverterRegistry.AllowsGameplay(mania, catchRuleset, true), Is.False);
    }

    [Test]
    public void TestNoMatchReturnsNull()
    {
        var result = BmsForeignBeatmapConverterRegistry.FindConverter(source, [new TestConverter(false)]);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TestUniqueMatchIsSelected()
    {
        var expected = new TestConverter(true);

        var result = BmsForeignBeatmapConverterRegistry.FindConverter(
            source,
            [new TestConverter(false), expected]);

        Assert.That(result, Is.SameAs(expected));
    }

    [Test]
    public void TestMultipleMatchesThrow()
    {
        Assert.Throws<InvalidOperationException>(() => BmsForeignBeatmapConverterRegistry.FindConverter(
            source,
            [new TestConverter(true), new TestConverter(true)]));
    }

    private static BeatmapInfo createManiaBeatmapInfo(int keyCount) => new(
        new RulesetInfo { ShortName = "mania", OnlineID = 3 },
        new BeatmapDifficulty { CircleSize = keyCount });

    private sealed class TestConverter(bool canConvert) : IBmsForeignBeatmapConverter
    {
        public bool CanConvert(IBeatmapInfo source) => canConvert;

        public BmsDifficultyInfo GetConvertedDifficultyInfo(IBeatmapInfo source) => new() { KeyCount = 8 };

        public bool CanConvert(IBeatmap source) => canConvert;

        public void Convert(IBeatmap source, BmsBeatmap target, CancellationToken cancellationToken)
        {
        }
    }
}
