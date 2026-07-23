using System;
using System.Collections.Generic;
using System.IO;
using osu.Framework.IO.Stores;

namespace osu.Game.Rulesets.BmsRuleset.Audio.Resources;

internal static class BmsAudioFormatSupport
{
    private static readonly string[] fallback_extensions = ["wav", "flac", "ogg", "mp3"];

    internal static IReadOnlyList<string> Extensions => fallback_extensions;

    public static void AddExtensions(ResourceStore<byte[]> resources)
    {
        foreach (var extension in fallback_extensions)
            resources.AddExtension(extension);
    }

    public static int GetFallbackPriority(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.');

        for (var i = 0; i < fallback_extensions.Length; i++)
        {
            if (extension.Equals(fallback_extensions[i], StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return fallback_extensions.Length;
    }

    public static bool IsSupported(string path) => GetFallbackPriority(path) < fallback_extensions.Length;
}
