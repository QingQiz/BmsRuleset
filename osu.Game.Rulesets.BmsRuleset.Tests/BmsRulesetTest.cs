using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Settings;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Beatmaps;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsRulesetTest
{
    [SetUp]
    public void SetUp()
    {
        ruleset = new BmsRuleset();
    }

    private BmsRuleset ruleset = null!;

    private class TestLegacySkin : ISkin
    {
        private readonly HashSet<string> textures;

        public TestLegacySkin(IEnumerable<string> textures = null)
        {
            this.textures = textures?.ToHashSet() ?? [];
        }

        public Drawable GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            if (!textures.Contains(componentName))
                return null;

            var texture = Texture.FromStream(new DummyRenderer(),
                new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")));
            Assert.That(texture, Is.Not.Null);
            return texture!;
        }

        public ISample GetSample(ISampleInfo sampleInfo) => null;

        public IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull
        {
            if (lookup is SkinConfiguration.LegacySetting legacy && legacy == SkinConfiguration.LegacySetting.Version)
                return SkinUtils.As<TValue>(new Bindable<decimal>(2.7m));

            if (lookup is LegacyManiaSkinConfigurationLookup maniaLookup)
            {
                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.NoteImage && maniaLookup.ColumnIndex == 0)
                    return SkinUtils.As<TValue>(new Bindable<string>("Note/Note-1H"));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.ColumnLightColour && maniaLookup.ColumnIndex == 0)
                    return SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(0, 0, 0, 1)));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour && maniaLookup.ColumnIndex == 0)
                    return SkinUtils.As<TValue>(new Bindable<Color4>(new Color4(0, 0, 0, 0)));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.HitPosition)
                    return SkinUtils.As<TValue>(new Bindable<float>(64));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.LightPosition)
                    return SkinUtils.As<TValue>(new Bindable<float>(64));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.ScorePosition)
                    return SkinUtils.As<TValue>(new Bindable<float>(400));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.ShowJudgementLine)
                    return SkinUtils.As<TValue>(new Bindable<bool>(true));

                if (maniaLookup.TotalColumns == 7 && maniaLookup.Lookup == LegacyManiaSkinConfigurationLookups.BarLineHeight)
                    return SkinUtils.As<TValue>(new Bindable<float>(1.2f));
            }

            return null;
        }
    }

    [Test]
    public void TestAutomationModsIncludeAutoplay()
    {
        var mods = ruleset.GetModsFor(ModType.Automation).ToArray();

        Assert.That(mods.OfType<BmsModAutoplay>().SingleOrDefault(), Is.Not.Null);
        Assert.That(mods.OfType<BmsModCinema>().SingleOrDefault(), Is.Not.Null);
    }

    [Test]
    public void TestAutoplayGeneratesPressAndReleaseFrames()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 1200, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2, IsLongNote = true, Duration = 500 },
            },
        };

        var replay = new BmsAutoGenerator(beatmap).Generate();
        var frames = replay.Frames.OfType<BmsReplayFrame>().ToArray();

        Assert.That(frames.Select(f => f.Time), Does.Contain(1000));
        Assert.That(frames.Select(f => f.Time), Does.Contain(1200));
        Assert.That(frames.Select(f => f.Time), Does.Contain(2500));
        Assert.That(frames.Single(f => f.Time == 1000).Actions, Contains.Item(BmsAction.Key1));
        Assert.That(frames.Single(f => f.Time == 2500).Actions, Does.Not.Contain(BmsAction.Key2));
    }

    [Test]
    public void TestAutoplayReleasesBeforePressingSameActionAtSameTime()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 1100, Column = 1 },
            },
        };

        var replay = new BmsAutoGenerator(beatmap).Generate();
        var frames = replay.Frames.OfType<BmsReplayFrame>().ToArray();

        Assert.That(frames.Single(f => f.Time == 1100).Actions, Contains.Item(BmsAction.Key1));
    }

    [Test]
    public void TestBeatmapConverterCanConvertBmsHitObjects()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 0 },
                new BmsHitObject { StartTime = 2000, Column = 1, IsLongNote = true, Duration = 500 },
            },
        };
        var converter = ruleset.CreateBeatmapConverter(beatmap);

        Assert.That(converter.CanConvert(), Is.True);
    }

    [Test]
    public void TestBeatmapConverterCanConvertEmptyBeatmap()
    {
        var beatmap = new Beatmap();
        var converter = ruleset.CreateBeatmapConverter(beatmap);

        Assert.That(converter.CanConvert(), Is.False);
    }

    [Test]
    public void TestBeatmapConverterConvert()
    {
        var beatmap = new Beatmap
        {
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 2 },
                new BmsHitObject { StartTime = 2000, Column = 5, IsLongNote = true, Duration = 800 },
            },
        };
        var converter = ruleset.CreateBeatmapConverter(beatmap);
        var converted = converter.Convert();

        Assert.That(converted.HitObjects.Count, Is.EqualTo(2));
    }

    [Test]
    public void TestCreateBeatmapConverter()
    {
        var beatmap = new Beatmap();
        var converter = ruleset.CreateBeatmapConverter(beatmap);

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
        var beatmap = new BmsBeatmap();
        var drawableRuleset = ruleset.CreateDrawableRulesetWith(beatmap);

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
    public void TestDefaultKeyBindingsIncludeDoublePlayActions()
    {
        var bindings = ruleset.GetDefaultKeyBindings((int)BmsLayoutVariant.Bme7KDouble).ToArray();

        Assert.That(bindings.Select(b => b.Action), Is.SupersetOf(new object[]
        {
            BmsAction.P2Scratch,
            BmsAction.P2Key1,
            BmsAction.P2Key7,
        }));
        Assert.That(bindings.Single(b => (BmsAction)b.Action == BmsAction.P2Scratch).KeyCombination.Keys, Contains.Item(InputKey.RShift));
        Assert.That(bindings.Single(b => (BmsAction)b.Action == BmsAction.P2Key1).KeyCombination.Keys, Contains.Item(InputKey.Keypad1));
    }

    [Test]
    public void TestDoublePlayActionsMapToSecondColumnBank()
    {
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Scratch, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(0));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key7, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(7));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Key1, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(8));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Key7, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(14));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Scratch, BmsLayoutVariant.Bme7KDouble), Is.EqualTo(15));
    }

    [Test]
    public void TestLayoutSpecificActionsDoNotReuseScratchMappings()
    {
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key5, BmsLayoutVariant.Bms5K), Is.EqualTo(5));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Key6, BmsLayoutVariant.Bms5K), Is.Null);
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2Scratch, BmsLayoutVariant.Bms5KDouble), Is.EqualTo(11));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.PmsKey1, BmsLayoutVariant.Pms9K), Is.EqualTo(0));
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.Scratch, BmsLayoutVariant.Pms9K), Is.Null);
        Assert.That(BmsKeyBindingConfiguration.ActionToColumn(BmsAction.P2PmsKey9, BmsLayoutVariant.Pms9KDouble), Is.EqualTo(17));
    }

    [Test]
    public void TestLegacyManiaSkinDiscoversDefaultNamedResourcesWithoutKeyGate()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };
        var skin = new BmsLegacySkinTransformer(new TestLegacySkin(
        [
            "Note/Note-1H",
            "mania-key1",
            "mania-key1D",
            "mania-stage-hint",
            "lightingN",
            "mania-hit300g",
        ]), beatmap);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetDrawableComponent(lookup), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget)), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, BmsLayoutVariant.Bme7K, 1)), Is.Null);
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new SkinComponentLookup<HitResult>(HitResult.Perfect)), Is.Not.Null);
    }

    [Test]
    public void TestLegacyManiaSkinReadsStageAndColourConfiguration()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };
        var skin = new BmsLegacySkinTransformer(new TestLegacySkin(), beatmap);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup))?.Value,
            Is.EqualTo(new Color4(0, 0, 0, 1)));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, Color4>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, lookup))?.Value,
            Is.EqualTo(new Color4(0, 0, 0, 0)));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value, Is.EqualTo(64));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.LightPosition))?.Value, Is.EqualTo(64));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ScorePosition))?.Value, Is.EqualTo(400));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, bool>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ShowJudgementLine))?.Value, Is.True);
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.BarLineHeight))?.Value,
            Is.EqualTo(1.2f));
    }

    [Test]
    public void TestLegacyManiaSkinUsesBmsLayoutKeyCount()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };
        var skin = new BmsLegacySkinTransformer(new TestLegacySkin(), beatmap);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("Note/Note-1H"));
    }
    [Test]
    public void TestHitWindowDefaultRankIsNormal()
    {
        // RANK 2 (Normal): perfect=18, great=40, good=100, ok/meh/miss=200
        var windows = new BmsHitWindows();
        windows.SetDifficulty(5); // OD ignored for BMS windows

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(18).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(40).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(100).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(200).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(200).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Miss), Is.EqualTo(200).Within(0.001));
    }

    [Test]
    [TestCase(0, 8,  24,  40)]   // RANK 0 - Very Hard
    [TestCase(1, 15, 30,  60)]   // RANK 1 - Hard
    [TestCase(2, 18, 40,  100)]  // RANK 2 - Normal
    [TestCase(3, 21, 60,  120)]  // RANK 3 - Easy
    [TestCase(4, 21, 60,  200)]  // RANK 4 - Very Easy
    public void TestHitWindowRank(int rank, double expectedPerfect, double expectedGreat, double expectedGood)
    {
        var windows = new BmsHitWindows(rank);
        windows.SetDifficulty(5); // OD value is irrelevant for BMS windows

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(expectedPerfect).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(expectedGreat).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(expectedGood).Within(0.001));
        // BAD (Ok), POOR (Meh), and Miss are all 200 ms for every rank
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(200).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(200).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Miss), Is.EqualTo(200).Within(0.001));
    }

    [Test]
    public void TestBmsRankParsedFromChart()
    {
        // Rank parsing is tested end-to-end via the decoder.
        // Direct BmsChartParser access is internal; see BmsBeatmapDecoderTest for coverage.
        Assert.Pass("Rank parsing is covered by BmsBeatmapDecoderTest.TestRankParsedFromChart.");
    }

    [Test]
    public void TestBmsRankDefaultWhenAbsent()
    {
        // Default rank tested end-to-end via the decoder; see BmsBeatmapDecoderTest.
        Assert.Pass("Default rank is covered by BmsBeatmapDecoderTest.TestRankDefaultsToNormalWhenAbsent.");
    }

    [Test]
    public void TestBmsRankStampedOnHitObject()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 1,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
            },
        };
        var converter = ruleset.CreateBeatmapConverter(beatmap);
        var converted = (BmsBeatmap)converter.Convert();

        Assert.That(converted.HitObjects[0].BmsRank, Is.EqualTo(1));
    }


    [Test]
    public void TestMetadata()
    {
        Assert.That(ruleset.ShortName, Is.EqualTo("bms"));
        Assert.That(ruleset.Description, Is.EqualTo("BMS Ruleset"));
        Assert.That(ruleset.RulesetAPIVersionSupported, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TestScoreProcessorBaseScoreIsPgreatTwo()
    {
        var processor = (BmsScoreProcessor)ruleset.CreateScoreProcessor();

        Assert.That(processor.GetBaseScoreForResult(HitResult.Perfect), Is.EqualTo(2));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Great), Is.EqualTo(1));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Good), Is.EqualTo(0));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Ok), Is.EqualTo(0));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Meh), Is.EqualTo(0));
        Assert.That(processor.GetBaseScoreForResult(HitResult.Miss), Is.EqualTo(0));
    }

    [Test]
    [TestCase(1.0,           ScoreRank.X)]  // All PGREAT
    [TestCase(8.0 / 9.0,    ScoreRank.S)]  // AAA
    [TestCase(7.0 / 9.0,    ScoreRank.A)]  // AA
    [TestCase(6.0 / 9.0,    ScoreRank.B)]  // A
    [TestCase(5.0 / 9.0,    ScoreRank.C)]  // B
    [TestCase(4.0 / 9.0,    ScoreRank.D)]  // below
    public void TestScoreProcessorRankFromAccuracy(double accuracy, ScoreRank expectedRank)
    {
        var processor = (BmsScoreProcessor)ruleset.CreateScoreProcessor();

        // Build result dict with just misses/perfects, using accuracy to differentiate.
        // All PGREATs (accuracy==1.0) → X; otherwise use generic non-all-perfect results.
        var results = new Dictionary<HitResult, int>();
        if (accuracy < 1.0)
            results[HitResult.Miss] = 1; // Ensures not all-PGREAT for non-X ranks.

        var rank = processor.RankFromScore(accuracy, results);

        Assert.That(rank, Is.EqualTo(expectedRank));
    }

    [Test]
    public void TestScoreProcessorRankXRequiresNoNonPgreat()
    {
        var processor = (BmsScoreProcessor)ruleset.CreateScoreProcessor();

        // Even at accuracy=1.0, if there's a GREAT it shouldn't be X.
        var resultsWithGreat = new Dictionary<HitResult, int> { [HitResult.Great] = 1 };
        var rank = processor.RankFromScore(1.0, resultsWithGreat);

        // With GREAT present at accuracy=1.0 (impossible in practice, but we test the guard)
        // the guard checks for non-perfect results; this should NOT be X.
        Assert.That(rank, Is.Not.EqualTo(ScoreRank.X));
    }

    [Test]
    public void TestGaugeInitialHealthIsTwentyPercent()
    {
        var processor = (BmsHealthProcessor)ruleset.CreateHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        Assert.That(processor.Health.Value, Is.EqualTo(0.2).Within(0.001));
    }

    [Test]
    public void TestGaugePgreatGainDrivenByTotal()
    {
        // With #TOTAL=200 and 1 note: pgreat gain = 200/100/1 = 2.0 (capped at Health.MaxValue=1).
        // We verify the computed per-note gain is TOTAL/(100*N).
        var processor = (BmsHealthProcessor)ruleset.CreateHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 200,
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // After one PGREAT the health should increase by total/100/noteCount = 200/100/1 = 2.0,
        // but Health is capped at MaxValue=1 so the observable result is 1.0.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Perfect,
        });

        Assert.That(processor.Health.Value, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void TestGaugeMissReducesHealthByFourPointEightPercent()
    {
        var processor = (BmsHealthProcessor)ruleset.CreateHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            // Small #TOTAL so PGREAT gain is negligible for this test.
            Total = 10,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        var initialHealth = processor.Health.Value;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Miss,
        });

        // Miss = −4.8%, but clamped at 0.
        var expectedHealth = Math.Max(0.0, initialHealth - 0.048);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    [Test]
    public void TestGaugeBadReducesHealthByThreePointTwoPercent()
    {
        var processor = (BmsHealthProcessor)ruleset.CreateHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 10, // Small #TOTAL so PGREAT gain is negligible.
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        var initialHealth = processor.Health.Value;
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
        {
            Type = HitResult.Ok, // BAD
        });

        // BAD = −3.2%, but clamped at 0.
        var expectedHealth = Math.Max(0.0, initialHealth - 0.032);
        Assert.That(processor.Health.Value, Is.EqualTo(expectedHealth).Within(0.001));
    }

    [Test]
    public void TestScoreProcessorBadBreaksCombo()
    {
        // BAD (Ok) must reset combo to 0 in BMS, even though HitResult.Ok.IsHit() = true in osu!.
        var processor = (BmsScoreProcessor)ruleset.CreateScoreProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
                new BmsHitObject { StartTime = 3000, Column = 3 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        // Two PGREATs → combo 2.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[1], beatmap.HitObjects[1].CreateJudgement())
            { Type = HitResult.Perfect });
        Assert.That(processor.Combo.Value, Is.EqualTo(2));

        // BAD (Ok) → combo must reset to 0.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[2], beatmap.HitObjects[2].CreateJudgement())
            { Type = HitResult.Ok });
        Assert.That(processor.Combo.Value, Is.EqualTo(0));
    }

    [Test]
    public void TestScoreProcessorPoorBreaksCombo()
    {
        // POOR (Meh) must reset combo to 0 in BMS, even though HitResult.Meh.IsHit() = true in osu!.
        var processor = (BmsScoreProcessor)ruleset.CreateScoreProcessor();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsHitObject { StartTime = 1000, Column = 1 },
                new BmsHitObject { StartTime = 2000, Column = 2 },
                new BmsHitObject { StartTime = 3000, Column = 3 },
            },
        };
        processor.ApplyBeatmap(beatmap);

        // Two PGREATs → combo 2.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[1], beatmap.HitObjects[1].CreateJudgement())
            { Type = HitResult.Perfect });
        Assert.That(processor.Combo.Value, Is.EqualTo(2));

        // POOR (Meh) → combo must reset to 0.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[2], beatmap.HitObjects[2].CreateJudgement())
            { Type = HitResult.Meh });
        Assert.That(processor.Combo.Value, Is.EqualTo(0));
    }

    [Test]
    public void TestScoreProcessorRankNeverF()
    {
        // In BMS, ScoreRank.F is never assigned from accuracy — fail is gauge-only.
        var processor = (BmsScoreProcessor)ruleset.CreateScoreProcessor();
        var results = new Dictionary<HitResult, int> { [HitResult.Miss] = 100 };

        // Even at accuracy 0 the lowest rank should be D, not F.
        var rank = processor.RankFromScore(0.0, results);
        Assert.That(rank, Is.Not.EqualTo(ScoreRank.F));
        Assert.That(rank, Is.EqualTo(ScoreRank.D));
    }

    [Test]
    public void TestGaugeClearConditionPassesAtEightyPercent()
    {
        // Normal gauge clear: ≥ 80% at song end → no failure.
        var processor = (BmsHealthProcessor)ruleset.CreateHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 400, // Large enough that one PGREAT reaches ≥ 80%.
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // One PGREAT: gain = 400/100/1 = 4.0, capped → health = 1.0.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });

        Assert.That(processor.HasFailed, Is.False);
        Assert.That(processor.Health.Value, Is.GreaterThanOrEqualTo(0.8));
    }

    [Test]
    public void TestGaugeClearConditionFailsBelowEightyPercent()
    {
        // Normal gauge clear: < 80% at last note → failure must be triggered.
        var processor = (BmsHealthProcessor)ruleset.CreateHealthProcessor(0);
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Total = 10, // Small TOTAL so PGREAT gain is negligible; health stays near initial 20%.
            HitObjects = { new BmsHitObject { StartTime = 1000, Column = 1 } },
        };
        processor.ApplyBeatmap(beatmap);

        // One PGREAT with tiny gain: health = 0.20 + 0.001 ≈ 0.201, well below 0.80.
        processor.ApplyResult(new JudgementResult(beatmap.HitObjects[0], beatmap.HitObjects[0].CreateJudgement())
            { Type = HitResult.Perfect });

        Assert.That(processor.Health.Value, Is.LessThan(0.8));
        Assert.That(processor.HasFailed, Is.True);
    }
}
