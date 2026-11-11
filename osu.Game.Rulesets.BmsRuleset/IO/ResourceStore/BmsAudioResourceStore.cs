using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Decoding;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

internal sealed class BmsAudioResourceStore(string basePath, CancellationToken cancellationToken = default) : IResourceStore<byte[]>
{
    private const int max_concurrent_flac_decodes = 4;

    private static readonly string[] fallback_extensions = ["wav", "flac", "ogg", "mp3", "m4a"];

    // Limit native decoder pressure when multiple samples are prefetched at once.
    private static readonly SemaphoreSlim flac_decode_semaphore = new(max_concurrent_flac_decodes, max_concurrent_flac_decodes);

    private readonly BmsFileResourceStore fileStore = new(basePath);
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> flacWaveCache = new(StringComparer.Ordinal);

    internal static IReadOnlyList<string> Extensions => fallback_extensions;

    public static void AddExtensions(ResourceStore<byte[]> resources)
    {
        foreach (var extension in fallback_extensions)
            resources.AddExtension(extension);
    }

    public byte[]? Get(string? name)
    {
        if (!fileStore.TryResolve(name, out var path))
            return null;

        return isFlac(path) ? getDecodedWave(path, name) : fileStore.Get(name);
    }

    public Task<byte[]?> GetAsync(string? name, CancellationToken ct = default)
        => Task.Run(() => Get(name), ct);

    // The framework's generic store contract omits nullability for missing resources.
    byte[] IResourceStore<byte[]>.Get(string name) => Get(name)!;

    Task<byte[]> IResourceStore<byte[]>.GetAsync(string name, CancellationToken cancellationToken) => GetAsync(name, cancellationToken)!;

    public Stream? GetStream(string? name)
    {
        if (!fileStore.TryResolve(name, out var path))
            return null;

        if (!isFlac(path))
            return fileStore.GetStream(name);

        var waveData = getDecodedWave(path, name);
        return waveData == null ? null : new MemoryStream(waveData, writable: false);
    }

    public IEnumerable<string> GetAvailableResources() => [];

    internal bool TryResolve(string? name, [NotNullWhen(true)] out string? path) => fileStore.TryResolve(name, out path);

    internal string? ResolveSamplePath(string definition) => ResolveSamplePaths(definition).FirstOrDefault();

    private IEnumerable<string> ResolveSamplePaths(string definition)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lookup in new BmsSampleInfo(definition).LookupNames)
        {
            if (TryResolve(lookup, out var path) && seen.Add(path))
                yield return path;
            var stem = Path.ChangeExtension(lookup, null);
            foreach (var extension in fallback_extensions)
                if (TryResolve($"{stem}.{extension}", out path) && seen.Add(path))
                    yield return path;
        }
    }

    internal async Task<BmsFixedRatePcmProcessor> OpenPcmAsync(string definition, double rate, CancellationToken token)
    {
        Exception? failure = null;
        foreach (var path in ResolveSamplePaths(definition))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var bytes = await GetAsync(path, token).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                    return BmsFixedRatePcmProcessor.CreateFromMemory(bytes, rate);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }
        throw new InvalidDataException($"No playable audio resource for '{definition}'.", failure);
    }

    public void Dispose()
    {
        flacWaveCache.Clear();
        fileStore.Dispose();
    }

    private byte[]? getDecodedWave(string path, string? resourceName)
        => flacWaveCache.GetOrAdd(path, resolvedPath => new Lazy<byte[]?>(() => decodeFlac(resolvedPath, resourceName), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private byte[]? decodeFlac(string path, string? resourceName)
    {
        var semaphoreAcquired = false;

        try
        {
            flac_decode_semaphore.Wait(cancellationToken);
            semaphoreAcquired = true;

            if (BmsFlacDecoder.TryDecodeToWave(File.ReadAllBytes(path), out var wave, out var error))
                return wave;

            BmsLogger.LogAudioFailure($"Failed to decode FLAC resource '{resourceName}': {error}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            BmsLogger.LogAudioFailure($"Failed to decode FLAC resource '{resourceName}'.", exception);
        }
        finally
        {
            if (semaphoreAcquired)
                flac_decode_semaphore.Release();
        }

        return null;
    }

    private static bool isFlac(string path) => Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase);
}
