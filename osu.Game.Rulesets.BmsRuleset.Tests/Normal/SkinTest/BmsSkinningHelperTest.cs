using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Skinning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Skin = osu.Game.Skinning.Skin;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsSkinningHelperTest
{
    private readonly DummyRenderer renderer = new();

    private static byte[] createPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private class TestSkin(DummyRenderer renderer) : ISkin
    {
        public Dictionary<string, (int Width, int Height)> TextureSizes { get; } = new();

        public Dictionary<LegacyManiaSkinConfigurationLookups, string> StringConfigs { get; } = new();

        public Dictionary<LegacyManiaSkinConfigurationLookups, float> FloatConfigs { get; } = new();

        public virtual Drawable GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public virtual Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            if (!TextureSizes.TryGetValue(componentName, out var size))
                return null;

            return renderer.CreateTexture(size.Width, size.Height, false, TextureFilteringMode.Linear, wrapModeS, wrapModeT, null);
        }

        public virtual ISample GetSample(ISampleInfo sampleInfo) => null;

        public virtual IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull
        {
            if (lookup is BmsSkinConfigurationLookup bmsLookup)
            {
                if (typeof(TValue) == typeof(string) && StringConfigs.TryGetValue(bmsLookup.Lookup, out var text))
                    return SkinUtils.As<TValue>(new Bindable<string>(text));

                if (typeof(TValue) == typeof(float) && FloatConfigs.TryGetValue(bmsLookup.Lookup, out var number))
                    return SkinUtils.As<TValue>(new Bindable<float>(number));

            }

            return null;
        }
    }

    private class CountingTextureSkin(DummyRenderer renderer) : TestSkin(renderer)
    {

        public List<string> TextureLookupNames { get; } = [];

        public int TextureLookups { get; private set; }

        public override Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            TextureLookups++;
            TextureLookupNames.Add(componentName);
            return base.GetTexture(componentName, wrapModeS, wrapModeT);
        }
    }

    private class CountingComponentSkin : ISkin
    {
        public int DrawableLookups { get; private set; }

        public Drawable GetDrawableComponent(ISkinComponentLookup lookup)
        {
            DrawableLookups++;
            return new Container();
        }

        public Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

        public ISample GetSample(ISampleInfo sampleInfo) => null;

        public IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull => null;
    }

    private class LiveLegacyConfigSkin(DummyRenderer renderer) : TestSkin(renderer)
    {
        public Bindable<float> ExplosionScale { get; } = new(1);

        public override IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
        {
            if (lookup is LegacyManiaSkinConfigurationLookup legacyLookup
                && legacyLookup.Lookup == LegacyManiaSkinConfigurationLookups.ExplosionScale)
            {
                return SkinUtils.As<TValue>(ExplosionScale);
            }

            return base.GetConfig<TLookup, TValue>(lookup);
        }
    }

    private class CountingDrawableSkinSource : ISkinSource, IBmsGameplaySkinDrawableSource
    {

        public IEnumerable<ISkin> AllSources => [];

        public int FactoryLookups { get; private set; }

        public BmsResolvedDrawableFactory GetDrawableFactory(BmsSkinComponentLookup lookup)
        {
            FactoryLookups++;
            return new BmsResolvedDrawableFactory(() => new Container());
        }

        public Drawable GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

        public ISample GetSample(ISampleInfo sampleInfo) => null;

        public IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull => null;

        public ISkin FindProvider(Func<ISkin, bool> lookupFunction) => null;

        public void TriggerSourceChanged() => SourceChanged.Invoke();

        public event Action SourceChanged = delegate { };
    }

    private class TestRawSkin(DummyRenderer renderer, Dictionary<string, byte[]> resources) : Skin(new SkinInfo("Test", "Test"), null, new TestByteResourceStore(resources))
    {

        public Dictionary<string, (int Width, int Height)> TextureSizes => skin.TextureSizes;

        public Dictionary<LegacyManiaSkinConfigurationLookups, string> StringConfigs => skin.StringConfigs;

        private readonly TestSkin skin = new(renderer);

        public override Drawable GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public override Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => skin.GetTexture(componentName, wrapModeS, wrapModeT);

        public override ISample GetSample(ISampleInfo sampleInfo) => null;

        public override IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup) => skin.GetConfig<TLookup, TValue>(lookup);
    }

    private class TestSkinSource(params ISkin[] skins) : ISkinSource
    {
        public IEnumerable<ISkin> AllSources => skins;

        public Drawable GetDrawableComponent(ISkinComponentLookup lookup)
        {
            foreach (var skin in skins)
            {
                if (skin.GetDrawableComponent(lookup) is { } drawable)
                    return drawable;
            }

            return null;
        }

        public Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            foreach (var skin in skins)
            {
                if (skin.GetTexture(componentName, wrapModeS, wrapModeT) is { } texture)
                    return texture;
            }

            return null;
        }

        public ISample GetSample(ISampleInfo sampleInfo)
        {
            foreach (var skin in skins)
            {
                if (skin.GetSample(sampleInfo) is { } sample)
                    return sample;
            }

            return null;
        }

        public IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull
        {
            foreach (var skin in skins)
            {
                if (skin.GetConfig<TLookup, TValue>(lookup) is { } config)
                    return config;
            }

            return null;
        }

        public ISkin FindProvider(Func<ISkin, bool> lookupFunction) => skins.FirstOrDefault(lookupFunction);

        public event Action SourceChanged
        {
            add { }
            remove { }
        }
    }

    private class TestByteResourceStore(Dictionary<string, byte[]> resources) : IResourceStore<byte[]>
    {

        #region Disposal

        public void Dispose()
        {
        }

        #endregion

        public byte[] Get(string name) => resources.GetValueOrDefault(name);

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public Stream GetStream(string name) => Get(name) is { } bytes ? new MemoryStream(bytes) : null;

        public IEnumerable<string> GetAvailableResources() => resources.Keys;
    }

    [Test]
    public void TestExplosionFactoryReadsLiveConfigWhenCreatingDrawable()
    {
        var skin = new LiveLegacyConfigSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-key1"] = (1, 1),
                ["lightingN"] = (20, 20),
            },
        };
        var transformer = new BmsLegacySkinTransformer(skin, new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        });
        var factory = ((IBmsGameplaySkinDrawableSource)transformer).GetDrawableFactory(
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1));

        skin.ExplosionScale.Value = 2;

        Assert.That(((LegacyBmsHitExplosion)factory!.Create()!).ResolvedScale, Is.EqualTo(2));
    }

    [Test]
    public void TestGameplaySkinCacheReusesResolvedDrawableFactoryUntilSourceChanges()
    {
        var skin = new CountingDrawableSkinSource();
        using var cache = new BmsGameplaySkinCache(skin);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(cache.GetDrawableFactory(lookup)!.Create(), Is.Not.Null);
        Assert.That(cache.GetDrawableFactory(lookup)!.Create(), Is.Not.Null);
        Assert.That(skin.FactoryLookups, Is.EqualTo(1));

        skin.TriggerSourceChanged();

        Assert.That(cache.GetDrawableFactory(lookup)!.Create(), Is.Not.Null);
        Assert.That(skin.FactoryLookups, Is.EqualTo(2));
    }

    [Test]
    public void TestGameplaySkinCacheReusesResolvedLongNoteBodyTextures()
    {
        var skin = new CountingTextureSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-note1L-0"] = (20, 100),
                ["mania-note1L-1"] = (20, 120),
            },
        };
        var source = new TestSkinSource(skin);
        using var cache = new BmsGameplaySkinCache(source);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, BmsLayoutVariant.Bme7K, 1);

        Assert.That(cache.GetLongNoteBodyTextureSet(lookup, renderer), Is.Not.Null);
        var lookupsAfterFirstResolve = skin.TextureLookups;

        Assert.That(cache.GetLongNoteBodyTextureSet(lookup, renderer), Is.Not.Null);
        Assert.That(skin.TextureLookups, Is.EqualTo(lookupsAfterFirstResolve));
    }

    [Test]
    public void TestGameplaySkinCacheReusesResolvedNoteHeight()
    {
        var skin = new CountingTextureSkin(renderer)
        {
            StringConfigs = { [LegacyManiaSkinConfigurationLookups.NoteImage] = "custom-note" },
            TextureSizes =
            {
                ["custom-note"] = (20, 10),
            },
        };
        var source = new TestSkinSource(skin);
        using var cache = new BmsGameplaySkinCache(source);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(cache.GetNoteHeight(lookup, 80), Is.EqualTo(40));
        var lookupsAfterFirstResolve = skin.TextureLookups;

        Assert.That(cache.GetNoteHeight(lookup, 80), Is.EqualTo(40));
        Assert.That(skin.TextureLookups, Is.EqualTo(lookupsAfterFirstResolve));
    }

    [Test]
    public void TestGameplaySkinCacheUsesTailTextureForTailHeight()
    {
        var skin = new CountingTextureSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-note1"] = (20, 10),
                ["mania-note1T"] = (20, 40),
            },
        };
        var source = new TestSkinSource(skin);
        using var cache = new BmsGameplaySkinCache(source);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, BmsLayoutVariant.Bme7K, 1);

        Assert.That(cache.GetNoteHeight(lookup, 20), Is.EqualTo(40));
    }

    [Test]
    public void TestGameplaySkinCacheWarmsLongNoteTexturesForBeatmapColumns()
    {
        var skin = new CountingTextureSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-key1"] = (20, 20),
                ["mania-note1H"] = (20, 20),
                ["mania-note1L-0"] = (20, 100),
                ["mania-note1T"] = (20, 20),
                ["mania-note2H"] = (20, 20),
                ["mania-note2L-0"] = (20, 100),
                ["mania-note2T"] = (20, 20),
            },
        };
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsLongNote { Column = 1, StartTime = 1000, Duration = 500 },
                new BmsNote { Column = 2, StartTime = 1000 },
            },
        };
        var source = new TestSkinSource(new BmsLegacySkinTransformer(skin, beatmap));
        using var cache = new BmsGameplaySkinCache(source);

        cache.WarmLongNoteTextures(beatmap, renderer);

        Assert.That(skin.TextureLookupNames, Does.Contain("mania-note1H"));
        Assert.That(skin.TextureLookupNames, Does.Contain("mania-note1L-0"));
        Assert.That(skin.TextureLookupNames, Does.Contain("mania-note1T"));
        Assert.That(skin.TextureLookupNames, Does.Not.Contain("mania-note2H"));
        Assert.That(skin.TextureLookupNames, Does.Not.Contain("mania-note2L-0"));
        Assert.That(skin.TextureLookupNames, Does.Not.Contain("mania-note2T"));
    }

    [Test]
    public void TestGenericDrawableFallbackDoesNotCreateDrawableUntilFactoryCreate()
    {
        var skin = new CountingComponentSkin();
        var source = new TestSkinSource(skin);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        var factory = BmsGameplaySkinDrawableResolver.Resolve(source, lookup);

        Assert.That(skin.DrawableLookups, Is.EqualTo(0));
        Assert.That(factory.Create(), Is.Not.Null);
        Assert.That(skin.DrawableLookups, Is.EqualTo(1));
    }

    [Test]
    public void TestHoldBodyCandidatesSkipBodyWhenItIsShortNoteFallback()
    {
        var skin = new TestSkin(renderer)
        {
            StringConfigs =
            {
                [LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage] = "note",
                [LegacyManiaSkinConfigurationLookups.NoteImage] = "note",
            },
        };
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, BmsLayoutVariant.Bme7K, 1);

        Assert.That(BmsLegacyTextureResolver.HoldBodyImageCandidates(skin, lookup).Where(c => c != null),
            Is.EqualTo(["mania-note1L", "note"]));
    }

    [Test]
    public void TestHoldHeadTexturePrefersConfiguredHeadThenLegacyFallback()
    {
        var skin = new TestSkin(renderer)
        {
            TextureSizes =
            {
                // Only "head" has a texture — the first configured candidate wins.
                ["head"] = (20, 10),
            },
            StringConfigs =
            {
                [LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage] = "head",
                [LegacyManiaSkinConfigurationLookups.NoteImage] = "note",
            },
        };
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteHead, BmsLayoutVariant.Bme7K, 1);

        var textures = BmsLegacyTextureResolver.ResolveNoteTextures(skin, lookup);
        Assert.That(textures, Has.Length.EqualTo(1));
        Assert.That(textures[0].DisplayWidth, Is.EqualTo(20));
    }

    [Test]
    public void TestLegacyTransformerCreatesResolvedNoteAndLegacyExplosionDrawables()
    {
        var skin = new CountingTextureSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-key1"] = (1, 1),
                ["mania-note1"] = (20, 10),
                ["lightingN"] = (40, 30),
            },
        };

        var transformer = new BmsLegacySkinTransformer(skin, new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        });

        var noteFactory = ((IBmsGameplaySkinDrawableSource)transformer).GetDrawableFactory(
            new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1));
        var explosionFactory = ((IBmsGameplaySkinDrawableSource)transformer).GetDrawableFactory(
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1));

        Assert.That(noteFactory!.Create(), Is.TypeOf<BmsResolvedNotePiece>());
        Assert.That(explosionFactory!.Create(), Is.TypeOf<LegacyBmsHitExplosion>());
    }

    [Test]
    public void TestLegacyTransformerDrawableFactoryReturnsNullForUnsupportedComponents()
    {
        var skin = new CountingTextureSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-key1"] = (1, 1),
            },
        };
        var transformer = new BmsLegacySkinTransformer(skin, new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        });

        Assert.DoesNotThrow(() =>
        {
            Assert.That(((IBmsGameplaySkinDrawableSource)transformer).GetDrawableFactory(
                new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, BmsLayoutVariant.Bme7K, 1)), Is.Null);
        });
    }

    [Test]
    public void TestLongNoteBodySourceSlicesUltraTallRawResourceBeforeTextureLookup()
    {
        var rawSkin = new TestRawSkin(renderer, new Dictionary<string, byte[]>
        {
            ["note-ln-body.png"] = createPng(2, 2050),
        })
        {
            TextureSizes = { ["note-ln-body"] = (2, 2050) },
            StringConfigs = { [LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage] = "note-ln-body" },
        };
        var source = new TestSkinSource(rawSkin);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, BmsLayoutVariant.Bme7K, 1);
        using var cache = new BmsLongNoteBodySource.BmsLongNoteBodyTextureCache();

        var resolved = BmsLongNoteBodySource.Resolve(source, lookup, renderer, cache);

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Value.Kind, Is.EqualTo(BmsLongNoteBodyTextureKind.SpatialSlices));
        Assert.That(resolved.Value.Textures.Select(t => t.Height), Is.EqualTo([683, 683, 684]));
    }

    [Test]
    public void TestLongNoteBodySourceUsesAnimationFramesWhenNoRawSliceExists()
    {
        var skin = new TestSkin(renderer)
        {
            TextureSizes =
            {
                ["mania-note1L-0"] = (2, 10),
                ["mania-note1L-1"] = (2, 11),
            },
        };
        var source = new TestSkinSource(skin);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, BmsLayoutVariant.Bme7K, 1);
        using var cache = new BmsLongNoteBodySource.BmsLongNoteBodyTextureCache();

        var resolved = BmsLongNoteBodySource.Resolve(source, lookup, renderer, cache);

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved!.Value.Kind, Is.EqualTo(BmsLongNoteBodyTextureKind.AnimationFrames));
        Assert.That(resolved.Value.Textures.Select(t => t.Height), Is.EqualTo([10, 11]));
    }

    [Test]
    public void TestMineTextureUsesHit100ThenSpecialNoteFallback()
    {
        var skin = new TestSkin(renderer)
        {
            TextureSizes =
            {
                // Only "mine" has a texture — the configured Hit100 name wins.
                ["mine"] = (10, 10),
            },
            StringConfigs = { [LegacyManiaSkinConfigurationLookups.Hit100] = "mine" },
        };
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Mine, BmsLayoutVariant.Bme7K, 1);

        var textures = BmsLegacyTextureResolver.ResolveNoteTextures(skin, lookup);
        Assert.That(textures, Has.Length.EqualTo(1));
        Assert.That(textures[0].DisplayWidth, Is.EqualTo(10));
    }

    [Test]
    public void TestNoteSizingFallsBackToConfiguredReferenceWidthWhenTextureMissing()
    {
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);
        var skin = new TestSkinSource(new TestSkin(renderer)
        {
            FloatConfigs = { [LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale] = 30 },
        });

        Assert.That(BmsGameplaySkinMetricsResolver.ResolveNoteHeight(skin, lookup, 80), Is.EqualTo(30));
    }

    [Test]
    public void TestNoteSizingUsesConfiguredReferenceWidthNotDrawWidth()
    {
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);
        var skin = new TestSkinSource(new TestSkin(renderer)
        {
            TextureSizes = { ["custom-note"] = (20, 10) },
            StringConfigs = { [LegacyManiaSkinConfigurationLookups.NoteImage] = "custom-note" },
            FloatConfigs = { [LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale] = 30 },
        });

        Assert.That(BmsGameplaySkinMetricsResolver.ResolveNoteHeight(skin, lookup, 80), Is.EqualTo(15));
    }

    [Test]
    public void TestNoteSizingUsesDefaultWhenNoTextureOrReferenceWidthExists()
    {
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(BmsGameplaySkinMetricsResolver.ResolveNoteHeight(new TestSkinSource(new TestSkin(renderer)), lookup, 80), Is.EqualTo(BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT));
    }

    [Test]
    public void TestTextureResolverMapsColumnsByDistanceToStageEdge()
    {
        Assert.That(BmsLegacyTextureResolver.FallbackColumnIndex(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)), Is.EqualTo("1"));
        Assert.That(BmsLegacyTextureResolver.FallbackColumnIndex(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 2)), Is.EqualTo("2"));
        Assert.That(BmsLegacyTextureResolver.FallbackColumnIndex(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7KDouble, 8)), Is.EqualTo("1"));
    }

    [Test]
    public void TestTextureResolverMapsScratchToSpecialFallback()
    {
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 0);

        Assert.That(BmsLegacyTextureResolver.FallbackColumnIndex(lookup), Is.EqualTo("S"));
    }
}
