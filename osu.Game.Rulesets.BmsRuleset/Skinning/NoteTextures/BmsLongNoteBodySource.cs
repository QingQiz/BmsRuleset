using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Embedded;
using osu.Game.Skinning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;

public enum BmsLongNoteBodyTextureKind
{
    /// <summary>
    /// Time-varying frames such as <c>mania-note1L-0..5</c>. Exactly one frame is selected at a time.
    /// </summary>
    AnimationFrames,

    /// <summary>
    /// Spatial top-to-bottom slices from one ultra-tall body image. All slices participate in tiling.
    /// </summary>
    SpatialSlices,
}

/// <summary>
/// The resolved LN body textures plus the interpretation the renderer must apply to them.
/// </summary>
public readonly record struct BmsLongNoteBodyTextureSet(Texture[] Textures, BmsLongNoteBodyTextureKind Kind);

/// <summary>
/// Resolves legacy long-note body textures and handles ultra-tall raw body slicing.
/// </summary>
/// <remarks>
/// Ultra-tall body images must be sliced from their raw PNG stream before texture upload. If they
/// are loaded through osu!'s normal texture store first, <c>MaxDimensionLimitedTextureLoaderStore</c>
/// may downscale the whole image to 8192px high and flatten rounded body endpoints.
/// </remarks>
public static class BmsLongNoteBodySource
{
    private const float max_source_slice_height = 1024;

    private static readonly FieldInfo? skin_store_field = typeof(Skin).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly Dictionary<RawSliceCacheKey, Texture[]> raw_slice_cache = new();
    private static readonly object raw_slice_cache_lock = new();

    public static void ClearCache()
    {
        // SourceChanged means a user skin or embedded fallback changed. Raw slices are renderer-owned
        // textures, so keep the cache coarse and invalidate all entries rather than trying to track
        // per-file lifetimes through skin-source wrappers.
        lock (raw_slice_cache_lock)
            raw_slice_cache.Clear();
    }

    public static BmsLongNoteBodyTextureSet? Resolve(ISkinSource skin, BmsSkinComponentLookup lookup, IRenderer renderer)
    {
        foreach (var candidate in BmsLegacyTextureResolver.HoldBodyImageCandidates(skin, lookup).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct())
        {
            // Try raw bytes before normal GetTextures(). This is the critical Argon/ultra-tall path:
            // normal texture stores may apply maximum-dimension scaling before the renderer sees the
            // image, which compresses rounded endpoints and makes LN bodies look flattened.
            var rawSlices = getRawBodySlices(skin, candidate!, renderer);

            if (rawSlices.Length > 0)
                return new BmsLongNoteBodyTextureSet(rawSlices, BmsLongNoteBodyTextureKind.SpatialSlices);

            // If no raw ultra-tall image exists, use normal legacy animation lookup. For classic skins
            // suffixes like -0..5 are hold-animation frames, not spatial body segments.
            var frames = skin.GetTextures(candidate!, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, "-", null, out _)
                .Where(t => t.DisplayWidth > 0 && t.DisplayHeight > 0)
                .ToArray();

            if (frames.Length > 0)
                return new BmsLongNoteBodyTextureSet(frames, BmsLongNoteBodyTextureKind.AnimationFrames);
        }

        return null;
    }

    private static Texture[] getRawBodySlices(ISkinSource skin, string candidate, IRenderer renderer)
    {
        var rawResource = findRawTextureResource(skin, candidate);

        if (rawResource == null)
            return [];

        var key = new RawSliceCacheKey(rawResource.Value.Provider, renderer, rawResource.Value.ResourceName);

        lock (raw_slice_cache_lock)
        {
            if (raw_slice_cache.TryGetValue(key, out var cached))
                return cached;
        }

        var decoded = decodeRawBodySlices(rawResource.Value.Store, rawResource.Value.ResourceName, renderer);

        lock (raw_slice_cache_lock)
            raw_slice_cache[key] = decoded;

        return decoded;
    }

