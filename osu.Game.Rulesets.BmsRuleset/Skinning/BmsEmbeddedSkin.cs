using System.Reflection;
using System.IO;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public sealed class BmsEmbeddedSkin : Skin
{
    private static readonly FieldInfo? skin_store_field = typeof(Skin).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly TextureStore textures;
    private readonly ISampleStore? samples;

    public BmsEmbeddedSkin(BmsEmbeddedSkinKind skin, IRenderer renderer, AudioManager? audioManager)
        : base(new SkinInfo($"BMS {skin}", "BMS Ruleset"), null, createStore(skin))
    {
        var resources = (IResourceStore<byte[]>)skin_store_field!.GetValue(this)!;

        textures = new TextureStore(renderer, new TextureLoaderStore(resources), scaleAdjust: 1);
        samples = audioManager?.GetSampleStore(new NamespacedResourceStore<byte[]>(resources, @"Samples"));
    }

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

    public override Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
    {
        componentName = componentName.Replace(@"@2x", string.Empty);

        var texture = textures.Get($"{Path.ChangeExtension(componentName, null)}@2x{Path.GetExtension(componentName)}", wrapModeS, wrapModeT);

        if (texture != null)
        {
            texture.ScaleAdjust = 2;
            return texture;
        }

        return textures.Get(componentName, wrapModeS, wrapModeT);
    }

    public override ISample? GetSample(ISampleInfo sampleInfo)
    {
        if (samples == null)
            return null;

        foreach (var lookup in sampleInfo.LookupNames)
        {
            var sample = samples.Get(lookup);

            if (sample != null)
                return sample;
        }

        return null;
    }

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
    {
        if (lookup is SkinConfiguration.LegacySetting legacy && legacy == SkinConfiguration.LegacySetting.Version)
            return SkinUtils.As<TValue>(new Bindable<decimal>(2.7m));

        return null;
    }

    protected override void Dispose(bool isDisposing)
    {
        textures.Dispose();
        samples?.Dispose();

        base.Dispose(isDisposing);
    }

    private static IResourceStore<byte[]> createStore(BmsEmbeddedSkinKind skin)
    {
        var resources = new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(BmsRuleset).Assembly), "Resources");

        return skin switch
        {
            BmsEmbeddedSkinKind.Modern => new NamespacedResourceStore<byte[]>(resources, "Skins/Modern"),
            _ => new NamespacedResourceStore<byte[]>(resources, "Textures"),
        };
    }
}

public enum BmsEmbeddedSkinKind
{
    Legacy,
    Modern,
}
