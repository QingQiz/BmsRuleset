using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal;

[TestFixture]
public class BmsRulesetTest
{

    [SetUp]
    public void SetUp()
    {
        ruleset = new BmsRuleset();
    }

    private BmsRuleset ruleset = null!;

    [Test]
    public void TestAutomationModsIncludeAutoplay()
    {
        var mods = ruleset.GetModsFor(ModType.Automation).ToArray();

        Assert.That(mods.OfType<BmsModAutoplay>().SingleOrDefault(), Is.Not.Null);
    }

    [Test]
    public void TestCreateBeatmapConverter()
    {
        var converter = ruleset.CreateBeatmapConverter(new Beatmap());

        Assert.That(converter, Is.TypeOf<BmsBeatmapConverter>());
    }

    [Test]
    public void TestCreateConfig()
    {
        var config = ruleset.CreateConfig(null);

        Assert.That(config, Is.TypeOf<BmsRulesetConfigManager>());
    }

    [Test]
    public void TestCreateDifficultyCalculatorUsesNativeBmsImplementation()
    {
        var working = new TestWorkingBeatmap(new BmsBeatmap());
        var calculator = ruleset.CreateDifficultyCalculator(working);

        Assert.That(calculator, Is.TypeOf<BmsDifficultyCalculator>());
    }

    [Test]
    public void TestCreateDrawableRulesetUsesBeatmapColumnCount()
    {
        var beatmap = new BmsBeatmap
        {
            TotalColumns = 16,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 15 },
            },
        };

        var drawableRuleset = (BmsDrawableRuleset)ruleset.CreateDrawableRulesetWith(beatmap);

        Assert.That(((BmsPlayfield)drawableRuleset.Playfield).TotalColumns, Is.EqualTo(16));
    }

    [Test]
    public void TestCreateDrawableRulesetUsesNativeBmsImplementation()
    {
        var drawableRuleset = ruleset.CreateDrawableRulesetWith(new BmsBeatmap());

        Assert.That(drawableRuleset, Is.TypeOf<BmsDrawableRuleset>());
    }

    [Test]
    public void TestCreateHealthProcessor()
    {
        var processor = ruleset.CreateHealthProcessor(0);

        Assert.That(processor, Is.TypeOf<BmsHealthProcessor>());
    }

    [Test]
    public void TestCreateIcon()
    {
        var icon = ruleset.CreateIcon();

        Assert.That(icon, Is.Not.Null);
    }

    [Test]
    public void TestCreateScoreProcessor()
    {
        var processor = ruleset.CreateScoreProcessor();

        Assert.That(processor, Is.TypeOf<BmsScoreProcessor>());
    }

    [Test]
    public void TestCreateSettingsIsNotManiaSettings()
    {
        var settings = ruleset.CreateSettings();

        Assert.That(settings, Is.Not.Null);
        Assert.That(settings, Is.TypeOf<BmsSettingsSubsection>());
    }

    [Test]
    public void TestEmptyPoorDisplayNameIsEPoor()
    {
        Assert.That(ruleset.GetDisplayNameForHitResult(HitResult.Miss).ToString(), Is.EqualTo("E-POOR"));
    }

    [Test]
    public void TestFunModsContainAllThreeLongNoteModeMods()
    {
        var mods = ruleset.GetModsFor(ModType.Fun).ToArray();

        Assert.That(mods.OfType<BmsModLongNote>().SingleOrDefault(), Is.Not.Null);
        Assert.That(mods.OfType<BmsModChargeNote>().SingleOrDefault(), Is.Not.Null);
        Assert.That(mods.OfType<BmsModHellChargeNote>().SingleOrDefault(), Is.Not.Null);
    }

    [Test]
    public void TestGaugeModAppliesGaugeTypeToHealthProcessor()
    {
        var processor = new BmsHealthProcessor();
        var mod = new BmsModExHardGauge();

        mod.ApplyToHealthProcessor(processor);

        Assert.That(processor.GaugeType, Is.EqualTo(BmsGaugeType.ExHard));
    }

    [Test]
    public void TestGaugeModsAreMutuallyIncompatible()
    {
        var hard = new BmsModHardGauge();

        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModAssistEasyGauge)));
        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModEasyGauge)));
        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModExHardGauge)));
        Assert.That(hard.IncompatibleMods, Does.Contain(typeof(BmsModHazardGauge)));
    }

    [Test]
    public void TestGaugeModsRegisteredWithExpectedAcronyms()
    {
        var reduction = ruleset.GetModsFor(ModType.DifficultyReduction).ToArray();
        var increase = ruleset.GetModsFor(ModType.DifficultyIncrease).ToArray();

        Assert.That(reduction.OfType<BmsModAssistEasyGauge>().Single().Acronym, Is.EqualTo("E2"));
        Assert.That(reduction.OfType<BmsModEasyGauge>().Single().Acronym, Is.EqualTo("E1"));
        Assert.That(increase.OfType<BmsModHardGauge>().Single().Acronym, Is.EqualTo("H1"));
        Assert.That(increase.OfType<BmsModExHardGauge>().Single().Acronym, Is.EqualTo("H2"));
        Assert.That(increase.OfType<BmsModHazardGauge>().Single().Acronym, Is.EqualTo("H3"));
    }

    [Test]
    public void TestLnModeDisplayAttributeHiddenWhenUndefined()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, KeyCount = 9, LockedLongNoteMode = BmsLongNoteMode.Undefined }
            .WriteToOsuDifficulty(beatmapInfo);

        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>());
        var lnModeAttr = attributes.SingleOrDefault(a => a.Acronym == "LM");

        Assert.That(lnModeAttr, Is.Null);
    }

    [Test]
    public void TestLnModeDisplayAttributeShowsWhenLocked()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 2, KeyCount = 9, LockedLongNoteMode = BmsLongNoteMode.ChargeNote }
            .WriteToOsuDifficulty(beatmapInfo);

        var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>());
        var lnModeAttr = attributes.SingleOrDefault(a => a.Acronym == "LM");

        Assert.That(lnModeAttr, Is.Not.Null);
        Assert.That(lnModeAttr.AdditionalMetrics[0].Value.ToString(), Is.EqualTo("CN"));
    }

    [Test]
    public void TestMetadata()
    {
        Assert.That(ruleset.ShortName, Is.EqualTo("bms"));
        Assert.That(ruleset.Description, Is.EqualTo("BMS"));
        Assert.That(ruleset.RulesetAPIVersionSupported, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TestRankAttributeMetricsIncludeScratchAndLongNoteTailWindows()
    {
        var beatmapInfo = new BeatmapInfo();
        new BmsDifficultyInfo { Rank = 3, KeyCount = 8 }.WriteToOsuDifficulty(beatmapInfo);

        var rank = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, Array.Empty<Mod>())
            .Single(attribute => attribute.Acronym == "RK");

        var metrics = rank.AdditionalMetrics.ToDictionary(metric => metric.Name.ToString(), metric => metric.Value.ToString());

        Assert.That(metrics["Normal note PGREAT"], Is.EqualTo("-20 to +20 ms"));
        Assert.That(metrics["Scratch BAD"], Is.EqualTo("-230 to +290 ms"));
        Assert.That(metrics["LN tail PGREAT"], Is.EqualTo("-120 to +120 ms"));
        Assert.That(metrics["Scratch LN tail GOOD"], Is.EqualTo("-210 to +210 ms"));
    }
}