    private static Texture[] decodeRawBodySlices(IResourceStore<byte[]> store, string resourceName, IRenderer renderer)
    {
        using var stream = store.GetStream(resourceName);

        if (stream == null)
            return [];

        using var image = Image.Load<Rgba32>(stream);

        if (image.Width <= 0 || image.Height <= max_source_slice_height)
            return [];

        // Split evenly so no uploaded texture exceeds the safe source-slice height. We don't stretch
        // here; display scaling is handled later by BmsSegmentedLongNoteBody based on lane width.
        var count = Math.Max(1, (int)Math.Ceiling(image.Height / max_source_slice_height));
        var result = new Texture[count];

        for (var i = 0; i < count; i++)
        {
            var y = image.Height * i / count;
            var nextY = image.Height * (i + 1) / count;
            var w = image.Width;
            using var slice = image.Clone(ctx => ctx.Crop(new Rectangle(0, y, w, nextY - y)));
            var upload = new TextureUpload(slice.Clone());
            var texture = renderer.CreateTexture(upload.Width, upload.Height, wrapModeS: WrapMode.ClampToEdge, wrapModeT: WrapMode.ClampToEdge);
            texture.SetData(upload);
            result[i] = texture;
        }

        return result;
    }

    private static (ISkin Provider, IResourceStore<byte[]> Store, string ResourceName)? findRawTextureResource(ISkinSource skin, string candidate)
    {
        // ISkinSource may wrap skins in transformers. Raw bytes live on the concrete provider, so
        // unwrap transformers before checking BmsEmbeddedSkin.Resources or Skin's private store.
        foreach (var provider in skin.AllSources.Select(unwrap))
        {
            if (rawStoreFor(provider) is not { } store)
                continue;

            foreach (var resourceName in textureStreamCandidates(candidate))
            {
                using var stream = store.GetStream(resourceName);

                if (stream != null)
                    return (provider, store, resourceName);
            }
        }

        return null;
    }

    private static IResourceStore<byte[]>? rawStoreFor(ISkin? provider) => provider switch
    {
        BmsEmbeddedSkin embedded => embedded.Resources,
        // osu.Game.Skinning.Skin keeps the resource store private. This reflection is intentionally
        // localised here so the rendering path can support user skins without special-casing only
        // the embedded skin provider.
        Skin concreteSkin when skin_store_field?.GetValue(concreteSkin) is IResourceStore<byte[]> store => store,
        _ => null,
    };

    private static ISkin unwrap(ISkin provider)
    {
        while (provider is ISkinTransformer transformer)
            provider = transformer.Skin;

        return provider;
    }

    private static IEnumerable<string> textureStreamCandidates(string candidate)
    {
        // Legacy texture lookup accepts names both with and without extension / @2x. Raw store lookup
        // has to try concrete filenames explicitly and prefers @2x for parity with normal texture load.
        // ReSharper disable once InconsistentNaming
        var without2x = candidate.Replace("@2x", string.Empty);
        var extension = Path.GetExtension(without2x);
        var baseName = Path.ChangeExtension(without2x, null);

        if (string.IsNullOrEmpty(extension))
        {
            yield return $"{baseName}@2x.png";
            yield return $"{baseName}.png";
        }
        else
        {
            yield return $"{baseName}@2x{extension}";
            yield return without2x;
        }
    }

    private sealed class RawSliceCacheKey(ISkin provider, IRenderer renderer, string resourceName)
        : IEquatable<RawSliceCacheKey>
    {
        private readonly ISkin provider = provider;
        private readonly IRenderer renderer = renderer;
        private readonly string resourceName = resourceName;

        public bool Equals(RawSliceCacheKey? other) => other != null
                                                       && ReferenceEquals(provider, other.provider)
                                                       && ReferenceEquals(renderer, other.renderer)
                                                       && resourceName == other.resourceName;

        public override bool Equals(object? obj) => obj is RawSliceCacheKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(provider), RuntimeHelpers.GetHashCode(renderer), resourceName);
    }
}
