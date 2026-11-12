using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;

namespace osu.Game.Rulesets.BmsRuleset.IO.ResourceStore;

internal sealed class BmsResourcePathIndex
{
    private static readonly Encoding shift_jis_encoding =
        CodePagesEncodingProvider.Instance.GetEncoding(932)
        ?? throw new InvalidOperationException("Shift-JIS encoding is not available.");

    private static readonly Encoding gbk_encoding =
        CodePagesEncodingProvider.Instance.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("GBK encoding is not available.");

    private readonly string directory;
    private readonly string directoryPrefix;
    private readonly Dictionary<string, List<string>> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> aliases = new(StringComparer.Ordinal);
    private readonly bool hasUnindexedDirectories;

    internal BmsResourcePathIndex(string directory, CancellationToken cancellationToken = default)
    {
        this.directory = Path.GetFullPath(string.IsNullOrEmpty(directory) ? "." : directory);
        directoryPrefix = this.directory.EndsWith(Path.DirectorySeparatorChar) ? this.directory : this.directory + Path.DirectorySeparatorChar;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(this.directory));

        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in current.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        // Linked directories may loop or lead outside this snapshot. Preserve direct
                        // file access there without following them during recursive enumeration.
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                            hasUnindexedDirectories = true;
                        else
                            pending.Push((DirectoryInfo)entry);
                        continue;
                    }

                    var path = entry.FullName;
                    if (!files.TryGetValue(path, out var variants))
                        files.Add(path, variants = []);
                    variants.Add(path);
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        addAlias(path[directoryPrefix.Length..], path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A directory can allow opening a known file while denying enumeration.
                hasUnindexedDirectories = true;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    internal bool TryResolve(string? name, [NotNullWhen(true)] out string? path)
    {
        if (!BmsFileResourceStore.TryGetFullPath(directory, name, out path))
            return false;

        if (files.TryGetValue(path, out var variants))
        {
            foreach (var variant in variants)
            {
                if (string.Equals(path, variant, StringComparison.Ordinal))
                {
                    path = variant;
                    return true;
                }
            }
        }

        // Case sensitivity belongs to the filesystem, which can vary between directories.
        // Only probe case variants in the snapshot or paths enumeration could not cover.
        if ((variants != null || hasUnindexedDirectories) && File.Exists(path))
            return true;

        return TryResolveAlias(path[directoryPrefix.Length..], out path);
    }

    internal bool TryResolveAlias(string alias, [NotNullWhen(true)] out string? path)
    {
        if (aliases.TryGetValue(alias, out var candidate) && candidate != null)
        {
            path = candidate;
            return true;
        }

        path = null;
        return false;
    }

    private void addAlias(string relativePath, string path)
    {
        // ASCII filenames survive the encoding round trip unchanged.
        if (Ascii.IsValid(relativePath))
            return;

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
            aliases.Add(alias, path);
        else if (!string.Equals(existing, path, StringComparison.Ordinal))
            aliases[alias] = null;
    }
}
