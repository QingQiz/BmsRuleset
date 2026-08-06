using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

public class BmsFileResourceStore(string basePath) : IResourceStore<byte[]>
{
    private static readonly Encoding shift_jis_encoding =
        CodePagesEncodingProvider.Instance.GetEncoding(932)
        ?? throw new InvalidOperationException("Shift-JIS encoding is not available.");

    private static readonly Encoding gbk_encoding = createStrictEncoding(936, "GBK");

    private static readonly ConcurrentDictionary<string, WeakReference<Lazy<MojibakeAliasIndex>>> shared_alias_indexes =
        new(StringComparer.Ordinal);

    private static readonly EnumerationOptions alias_enumeration_options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    // In-memory working beatmaps may use an empty source; resolve it as the current directory.
    private readonly string basePath = Path.GetFullPath(string.IsNullOrEmpty(basePath) ? "." : basePath);
    private Lazy<MojibakeAliasIndex>? aliasIndex;

    public void Dispose()
    {
        aliasIndex = null;
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

        if (!IsPathInsideDirectory(path, basePath))
        {
            path = null!;
            return false;
        }

        if (File.Exists(path))
            return true;

        var relativePath = normaliseSeparators(Path.GetRelativePath(basePath, path));

        if (getAliasIndex().TryResolve(relativePath, out path) && File.Exists(path))
            return true;

        path = null!;
        return false;
    }

    private MojibakeAliasIndex getAliasIndex()
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

    private static Lazy<MojibakeAliasIndex> getSharedAliasIndex(string directory)
    {
        while (true)
        {
            if (shared_alias_indexes.TryGetValue(directory, out var existing)
                && existing.TryGetTarget(out var cached))
                return cached;

            var created = new Lazy<MojibakeAliasIndex>(
                () => new MojibakeAliasIndex(directory),
                LazyThreadSafetyMode.ExecutionAndPublication);
            var replacement = new WeakReference<Lazy<MojibakeAliasIndex>>(created);

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
                ((ICollection<KeyValuePair<string, WeakReference<Lazy<MojibakeAliasIndex>>>>)shared_alias_indexes).Remove(pair);
        }
    }

    private static Encoding createStrictEncoding(int codePage, string name)
    {
        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage)
                       ?? throw new InvalidOperationException($"{name} encoding is not available.");
        encoding = (Encoding)encoding.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        return encoding;
    }

    private static string normaliseSeparators(string path) =>
        path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private sealed class MojibakeAliasIndex
    {
        private readonly Dictionary<string, string?> aliases = new(StringComparer.Ordinal);

        public MojibakeAliasIndex(string directory)
        {
            try
            {
                // Some editors write GBK resource paths into otherwise Shift-JIS charts. Building
                // aliases from real filenames remains deterministic even when decoding lost bytes.
                foreach (var file in Directory.EnumerateFiles(directory, "*", alias_enumeration_options))
                    addAlias(directory, file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An incomplete index remains safe because only unique, existing paths are returned.
            }
        }

        public bool TryResolve(string alias, out string path)
        {
            if (aliases.TryGetValue(alias, out var candidate) && candidate != null)
            {
                path = candidate;
                return true;
            }

            path = null!;
            return false;
        }

        private void addAlias(string directory, string file)
        {
            var relativePath = normaliseSeparators(Path.GetRelativePath(directory, file));
            string alias;

            try
            {
                alias = shift_jis_encoding.GetString(gbk_encoding.GetBytes(relativePath));
            }
            catch (EncoderFallbackException)
            {
                return;
            }

            if (alias == relativePath)
                return;

            if (!aliases.TryGetValue(alias, out var existing))
            {
                aliases.Add(alias, file);
                return;
            }

            if (!string.Equals(existing, file, StringComparison.Ordinal))
                aliases[alias] = null;
        }
    }
}
