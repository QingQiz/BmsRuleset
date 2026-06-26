using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     An <see cref="IResourceStore{T}"/> backed by an in-memory dictionary. Used to feed
///     BASS-rendered stretched WAV bytes back into the framework's ISampleStore so they can be
///     re-decoded into a playable ISample.
/// </summary>
public sealed class MemoryByteStore(Dictionary<string, byte[]> bytes) : IResourceStore<byte[]>
{

    #region Disposal

    public void Dispose() => GC.SuppressFinalize(this);

    #endregion

    public byte[] Get(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null!;

        return bytes.TryGetValue(name, out var b) ? b : null!;
    }

    public Task<byte[]> GetAsync(string? name, CancellationToken cancellationToken = default)
        => Task.FromResult(Get(name));

    public Stream? GetStream(string? name)
    {
        if (string.IsNullOrEmpty(name) || !bytes.TryGetValue(name, out var b))
            return null;

        // Read-only view over the shared buffer so a caller can't mutate the dict's bytes.
        return new MemoryStream(b, 0, b.Length, writable: false);
    }

    public IEnumerable<string> GetAvailableResources() => bytes.Keys;
}
