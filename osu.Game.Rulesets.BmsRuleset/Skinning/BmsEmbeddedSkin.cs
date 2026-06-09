using System;
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

/// <inheritdoc cref="ISkin" />
/// <summary>
/// A lightweight DLL-embedded skin that provides BMS-specific fallback textures and samples.
/// </summary>
/// <remarks>
/// This is the last-resort visual layer in the BMS skin source chain: it is only consulted
/// after the user's selected skin, <see cref="T:osu.Game.Skinning.LegacyBeatmapSkin">LegacyBeatmapSkin</see>, and all other osu!
/// skin sources have failed to satisfy a lookup.
/// <para>
/// Unlike the osu! built-in skins it deliberately does <em>not</em> inherit from
/// <see cref="T:osu.Game.Skinning.Skin">Skin</see>. This avoids reflection-based store access and prevents
/// <see cref="M:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkinSource.GetEmbeddedSkinKind(System.Collections.Generic.IEnumerable{osu.Game.Skinning.ISkin})">BmsEmbeddedSkinSource.GetEmbeddedSkinKind</see> from misidentifying it as a
/// user skin when scanning <c>AllSources</c>.
/// </para>
/// <para>
/// The class is a pure resource provider: <see cref="M:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkin.GetDrawableComponent(osu.Game.Skinning.ISkinComponentLookup)">GetDrawableComponent</see> and
/// <see cref="M:osu.Game.Rulesets.BmsRuleset.Skinning.BmsEmbeddedSkin.GetConfig``2(``0)">GetConfig{TLookup,TValue}</see> always return <c>null</c>.
/// Only textures and samples are served from the embedded store.
/// </para>
/// </remarks>
public sealed class BmsEmbeddedSkin : ISkin, IDisposable
{
    /// <summary>
    /// The raw byte store backing this skin, exposed so that callers such as
    /// <see cref="BmsLegacySkinTransformer"/> can read <c>skin.ini</c> without reflection.
    /// </summary>
    internal readonly IResourceStore<byte[]> Resources;

    private readonly TextureStore textures;
    private readonly ISampleStore? samples;

    /// <param name="kind">Which embedded asset set to load.</param>
    /// <param name="renderer">Renderer used to upload textures to the GPU.</param>
    /// <param name="audioManager">
    /// Audio manager used to open sample streams.
    /// Pass <c>null</c> in headless / test contexts where audio is unavailable.
    /// </param>
    public BmsEmbeddedSkin(BmsEmbeddedSkinKind kind, IRenderer renderer, AudioManager? audioManager)
    {
        Resources = createStore(kind);

        textures = new TextureStore(renderer, new MaxDimensionLimitedTextureLoaderStore(new TextureLoaderStore(Resources)), scaleAdjust: 1);
        samples = audioManager?.GetSampleStore(new NamespacedResourceStore<byte[]>(Resources, "Samples"));
    }

    #region Disposal

    /// <inheritdoc/>
    public void Dispose()
    {
        textures.Dispose();
        samples?.Dispose();
        Resources.Dispose();
    }

    #endregion

    /// <inheritdoc/>
    /// <remarks>Always returns <c>null</c>; this skin provides no drawable components.</remarks>
    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

    /// <inheritdoc />
    /// <summary>
    /// Returns a texture from the embedded store.
    /// Textures are stored at 2x resolution (with <c>@2x</c> suffix in the resource name)
    /// so the lookup explicitly tries the <c>@2x</c>-suffixed name first and stamps
    /// <see cref="Texture.ScaleAdjust"/> on the result so the framework renders it
    /// at the correct 1x display size.
    /// </summary>
    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
    {
        // First try @2x variant (matching the embedded resource filenames).
        var texture = textures.Get($"{componentName}@2x", wrapModeS, wrapModeT);

        if (texture != null)
        {
            texture.ScaleAdjust = 2;
            return texture;
        }

        // Fallback to plain name (for LegacyModern or any 1x texture).
        return textures.Get(componentName, wrapModeS, wrapModeT);
    }

    /// <inheritdoc/>
    public ISample? GetSample(ISampleInfo sampleInfo)
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

    /// <inheritdoc/>
    /// <remarks>Always returns <c>null</c>; this skin provides no configuration values.</remarks>
    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => null;

    private static IResourceStore<byte[]> createStore(BmsEmbeddedSkinKind kind)
    {
        var resources = new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(BmsRuleset).Assembly), "Resources");

        return kind switch
        {
            BmsEmbeddedSkinKind.LegacyModern => new NamespacedResourceStore<byte[]>(resources, "Skins/Modern"),
            _ => new NamespacedResourceStore<byte[]>(resources, "Textures"),
        };
    }
}

/// <summary>
/// Selects which set of BMS-ruleset-embedded fallback assets to use, based on
/// the aesthetic style of the user's currently active skin.
/// </summary>
public enum BmsEmbeddedSkinKind
{
    /// <summary>
    /// Classic osu!stable-style assets (loaded from <c>Resources/Textures/</c>).
    /// Used when the active user skin is <see cref="DefaultLegacySkin"/>,
    /// <see cref="RetroSkin"/>, an unrecognised <see cref="Skin"/> subclass,
    /// or when no skin is active.
    /// </summary>
    LegacyOld,

    /// <summary>
    /// Modern Argon-compatible assets (loaded from <c>Resources/Skins/Modern/</c>).
    /// Used when the active user skin is <see cref="ArgonSkin"/>,
    /// <see cref="ArgonProSkin"/>, or <see cref="TrianglesSkin"/>.
    /// </summary>
    LegacyModern,
}
