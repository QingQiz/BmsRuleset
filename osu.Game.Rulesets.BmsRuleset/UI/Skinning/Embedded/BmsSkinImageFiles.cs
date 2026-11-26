using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Game.IO;
using osu.Game.Skinning;
using osu.Game.Utils;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;

internal static class BmsSkinImageFiles
{
    internal static IReadOnlyDictionary<string, string> StageChoices(SkinManager skins, ISkinSource source)
    {
        var files = Enumerate(skins, source).ToArray();
        var stems = files.Select(textureStem).ToHashSet(StringComparer.Ordinal);
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var stem = textureStem(file);
            var separator = stem.LastIndexOf('-');
            if (separator >= 0 && int.TryParse(stem.AsSpan(separator + 1), out var frame))
            {
                var animation = stem[..separator];
                if (stems.Contains($"{animation}-0") && stems.Contains($"{animation}-1"))
                {
                    // Show the first frame's filename but pass the sequence name to legacy animation lookup.
                    if (frame == 0)
                        choices.TryAdd(animation, file);
                    continue;
                }
            }

            choices.TryAdd(file, file);
        }

        return choices;

        static string textureStem(string file)
        {
            var stem = Path.ChangeExtension(file, null);
            return stem.EndsWith("@2x", StringComparison.Ordinal) ? stem[..^3] : stem;
        }
    }

    internal static IEnumerable<string> Enumerate(SkinManager skins, ISkinSource source)
    {
        var imported = skins.CurrentSkin.Value.SkinInfo.PerformRead(skin => skin.Files.Select(file => file.Filename).ToArray());
        var resources = ((IStorageResourceProvider)skins).Resources;
        return imported.Concat(source.AllSources.SelectMany(filesFor))
            .Where(name => SupportedExtensions.IMAGE_EXTENSIONS.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> filesFor(ISkin skin)
        {
            while (skin is ISkinTransformer transformer)
                skin = transformer.Skin;

            return skin switch
            {
                BmsEmbeddedSkin embedded => embedded.Resources.GetAvailableResources(),
                DefaultLegacySkin => filesIn("Skins/Legacy/"),
                RetroSkin => filesIn("Skins/Retro/"),
                _ => [],
            };
        }

        // Editable copies retain the built-in source, while Realm lists only the imported files.
        IEnumerable<string> filesIn(string prefix) => resources.GetAvailableResources()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..]);
    }
}
