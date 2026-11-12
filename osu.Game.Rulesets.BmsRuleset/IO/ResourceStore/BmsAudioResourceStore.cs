using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Decoding;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Processing;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

internal sealed class BmsAudioResourceStore(string basePath, CancellationToken cancellationToken = default) : IDisposable
{
    private const int max_concurrent_flac_decodes = 4;

    private static readonly string[] fallback_extensions = ["wav", "flac", "ogg", "mp3", "m4a"];

    // Limit native decoder pressure when multiple samples are prefetched at once.
    private static readonly SemaphoreSlim flac_decode_semaphore = new(max_concurrent_flac_decodes, max_concurrent_flac_decodes);

    // Keep the snapshot local to playback so a later load observes filesystem changes.
    private readonly Lazy<BmsResourcePathIndex> fileIndex = new(() => new BmsResourcePathIndex(basePath, cancellationToken));
    private readonly ConcurrentDictionary<string, Lazy<byte[]?>> flacWaveCache = new(StringComparer.Ordinal);

    internal static IReadOnlyList<string> Extensions => fallback_extensions;

    internal string? ResolveSamplePath(string definition) => resolveSamplePaths(definition).FirstOrDefault();

    private IEnumerable<string> resolveSamplePaths(string definition)
    {
        var seenLookups = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lookup in new BmsSampleInfo(definition).LookupNames)
        {
            if (seenLookups.Add(lookup) && fileIndex.Value.TryResolve(lookup, out var path) && seen.Add(path))
                yield return path;

            var stem = Path.ChangeExtension(lookup, null);
            foreach (var extension in fallback_extensions)
            {
                var candidate = $"{stem}.{extension}";
                if (seenLookups.Add(candidate) && fileIndex.Value.TryResolve(candidate, out path) && seen.Add(path))
                    yield return path;
            }
        }
    }

    internal async Task<BmsFixedRatePcmProcessor> OpenPcmAsync(string definition, double rate, CancellationToken token)
    {
        Exception? failure = null;
        foreach (var path in resolveSamplePaths(definition))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var bytes = await Task.Run(() => Path.GetExtension(path).Equals(".flac", StringComparison.OrdinalIgnoreCase)
                    ? getDecodedWave(path)
                    : File.ReadAllBytes(path), token).ConfigureAwait(false);
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
    }

    private byte[]? getDecodedWave(string path)
        => flacWaveCache.GetOrAdd(path, resolvedPath => new Lazy<byte[]?>(() => decodeFlac(resolvedPath), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private byte[]? decodeFlac(string path)
    {
        var semaphoreAcquired = false;

        try
        {
            flac_decode_semaphore.Wait(cancellationToken);
            semaphoreAcquired = true;

            if (BmsFlacDecoder.TryDecodeToWave(File.ReadAllBytes(path), out var wave, out var error))
                return wave;

            BmsLogger.LogAudioFailure($"Failed to decode FLAC resource '{path}': {error}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            BmsLogger.LogAudioFailure($"Failed to decode FLAC resource '{path}'.", exception);
        }
        finally
        {
            if (semaphoreAcquired)
                flac_decode_semaphore.Release();
        }

        return null;
    }
}
