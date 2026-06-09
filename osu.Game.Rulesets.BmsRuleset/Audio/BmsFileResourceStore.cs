using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.Audio;

/// <summary>
///     An <see cref="IResourceStore{T}"/> that resolves audio files directly from the
///     original BMS chart directory on the filesystem, bypassing osu!'s Realm file storage.
/// </summary>
/// <remarks>
///     <para>
///         When resolving a sample name such as <c>wav01.wav</c>, this store reads the file from
///         <c>{basePath}/wav01.wav</c> via <see cref="File.ReadAllBytes(string)" />.
///     </para>
///     <para>
///         <b>byte[] allocation note:</b>
///         <see cref="File.ReadAllBytes" /> allocates a managed <c>byte[]</c> for the entire file
///         content, which the framework's <see cref="osu.Framework.Audio.Sample.SampleBassFactory" />
///         then copies into BASS native memory. This allocation occurs only during the preload phase
///         on the async background thread (<c>BackgroundDependencyLoader</c>)
///         — not during gameplay playback via <see cref="osu.Framework.Audio.Sample.ISample.GetChannel" />.
///     </para>
/// </remarks>
public class BmsFileResourceStore(string basePath) : IResourceStore<byte[]>
{

    public byte[] Get(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null!;

        var path = Path.Combine(basePath, name);

        if (!File.Exists(path))
            return null!;

        return File.ReadAllBytes(path);
    }

    public Task<byte[]> GetAsync(string? name, CancellationToken cancellationToken = default)
        => Task.Run(() => Get(name), cancellationToken);

    public Stream? GetStream(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var path = Path.Combine(basePath, name);

        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    public IEnumerable<string> GetAvailableResources() => [];

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
