using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Dummy;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Testing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;
using osuTK.Graphics;
using Skin = osu.Game.Skinning.Skin;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsLegacySkinTransformerTest
{
    private class TestLegacySkin(IEnumerable<string> textures = null) : ISkin
    {
        private readonly HashSet<string> textures = textures?.ToHashSet() ?? [];

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

    private class TestResourceSkin(IEnumerable<string> textures) : ISkin
    {
        private readonly HashSet<string> textures = textures.ToHashSet();

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
            where TValue : notnull => null;
    }

#nullable enable
    private class TestDrawableSkin : ISkin
    {
        public Drawable? Drawable { get; init; }

        public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => Drawable;

        public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

        public ISample? GetSample(ISampleInfo sampleInfo) => null;

        public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
            where TLookup : notnull
            where TValue : notnull => null;
    }
#nullable disable

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

    private class TestStorageResourceProvider : IStorageResourceProvider
    {
        public IRenderer Renderer { get; } = new DummyRenderer();

        public AudioManager AudioManager => null;

        public IResourceStore<byte[]> Files { get; } = new ResourceStore<byte[]>();

        public IResourceStore<byte[]> Resources { get; } = new ResourceStore<byte[]>();

        public RealmAccess RealmAccess => null!;

        public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => new TextureLoaderStore(underlyingStore);
    }

    private static readonly IStorageResourceProvider storage_resources = new TestStorageResourceProvider();

    private class TestSkinIniSkin(string skinIni, IEnumerable<string> textures = null) : Skin(new SkinInfo("Test", "Test"), null, new TestByteResourceStore(new Dictionary<string, byte[]>
    {
        ["skin.ini"] = Encoding.UTF8.GetBytes(skinIni),
    }))
    {
        private readonly HashSet<string> textures = textures?.ToHashSet() ?? [];

        public override Drawable GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public override Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            if (!textures.Contains(componentName))
                return null;

            return createTexture();
        }

        public override ISample GetSample(ISampleInfo sampleInfo) => null;

        public override IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
        {
            if (lookup is SkinConfiguration.LegacySetting legacy && legacy == SkinConfiguration.LegacySetting.Version)
                return SkinUtils.As<TValue>(new Bindable<decimal>(2.7m));

            return null;
        }
    }

    private class TestLegacySkinIniSkin(string skinIni)
        : LegacySkin(new SkinInfo("Test", "Test"), null, new TestByteResourceStore(new Dictionary<string, byte[]>
        {
            ["skin.ini"] = Encoding.UTF8.GetBytes(skinIni),
        }));

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

    private static Texture createTexture()
    {
        var texture = Texture.FromStream(new DummyRenderer(),
            new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==")));
        Assert.That(texture, Is.Not.Null);
        return texture!;
    }

    private static BmsLegacySkinTransformer createConfiguredSkin(string skinIni, IEnumerable<string> textures = null,
                                                                 BmsLayoutVariant layoutVariant = BmsLayoutVariant.Bme7K, int totalColumns = 8)
        => new(new TestSkinIniSkin(skinIni, textures), createBeatmap(layoutVariant, totalColumns));

    private static IBeatmap createBeatmap(BmsLayoutVariant layoutVariant = BmsLayoutVariant.Bme7K, int totalColumns = 8)
        => new BmsBeatmap
        {
            LayoutVariant = layoutVariant,
            TotalColumns = totalColumns,
        };

    private static ISkinComponentLookup createUserMainHudLookup()
    {
        var type = typeof(Skin).Assembly.GetType("osu.Game.Skinning.UserSkinComponentLookup");
        Assert.That(type, Is.Not.Null);

        var lookup = new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.MainHUDComponents, new BmsRuleset().RulesetInfo);
        var instance = Activator.CreateInstance(type!, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [lookup], null);

        Assert.That(instance, Is.InstanceOf<ISkinComponentLookup>());
        return (ISkinComponentLookup)instance!;
    }

    [Test]
    public void TestBms5KFallsBackToSixKeySpecialStyleBeforeFiveKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 5
                                        NoteImage0: plain-5k

                                        [Mania]
                                        Keys: 6
                                        SpecialStyle: 1
                                        NoteImage1: special-6k
                                        """, layoutVariant: BmsLayoutVariant.Bms5K, totalColumns: 6);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bms5K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("special-6k"));
    }

    [Test]
    public void TestBms5KFallsBackToPlainSixKeyBeforeFiveKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 5
                                        NoteImage0: plain-5k

                                        [Mania]
                                        Keys: 6
                                        NoteImage1: plain-6k
                                        """, layoutVariant: BmsLayoutVariant.Bms5K, totalColumns: 6);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bms5K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("plain-6k"));
    }

    [Test]
    public void TestBms7KFallsBackToEightKeySpecialStyleBeforeSevenKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 7
                                        NoteImage0: plain-7k

                                        [Mania]
                                        Keys: 8
                                        SpecialStyle: 1
                                        NoteImage1: special-8k
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("special-8k"));
    }

    [Test]
    public void TestBms7KPrefersEightKeySpecialStyleOverPlainEightKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 8
                                        NoteImage1: plain-8k

                                        [Mania]
                                        Keys: 8
                                        SpecialStyle: 1
                                        NoteImage1: special-8k
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("special-8k"));
    }

    [Test]
    public void TestBms7KPrefersEightKeySpecialStyleOverPlainEightKeyFromLegacySkin()
    {
        var skin = new BmsLegacySkinTransformer(new TestLegacySkinIniSkin("""
                                                                          [Mania]
                                                                          Keys: 8
                                                                          NoteImage1: plain-8k

                                                                          [Mania]
                                                                          Keys: 8
                                                                          SpecialStyle: 1
                                                                          NoteImage1: special-8k
                                                                          """), createBeatmap());
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("special-8k"));
    }

    [Test]
    public void TestBms7KCreatesDrawableFromEightKeySpecialStyleWhenPlainEightKeyAlsoExists()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 8
                                        NoteImage1: plain-8k

                                        [Mania]
                                        Keys: 8
                                        SpecialStyle: 1
                                        NoteImage1: special-8k
                                        """, ["special-8k"]);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetDrawableComponent(lookup), Is.Not.Null);
    }

    [Test]
    public void TestBms7KTreatsEightKeySectionAsSpecialStyleWhenDefaultSpecialStyleRepeatsLater()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 8
                                        NoteImage1: plain-8k

                                        [Mania]
                                        Keys: 8
                                        SpecialStyle: 0
                                        SpecialStyle: 1
                                        HitPosition: 400
                                        NoteImage1: special-8k
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("special-8k"));
    }

    [Test]
    public void TestBms7KFallsBackToPlainEightKeyBeforeSevenKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 7
                                        NoteImage0: plain-7k

                                        [Mania]
                                        Keys: 8
                                        NoteImage1: plain-8k
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("plain-8k"));
    }

    [Test]
    public void TestBms5KDoubleFallsBackToPlainTwelveKeyBeforeTenKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 10
                                        NoteImage0: plain-10k

                                        [Mania]
                                        Keys: 12
                                        NoteImage1: plain-12k
                                        """, layoutVariant: BmsLayoutVariant.Bms5KDouble, totalColumns: 12);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bms5KDouble, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("plain-12k"));
    }

    [Test]
    public void TestBms7KDoubleFallsBackToPlainSixteenKeyBeforeFourteenKey()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 14
                                        NoteImage7: plain-14k

                                        [Mania]
                                        Keys: 16
                                        NoteImage8: plain-16k
                                        """, layoutVariant: BmsLayoutVariant.Bme7KDouble, totalColumns: 16);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7KDouble, 8);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("plain-16k"));
    }

    [Test]
    public void TestBmsHealthDisplayDoesNotInheritOsuHealthDisplay()
    {
        Assert.That(new BmsHealthDisplay(), Is.Not.InstanceOf<HealthDisplay>());
    }

    [Test]
    public void TestStageHudIsExistingLayoutOnlyComponent()
    {
        var stageHud = new BmsStageHud();

        Assert.Multiple(() =>
        {
            Assert.That(stageHud, Is.InstanceOf<ISerialisableDrawable>());
            Assert.That(((ISerialisableDrawable)stageHud).IsEditable, Is.True);
            Assert.That(SerialisedDrawableInfo.GetAllAvailableDrawables(new BmsRuleset().RulesetInfo), Does.Not.Contain(typeof(BmsStageHud)));
            Assert.That(stageHud.AutoSizeAxes, Is.EqualTo(Axes.None));
            Assert.That(stageHud.Width, Is.Zero);
            Assert.That(stageHud.Height, Is.Zero);
        });
    }

    [Test]
    public void TestBgaDisplayIsPlacedBehindNativePlayfield()
    {
        Assert.That(new BmsBgaDisplay(), Is.InstanceOf<ISerialisableDrawable>());
        Assert.That(new BmsStageHud(), Is.InstanceOf<ISerialisableDrawable>());

        var rulesetHud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.MainHUDComponents, new BmsRuleset().RulesetInfo));
        var playfieldHud = BmsDefaultHud.GetDrawableComponent(new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.Playfield, new BmsRuleset().RulesetInfo));

        Assert.That(rulesetHud, Is.Not.Null);
        Assert.That(playfieldHud, Is.Not.Null);

        // The BGA lives in the MainHUD container (skin-editable) and is rehosted behind the
        // playfield at runtime via RenderOutsideHudVisibility — not placed in the Playfield
        // container directly. The Playfield container therefore holds no BmsBgaDisplay.
        var bga = rulesetHud!.ChildrenOfType<BmsBgaDisplay>().SingleOrDefault();
        Assert.That(bga, Is.Not.Null);
        Assert.That(bga!.AutoSizeToParent, Is.True);
        Assert.That(bga.RenderOutsideHudVisibility, Is.True);
        Assert.That(bga.Depth, Is.EqualTo(float.MaxValue));

        Assert.That(rulesetHud!.ChildrenOfType<BmsStageHud>().SingleOrDefault(), Is.Not.Null);

        Assert.That(playfieldHud!.ChildrenOfType<BmsBgaDisplay>(), Is.Empty);
        Assert.That(playfieldHud.ChildrenOfType<BmsStageHud>(), Is.Empty);
    }

    [Test]
    public void TestSavedBmsHudLayoutRegeneratesMissingStageHud()
    {
        using var source = new BmsEmbeddedSkinSource();
        var savedLayout = new Container();

        source.SetSources(new TestSkinSource(new TestDrawableSkin { Drawable = savedLayout }), null);

        var hud = source.GetDrawableComponent(createUserMainHudLookup());

        Assert.That(hud!.ChildrenOfType<BmsStageHud>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void TestSavedBmsHudLayoutRemovesDuplicateStageHuds()
    {
        using var source = new BmsEmbeddedSkinSource();
        var first = new BmsStageHud();
        var second = new BmsStageHud();
        var savedLayout = new Container
        {
            Children =
            [
                first,
                second,
            ],
        };

        source.SetSources(new TestSkinSource(new TestDrawableSkin { Drawable = savedLayout }), null);

        var hud = source.GetDrawableComponent(createUserMainHudLookup());

        Assert.Multiple(() =>
        {
            Assert.That(hud!.ChildrenOfType<BmsStageHud>().Count(), Is.EqualTo(1));
            Assert.That(hud.ChildrenOfType<BmsStageHud>().Single(), Is.SameAs(first));
        });
    }

    [Test]
    public void TestBmsSkinIniImageCreatesDrawable()
    {
        var skin = createConfiguredSkin("""
                                        [BMS]
                                        Layout: 7K
                                        NoteImage1: custom-note
                                        """, ["custom-note"]);

        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
    }

    [Test]
    public void TestBmsKeyAreaDoesNotClipAtJudgeLine()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 8
                                        SpecialStyle: 1
                                        HitPosition: 347
                                        KeyImage1: custom-key
                                        """, ["custom-key"]);

        var keyArea = skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, BmsLayoutVariant.Bme7K, 1));

        Assert.That(keyArea, Is.Not.Null);
        var sprite = keyArea!.ChildrenOfType<Sprite>().Single();
        var container = keyArea!.ChildrenOfType<Container>().Single(c => c.Children.Contains(sprite));

        Assert.Multiple(() =>
        {
            Assert.That(keyArea.ChildrenOfType<Container>().Where(c => c.RelativeSizeAxes == Axes.Both && c.Padding.Bottom > 0), Is.Empty);
            Assert.That(container.Anchor, Is.EqualTo(Anchor.BottomCentre));
            Assert.That(container.Origin, Is.EqualTo(Anchor.BottomCentre));
            Assert.That(container.RelativeSizeAxes, Is.EqualTo(Axes.Both));
            Assert.That(container.Masking, Is.False);
            Assert.That(container.Height, Is.EqualTo(1));
            Assert.That(container.AutoSizeAxes, Is.EqualTo(Axes.None));
            Assert.That(container.Y, Is.Zero);
            Assert.That(sprite.Anchor, Is.EqualTo(Anchor.BottomCentre));
            Assert.That(sprite.Origin, Is.EqualTo(Anchor.BottomCentre));
            Assert.That(sprite.RelativeSizeAxes, Is.EqualTo(Axes.X));
            Assert.That(sprite.Width, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestBmsKeyAreaOverflowPlacesTallImageTopAtJudgeLine()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LegacyBmsKeyArea.CalculateBottomOverflow(40, 80), Is.Zero);
            Assert.That(LegacyBmsKeyArea.CalculateBottomOverflow(80, 80), Is.Zero);
            Assert.That(LegacyBmsKeyArea.CalculateBottomOverflow(120, 80), Is.EqualTo(40));
        });
    }

    [Test]
    public void TestBmsKeyAreaScalesImageToColumnWidth()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LegacyBmsKeyArea.CalculateColumnWidthScale(200, 50), Is.EqualTo(0.25f));
            Assert.That(LegacyBmsKeyArea.CalculateColumnWidthScale(25, 50), Is.EqualTo(2));
            Assert.That(LegacyBmsKeyArea.CalculateColumnWidthScale(0, 50), Is.EqualTo(1));
            Assert.That(LegacyBmsKeyArea.CalculateColumnWidthScale(200, 0), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestBmsSkinIniMineImageCreatesDrawable()
    {
        var skin = createConfiguredSkin("""
                                        [BMS]
                                        Layout: 7K
                                        MineImage1: custom-mine
                                        """, ["custom-mine"]);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.Hit100,
            new BmsSkinComponentLookup(BmsSkinComponents.Mine, BmsLayoutVariant.Bme7K, 1)))?.Value, Is.EqualTo("custom-mine"));
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.Mine, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
    }

    [Test]
    public void TestBmsSkinIniOverridesManiaSkinIni()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 8
                                        SpecialStyle: 1
                                        NoteImage1: mania-note

                                        [BMS]
                                        Layout: 7K
                                        NoteImage1: bms-note
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("bms-note"));
    }

    [Test]
    public void TestBuiltInTransformerDefersBmsComponentsToEmbeddedFallback()
    {
        var transformer = new BmsBuiltInSkinTransformer(new TestSkinIniSkin("""
                                                                            [BMS]
                                                                            Layout: 7K
                                                                            NoteImage1: built-in-note
                                                                            """, ["built-in-note"]));
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(transformer.GetDrawableComponent(lookup), Is.Null);
        Assert.That(transformer.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup)), Is.Null);
        Assert.That(transformer.GetDrawableComponent(new SkinComponentLookup<HitResult>(HitResult.Perfect)), Is.Null);
    }

    [Test]
    public void TestCustomSkinStopsBeforeDefaultFallbackSkin()
    {
        Assert.That(BmsEmbeddedSkinFallbackFactory.GetEmbeddedSkinKind(
        [
            new TestSkinIniSkin("""
                                [BMS]
                                Layout: 7K
                                NoteImage1: custom-note
                                """, ["custom-note"]),
            new BmsBuiltInSkinTransformer(new TrianglesSkin(TrianglesSkin.CreateInfo(), storage_resources)),
        ]), Is.EqualTo(BmsEmbeddedSkinKind.LegacyOld));
    }

    [Test]
    public void TestDefaultSkinResourcesAreEmbedded()
    {
        var resources = typeof(BmsRuleset).Assembly.GetManifestResourceNames();

        Assert.That(resources, Does.Contain("osu.Game.Rulesets.BmsRuleset.Resources.Textures.mania-key1@2x.png"));
        Assert.That(resources, Does.Contain("osu.Game.Rulesets.BmsRuleset.Resources.Skins.Modern.mania-key1.png"));
        Assert.That(resources, Does.Contain("osu.Game.Rulesets.BmsRuleset.Resources.Textures.mania-hit300g-0@2x.png"));
        Assert.That(resources, Does.Contain("osu.Game.Rulesets.BmsRuleset.Resources.Textures.lightingN@2x.png"));
        Assert.That(resources, Does.Contain("osu.Game.Rulesets.BmsRuleset.Resources.Textures.scorebar-bg@2x.png"));
    }

    [Test]
    public void TestEmbeddedFallbackProvidesCodeDefaultGameplayComponents()
    {
        using var source = new BmsEmbeddedSkinSource();
        var parent = new TestSkinSource();
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };

        source.SetSources(parent, BmsEmbeddedSkinFallbackFactory.Create(parent.AllSources, beatmap, new DummyRenderer()));

        foreach (var lookup in new[]
                 {
                     new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, BmsLayoutVariant.Bme7K, 1),
                     new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, BmsLayoutVariant.Bme7K, 1),
                     new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, BmsLayoutVariant.Bme7K, 1),
                     new BmsSkinComponentLookup(BmsSkinComponents.HitTarget),
                     new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1),
                     new BmsSkinComponentLookup(BmsSkinComponents.StageBackground),
                 })
        {
            Assert.That(((IBmsGameplaySkinDrawableSource)source).GetDrawableFactory(lookup)?.Create(), Is.Not.Null, lookup.Component.ToString());
        }
    }

    [Test]
    public void TestEmbeddedFallbackUsesLegacyForLegacyBuiltInSkins()
    {
        Assert.That(BmsEmbeddedSkinFallbackFactory.GetEmbeddedSkinKind([new BmsBuiltInSkinTransformer(new DefaultLegacySkin(DefaultLegacySkin.CreateInfo(), storage_resources))]),
            Is.EqualTo(BmsEmbeddedSkinKind.LegacyOld));
        Assert.That(BmsEmbeddedSkinFallbackFactory.GetEmbeddedSkinKind([new BmsBuiltInSkinTransformer(new RetroSkin(RetroSkin.CreateInfo(), storage_resources))]),
            Is.EqualTo(BmsEmbeddedSkinKind.LegacyOld));
    }

    [Test]
    public void TestEmbeddedFallbackUsesModernForModernBuiltInSkins()
    {
        Assert.That(BmsEmbeddedSkinFallbackFactory.GetEmbeddedSkinKind([new BmsBuiltInSkinTransformer(new ArgonSkin(ArgonSkin.CreateInfo(), storage_resources))]),
            Is.EqualTo(BmsEmbeddedSkinKind.LegacyModern));
        Assert.That(BmsEmbeddedSkinFallbackFactory.GetEmbeddedSkinKind([new BmsBuiltInSkinTransformer(new ArgonProSkin(ArgonProSkin.CreateInfo(), storage_resources))]),
            Is.EqualTo(BmsEmbeddedSkinKind.LegacyModern));
        Assert.That(BmsEmbeddedSkinFallbackFactory.GetEmbeddedSkinKind([new BmsBuiltInSkinTransformer(new TrianglesSkin(TrianglesSkin.CreateInfo(), storage_resources))]),
            Is.EqualTo(BmsEmbeddedSkinKind.LegacyModern));
    }

    [Test]
    public void TestEmbeddedLegacyOldProvidesManiaKeyTexture()
    {
        // The Classic skin has no mania-key of its own; its key area is served by the embedded
        // LegacyOld fallback, so that fallback must actually resolve the mania-key textures.
        using var skin = new BmsEmbeddedSkin(BmsEmbeddedSkinKind.LegacyOld, new DummyRenderer());
        Assert.That(skin.GetTexture("mania-key1", default, default), Is.Not.Null);
        Assert.That(skin.GetTexture("mania-key1D", default, default), Is.Not.Null);
        Assert.That(skin.GetTexture("mania-keyS", default, default), Is.Not.Null);
    }

    [Test]
    public void TestEmbeddedSkinSourceFallsBackFromModernToLegacy()
    {
        var beatmap = createBeatmap();
        using var source = new BmsEmbeddedSkinSource();
        var parent = new TestSkinSource();
        var primary = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                       [BMS]
                                                                       Layout: 7K
                                                                       NoteImage1: missing-modern-note
                                                                       """), beatmap);
        var fallback = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                        [BMS]
                                                                        Layout: 7K
                                                                        NoteImage1: legacy-note
                                                                        """, ["legacy-note"]), beatmap);

        source.SetSources(parent, new BmsEmbeddedSkinFallbackChain(primary, fallback));

        Assert.That(source.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage,
                new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)))?.Value,
            Is.EqualTo("missing-modern-note"));
        Assert.That(source.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
    }

    [Test]
    public void TestEmbeddedSkinSourceUsesEmbeddedHudBeforeParentBuiltInHud()
    {
        var beatmap = createBeatmap();
        using var source = new BmsEmbeddedSkinSource();
        var parent = new TestSkinSource(new BmsBuiltInSkinTransformer(new ArgonSkin(ArgonSkin.CreateInfo(), storage_resources)));
        var primary = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                       [BMS]
                                                                       Layout: 7K
                                                                       """), beatmap);

        source.SetSources(parent, new BmsEmbeddedSkinFallbackChain(primary, null));

        Assert.That(source.GetDrawableComponent(new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.MainHUDComponents, new BmsRuleset().RulesetInfo)), Is.Not.Null);
    }

    [Test]
    public void TestEmbeddedSkinSourceUsesParentBeforeEmbeddedFallbacks()
    {
        var beatmap = createBeatmap();
        using var source = new BmsEmbeddedSkinSource();
        var parent = new TestSkinSource(new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                                         [BMS]
                                                                                         Layout: 7K
                                                                                         NoteImage1: parent-note
                                                                                         """, ["parent-note"]), beatmap));
        var primary = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                       [BMS]
                                                                       Layout: 7K
                                                                       NoteImage1: primary-note
                                                                       """, ["primary-note"]), beatmap);
        var fallback = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                        [BMS]
                                                                        Layout: 7K
                                                                        NoteImage1: fallback-note
                                                                        """, ["fallback-note"]), beatmap);

        source.SetSources(parent, new BmsEmbeddedSkinFallbackChain(primary, fallback));

        Assert.That(source.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage,
                new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)))?.Value,
            Is.EqualTo("parent-note"));
    }

    [Test]
    public void TestGameplaySkinCacheUsesEmbeddedFallbackFactoryWhenParentMisses()
    {
        var beatmap = createBeatmap();
        using var source = new BmsEmbeddedSkinSource();
        var parent = new TestSkinSource();
        var primary = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                       [BMS]
                                                                       Layout: 7K
                                                                       NoteImage1: primary-note
                                                                       """, ["primary-note"]), beatmap);

        source.SetSources(parent, new BmsEmbeddedSkinFallbackChain(primary, null));

        using var cache = new BmsGameplaySkinCache(source);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(cache.GetDrawableFactory(lookup)?.Create(), Is.Not.Null);
    }

    [Test]
    public void TestGameplaySkinCacheUsesParentDrawableBeforeEmbeddedFallbackFactory()
    {
        var beatmap = createBeatmap();
        using var source = new BmsEmbeddedSkinSource();
        var parentDrawable = new Container();
        var parent = new TestSkinSource(new TestDrawableSkin { Drawable = parentDrawable });
        var primary = new BmsLegacySkinTransformer(new TestSkinIniSkin("""
                                                                       [BMS]
                                                                       Layout: 7K
                                                                       NoteImage1: primary-note
                                                                       """, ["primary-note"]), beatmap);

        source.SetSources(parent, new BmsEmbeddedSkinFallbackChain(primary, null));

        using var cache = new BmsGameplaySkinCache(source);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(cache.GetDrawableFactory(lookup)?.Create(), Is.SameAs(parentDrawable));
    }

    [Test]
    public void TestLegacyHudDoesNotProvideHealthDisplay()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };
        var skin = new BmsLegacySkinTransformer(new TestLegacySkin(["mania-key1"]), beatmap);
        var hud = skin.GetDrawableComponent(new GlobalSkinnableContainerLookup(GlobalSkinnableContainers.MainHUDComponents, new RulesetInfo(Constant.SHORT_NAME, "BMS", string.Empty, -1)));

        Assert.That(hud, Is.Not.Null);
        Assert.That(hud!.ChildrenOfType<HealthDisplay>(), Is.Empty);
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
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
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
    public void TestLongNoteImagesFallBackToShortNoteImage()
    {
        var skin = createConfiguredSkin("""
                                        [BMS]
                                        Layout: 7K
                                        NoteImage1: bms-note
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage, lookup))?.Value,
            Is.EqualTo("bms-note"));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, lookup))?.Value,
            Is.EqualTo("bms-note"));
        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, lookup))?.Value,
            Is.EqualTo("bms-note"));
    }

    [Test]
    public void TestMineUsesDefaultImageFallback()
    {
        var skin = new BmsLegacySkinTransformer(new TestResourceSkin(
        [
            "mania-key1",
            "mania-noteS",
        ]), new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        });

        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.Mine, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
    }

    [Test]
    public void TestPlainManiaFallbackUsesMappedColumn()
    {
        var skin = createConfiguredSkin("""
                                        [Mania]
                                        Keys: 7
                                        NoteImage0: plain-7k
                                        """);
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1);

        Assert.That(skin.GetConfig<BmsSkinConfigurationLookup, string>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteImage, lookup))?.Value,
            Is.EqualTo("plain-7k"));
    }

    [Test]
    public void TestResourceBackedDefaultSkinDoesNotRequireLegacyConfig()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
        };
        var skin = new BmsLegacySkinTransformer(new TestResourceSkin(
        [
            "mania-key1",
            "mania-key1D",
            "mania-note1",
            "mania-stage-hint",
            "mania-hit300g",
        ]), beatmap);

        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget)), Is.Not.Null);
        Assert.That(skin.GetDrawableComponent(new SkinComponentLookup<HitResult>(HitResult.Perfect)), Is.Not.Null);
    }

    [Test]
    public void TestRulesetTransformsBuiltInSkins()
    {
        var ruleset = new BmsRuleset();
        var beatmap = createBeatmap();

        Assert.That(ruleset.CreateSkinTransformer(new ArgonSkin(ArgonSkin.CreateInfo(), storage_resources), beatmap), Is.TypeOf<BmsBuiltInSkinTransformer>());
        Assert.That(ruleset.CreateSkinTransformer(new ArgonProSkin(ArgonProSkin.CreateInfo(), storage_resources), beatmap), Is.TypeOf<BmsBuiltInSkinTransformer>());
        Assert.That(ruleset.CreateSkinTransformer(new TrianglesSkin(TrianglesSkin.CreateInfo(), storage_resources), beatmap), Is.TypeOf<BmsBuiltInSkinTransformer>());
        Assert.That(ruleset.CreateSkinTransformer(new DefaultLegacySkin(DefaultLegacySkin.CreateInfo(), storage_resources), beatmap), Is.TypeOf<BmsBuiltInSkinTransformer>());
        Assert.That(ruleset.CreateSkinTransformer(new RetroSkin(RetroSkin.CreateInfo(), storage_resources), beatmap), Is.TypeOf<BmsBuiltInSkinTransformer>());
    }

    [Test]
    public void TestRulesetTransformsCurrentSkin()
    {
        var transformer = new BmsRuleset().CreateSkinTransformer(new TestSkinIniSkin("""
                                                                                     [BMS]
                                                                                     Layout: 7K
                                                                                     NoteImage1: custom-note
                                                                                     """, ["custom-note"]), createBeatmap());

        Assert.That(transformer, Is.Not.Null);
        Assert.That(transformer!.GetDrawableComponent(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1)), Is.Not.Null);
    }
}
