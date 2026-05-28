using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsRulesetTest
{
    private BmsRuleset ruleset = null!;

    [SetUp]
    public void SetUp()
    {
        ruleset = new BmsRuleset();
    }

    [Test]
    public void TestAutomationModsIncludeAutoplay()
    {
        var mods = ruleset.GetModsFor(ModType.Automation).ToArray();

        Assert.That(mods.OfType<BmsModAutoplay>().SingleOrDefault(), Is.Not.Null);
        Assert.That(mods.OfType<BmsModCinema>().SingleOrDefault(), Is.Not.Null);
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
    public void TestMetadata()
    {
        Assert.That(ruleset.ShortName, Is.EqualTo("bms"));
        Assert.That(ruleset.Description, Is.EqualTo("BMS Ruleset"));
        Assert.That(ruleset.RulesetAPIVersionSupported, Is.Not.Null.And.Not.Empty);
    }
}
