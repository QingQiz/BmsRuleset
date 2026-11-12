using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

public class BmsFileResourceStore(string basePath) : IResourceStore<byte[]>
{
    private static readonly ConcurrentDictionary<string, WeakReference<Lazy<BmsResourcePathIndex>>> shared_alias_indexes =
        new(StringComparer.Ordinal);

    // In-memory working beatmaps may use an empty source; resolve it as the current directory.
    private readonly string basePath = Path.GetFullPath(string.IsNullOrEmpty(basePath) ? "." : basePath);
    private Lazy<BmsResourcePathIndex>? aliasIndex;

    public void Dispose()
    {
        aliasIndex = null;
    }

    public byte[]? Get(string? name) => TryResolve(name, out var path) ? File.ReadAllBytes(path) : null;

    // The framework's generic store contract omits nullability for missing resources.
    byte[] IResourceStore<byte[]>.Get(string name) => Get(name)!;

    Task<byte[]> IResourceStore<byte[]>.GetAsync(string name, CancellationToken cancellationToken) => Task.Run(() => Get(name)!, cancellationToken);

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
    internal bool TryResolve(string? name, [NotNullWhen(true)] out string? path)
    {
        if (!TryGetFullPath(basePath, name, out path))
            return false;

        if (File.Exists(path))
            return true;

        var relativePath = Path.GetRelativePath(basePath, path);

        // Reinterpreting a GBK filename as Shift-JIS cannot produce an ASCII-only alias.
        if (!Ascii.IsValid(relativePath)
            && getAliasIndex().TryResolveAlias(relativePath, out path) && File.Exists(path))
            return true;

        path = null;
        return false;
    }

    internal static bool TryGetFullPath(string basePath, string? name, [NotNullWhen(true)] out string? path)
    {
        path = null;

        if (string.IsNullOrEmpty(name))
            return false;

        try
        {
            path = Path.GetFullPath(Path.Combine(basePath, name));
        }
        catch (Exception)
        {
            // Chart-controlled value containing illegal path characters.
            path = null;
            return false;
        }

        if (!IsPathInsideDirectory(path, basePath))
        {
            path = null;
            return false;
        }

        return true;
    }

    private BmsResourcePathIndex getAliasIndex()
    {
        var lazy = aliasIndex;

        if (lazy == null)
        {
            lazy = getSharedAliasIndex(basePath);
            Interlocked.CompareExchange(ref aliasIndex, lazy, null);
            lazy = aliasIndex;
        }

        return lazy.Value;
    }

    private static Lazy<BmsResourcePathIndex> getSharedAliasIndex(string directory)
    {
        while (true)
        {
            if (shared_alias_indexes.TryGetValue(directory, out var existing)
                && existing.TryGetTarget(out var cached))
                return cached;

            var created = new Lazy<BmsResourcePathIndex>(
                () => new BmsResourcePathIndex(directory),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var replacement = new WeakReference<Lazy<BmsResourcePathIndex>>(created);

            var stored = existing == null
                ? shared_alias_indexes.TryAdd(directory, replacement)
                : shared_alias_indexes.TryUpdate(directory, replacement, existing);

            if (!stored)
                continue;

            pruneDeadAliasIndexes();
            return created;
        }
    }

    private static void pruneDeadAliasIndexes()
    {
        foreach (var pair in shared_alias_indexes)
        {
            if (!pair.Value.TryGetTarget(out _))
                ((ICollection<KeyValuePair<string, WeakReference<Lazy<BmsResourcePathIndex>>>>)shared_alias_indexes).Remove(pair);
        }
    }
}
