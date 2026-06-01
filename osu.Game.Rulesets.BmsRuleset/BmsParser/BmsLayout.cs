using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static class BmsLayout
{
    public const int BMS5_KEY_COLUMNS = 6;
    public const int BME7_KEY_COLUMNS = 8;
    public const int BMS5_DOUBLE_PLAY_COLUMNS = 12;
    public const int DOUBLE_PLAY_COLUMNS = 16;
    public const int PMS_COLUMNS = 9;
    public const int PMS_DOUBLE_PLAY_COLUMNS = 18;

    private static readonly string[] pms_double_play_only_channels = ["21", "26", "27", "28", "29"];
    private static readonly string[] second_player_channels = ["21", "22", "23", "24", "25", "26", "28", "29"];
    private static readonly string[] seven_key_only_channels = ["18", "19"];

    public static BmsLayoutVariant InferVariant(IEnumerable<string> channels, string? pathOrExtension = null)
    {
        var visibleChannels = channels.Select(normalisePlayableChannel)
            .Where(c => c != null)
            .Select(c => c!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extension = pathOrExtension == null ? string.Empty : Path.GetExtension(pathOrExtension);

        if (string.IsNullOrEmpty(extension) && pathOrExtension?.StartsWith(".", StringComparison.Ordinal) == true)
            extension = pathOrExtension;

        if (extension.Equals(".pms", StringComparison.OrdinalIgnoreCase))
            return visibleChannels.Overlaps(pms_double_play_only_channels) ? BmsLayoutVariant.Pms9KDouble : BmsLayoutVariant.Pms9K;

        if (visibleChannels.Overlaps(second_player_channels))
            return visibleChannels.Overlaps(seven_key_only_channels) ? BmsLayoutVariant.Bme7KDouble : BmsLayoutVariant.Bms5KDouble;

        return visibleChannels.Overlaps(seven_key_only_channels) ? BmsLayoutVariant.Bme7K : BmsLayoutVariant.Bms5K;
    }

    public static int InferTotalColumns(IEnumerable<string> channels, string? pathOrExtension = null)
        => GetTotalColumns(InferVariant(channels, pathOrExtension));

    public static int GetTotalColumns(BmsLayoutVariant variant) => variant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => BMS5_KEY_COLUMNS,
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => BME7_KEY_COLUMNS,
        BmsLayoutVariant.Pms9K => PMS_COLUMNS,
        BmsLayoutVariant.Bms5KDouble => BMS5_DOUBLE_PLAY_COLUMNS,
        BmsLayoutVariant.Bme7KDouble => DOUBLE_PLAY_COLUMNS,
        BmsLayoutVariant.Pms9KDouble => PMS_DOUBLE_PLAY_COLUMNS,
        _ => BMS5_KEY_COLUMNS,
    };

    public static BmsLayoutVariant VariantFromTotalColumns(int totalColumns) => totalColumns switch
    {
        BMS5_KEY_COLUMNS => BmsLayoutVariant.Bms5K,
        BME7_KEY_COLUMNS => BmsLayoutVariant.Bme7K,
        PMS_COLUMNS => BmsLayoutVariant.Pms9K,
        BMS5_DOUBLE_PLAY_COLUMNS => BmsLayoutVariant.Bms5KDouble,
        DOUBLE_PLAY_COLUMNS => BmsLayoutVariant.Bme7KDouble,
        PMS_DOUBLE_PLAY_COLUMNS => BmsLayoutVariant.Pms9KDouble,
        _ => BmsLayoutVariant.Bms5K,
    };

    public static bool IsKnownTotalColumns(int totalColumns) =>
        totalColumns is BMS5_KEY_COLUMNS or BME7_KEY_COLUMNS or BMS5_DOUBLE_PLAY_COLUMNS or PMS_COLUMNS or DOUBLE_PLAY_COLUMNS or PMS_DOUBLE_PLAY_COLUMNS;

    public static bool TryMapPlayableChannel(string channel, int totalColumns, out int column)
    {
        var visibleChannel = normalisePlayableChannel(channel);

        if (visibleChannel == null)
        {
            column = -1;
            return false;
        }

        return totalColumns switch
        {
            PMS_COLUMNS => tryMapPmsSingleChannel(visibleChannel, out column),
            PMS_DOUBLE_PLAY_COLUMNS => tryMapPmsDoubleChannel(visibleChannel, out column),
            BMS5_DOUBLE_PLAY_COLUMNS => tryMapBms5DoubleChannel(visibleChannel, out column),
            _ => TryMapBmsChannel(visibleChannel, out column),
        };
    }

    public static bool TryMapVisibleChannel(string channel, int totalColumns, out int column)
    {
        if (channel.Length != 2 || channel[0] is not ('1' or '2'))
        {
            column = -1;
            return false;
        }

        return totalColumns switch
        {
            PMS_COLUMNS => tryMapPmsSingleChannel(channel, out column),
            PMS_DOUBLE_PLAY_COLUMNS => tryMapPmsDoubleChannel(channel, out column),
            BMS5_DOUBLE_PLAY_COLUMNS => tryMapBms5DoubleChannel(channel, out column),
            _ => TryMapBmsChannel(channel, out column),
        };
    }

    public static bool TryMapBmsChannel(string channel, out int column)
    {
        column = channel switch
        {
            "16" => 0,
            "11" => 1,
            "12" => 2,
            "13" => 3,
            "14" => 4,
            "15" => 5,
            "18" => 6,
            "19" => 7,
            "21" => 8,
            "22" => 9,
            "23" => 10,
            "24" => 11,
            "25" => 12,
            "28" => 13,
            "29" => 14,
            "26" => 15,
            _ => -1,
        };

        return column >= 0;
    }

    private static bool tryMapPmsSingleChannel(string channel, out int column)
    {
        column = channel switch
        {
            "11" => 0,
            "12" => 1,
            "13" => 2,
            "14" => 3,
            "15" => 4,
            "18" or "22" => 5,
            "19" or "23" => 6,
            "16" or "24" => 7,
            "17" or "25" => 8,
            _ => -1,
        };

        return column >= 0;
    }

    private static bool tryMapBms5DoubleChannel(string channel, out int column)
    {
        column = channel switch
        {
            "16" => 0,
            "11" => 1,
            "12" => 2,
            "13" => 3,
            "14" => 4,
            "15" => 5,
            "21" => 6,
            "22" => 7,
            "23" => 8,
            "24" => 9,
            "25" => 10,
            "26" => 11,
            _ => -1,
        };

        return column >= 0;
    }

    private static bool tryMapPmsDoubleChannel(string channel, out int column)
    {
        if (channel.Length == 2 && channel[0] is '1' or '2' && channel[1] is >= '1' and <= '9')
        {
            column = (channel[0] == '2' ? PMS_COLUMNS : 0) + channel[1] - '1';
            return true;
        }

        column = -1;
        return false;
    }

    private static string? normalisePlayableChannel(string channel)
    {
        if (channel.Length != 2)
            return null;

        return channel[0] switch
        {
            '1' or '2' => channel,
            '5' => $"1{channel[1]}",
            '6' => $"2{channel[1]}",
            'D' => $"1{channel[1]}",
            'E' => $"2{channel[1]}",
            _ => null,
        };
    }
}
