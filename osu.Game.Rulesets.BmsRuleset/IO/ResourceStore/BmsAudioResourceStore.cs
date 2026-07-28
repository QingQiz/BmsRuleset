using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.Media.Audio;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Decoding;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

internal sealed class BmsAudioResourceStore(string basePath, CancellationToken cancellationToken = default) : IResourceStore<byte[]>
{
    private const int max_concurrent_flac_decodes = 4;

    private static readonly string[] fallback_extensions = ["wav", "flac", "ogg", "mp3"];

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

    public byte[] Get(string? name)
    {
        if (!fileStore.TryResolve(name, out var path))
            return null!;

        return isFlac(path) ? getDecodedWave(path, name)! : fileStore.Get(name);
    }

    public Task<byte[]> GetAsync(string? name, CancellationToken ct = default)
        => Task.Run(() => Get(name), ct);

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

            BmsAudioLogger.LogLoadFailure($"Failed to decode FLAC resource '{resourceName}': {error}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            BmsAudioLogger.LogLoadFailure($"Failed to decode FLAC resource '{resourceName}'.", exception);
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
