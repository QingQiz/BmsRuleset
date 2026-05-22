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
using osu.Game.Rulesets.Scoring;
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
    public void TestMetadata()
    {
        Assert.That(ruleset.ShortName, Is.EqualTo("bms"));
        Assert.That(ruleset.Description, Is.EqualTo("BMS Ruleset"));
        Assert.That(ruleset.RulesetAPIVersionSupported, Is.Not.Null.And.Not.Empty);
    }
}
