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
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public class BmsLegacySkinTransformerTest
{
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
}
