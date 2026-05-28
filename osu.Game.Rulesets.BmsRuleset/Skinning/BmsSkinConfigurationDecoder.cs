using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using osu.Framework.IO.Stores;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public static class BmsSkinConfigurationDecoder
{
    private static readonly FieldInfo? skin_store_field = typeof(Skin).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic);

    public static IReadOnlyList<BmsSkinConfiguration> Decode(ISkin skin)
    {
        if (skin is not Skin concreteSkin || skin_store_field?.GetValue(concreteSkin) is not IResourceStore<byte[]> store)
            return [];

        using var stream = store.GetStream("skin.ini");
        if (stream == null)
            return [];

        using var reader = new StreamReader(stream);
        return Decode(reader);
    }

    public static IReadOnlyList<BmsSkinConfiguration> Decode(TextReader reader)
    {
        var result = new List<BmsSkinConfiguration>();
        BmsSkinConfiguration? current = null;

        while (reader.ReadLine() is { } rawLine)
        {
            var line = stripComments(rawLine).Trim();

            if (line.Length == 0)
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                commitCurrent();

                current = line[1..^1] switch
                {
                    "BMS" => new BmsSkinConfiguration(BmsSkinConfigurationSection.Bms),
                    "Mania" => new BmsSkinConfiguration(BmsSkinConfigurationSection.Mania),
                    _ => null,
                };

                continue;
            }

            if (current == null)
                continue;

            var pair = splitKeyValue(line);

            if (pair.Key.Length == 0)
                continue;

            switch (pair.Key)
            {
                case "Layout" when current.Section == BmsSkinConfigurationSection.Bms:
                    current.Layout = parseLayout(pair.Value);
                    break;

                case "Keys" when current.Section == BmsSkinConfigurationSection.Mania:
                    if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keys))
                        current.Keys = keys;
                    break;

                case "SpecialStyle" when current.Section == BmsSkinConfigurationSection.Mania:
                    if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var specialStyle))
                        current.SpecialStyle = specialStyle;
                    break;

                case string colour when colour.StartsWith("Colour", StringComparison.Ordinal):
                    if (tryParseColour(pair.Value, out var parsed))
                        current.Colours[pair.Key] = parsed;
                    break;

                default:
                    current.Values[pair.Key] = pair.Value;
                    break;
            }
        }

        commitCurrent();
        return result;

        void commitCurrent()
        {
            if (current == null)
                return;

            if (current.Section == BmsSkinConfigurationSection.Bms && current.Layout != null)
                result.Add(current);
            else if (current.Section == BmsSkinConfigurationSection.Mania && current.Keys != null)
                result.Add(current);

            current = null;
        }
    }

    private static string stripComments(string line)
    {
        var index = line.AsSpan().IndexOf("//".AsSpan());
        return index >= 0 ? line[..index] : line;
    }

    private static KeyValuePair<string, string> splitKeyValue(string line)
    {
        var split = line.Split(':', 2, StringSplitOptions.TrimEntries);
        return new KeyValuePair<string, string>(split[0], split.Length > 1 ? split[1] : string.Empty);
    }

    private static BmsLayoutVariant? parseLayout(string value)
    {
        return value switch
        {
            "5K" or "BMS5K" => BmsLayoutVariant.Bms5K,
            "7K" or "BME7K" => BmsLayoutVariant.Bme7K,
            "9K" or "PMS9K" => BmsLayoutVariant.Pms9K,
            "10K" or "BMS5KDouble" => BmsLayoutVariant.Bms5KDouble,
            "14K" or "BME7KDouble" => BmsLayoutVariant.Bme7KDouble,
            "18K" or "PMS9KDouble" => BmsLayoutVariant.Pms9KDouble,
            _ => null,
        };
    }

    private static bool tryParseColour(string value, out Color4 colour)
    {
        colour = default;
        var split = value.Split(',', StringSplitOptions.TrimEntries);
        if (split.Length is not 3 and not 4)
            return false;

        if (!byte.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(split[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(split[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
            return false;

        var a = (byte)255;
        if (split.Length == 4 && !byte.TryParse(split[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out a))
            return false;

        colour = new Color4(r, g, b, a);
        return true;
    }
}
