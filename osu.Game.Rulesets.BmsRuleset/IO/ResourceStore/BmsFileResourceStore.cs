using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

public class BmsFileResourceStore(string basePath) : IResourceStore<byte[]>
{
    public void Dispose()
    {
    }

    public byte[] Get(string? name) => TryResolve(name, out var path) ? File.ReadAllBytes(path) : null!;

    public Task<byte[]> GetAsync(string? name, CancellationToken cancellationToken = default)
        => Task.Run(() => Get(name), cancellationToken);

    public Stream? GetStream(string? name)
    {
        if (!TryResolve(name, out var path))
            return null;

        return File.OpenRead(path);
    }

    public IEnumerable<string> GetAvailableResources() => [];

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="directory"/> itself or a descendant of it.
    /// Case-sensitive so a <c>../sibling</c> escape via a case-variant directory name cannot slip
    /// past on case-sensitive filesystems.
    /// </summary>
    internal static bool IsPathInsideDirectory(string path, string directory)
    {
        var directoryWithSeparator = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;

        return path.StartsWith(directoryWithSeparator, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves <paramref name="name"/> against <c>basePath</c> to a canonical full path,
    /// returning false if the result falls outside <c>basePath</c> or does not exist.
    /// </summary>
    internal bool TryResolve(string? name, out string path)
    {
        path = null!;

        if (string.IsNullOrEmpty(name))
            return false;

        try
        {
            path = Path.GetFullPath(Path.Combine(basePath, name));
        }
        catch (Exception)
        {
            // Chart-controlled value containing illegal path characters.
            path = null!;
            return false;
        }

        if (!IsPathInsideDirectory(path, basePath) || !File.Exists(path))
        {
            path = null!;
            return false;
        }

        return true;
    }
}
