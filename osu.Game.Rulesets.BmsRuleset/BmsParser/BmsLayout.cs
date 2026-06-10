using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// ReSharper disable InconsistentNaming

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

public static class BmsLayout
{
    public const int BMS5_KEY_COLUMNS = 6;
    public const int BME7_KEY_COLUMNS = 8;
    public const int BMS5_DOUBLE_PLAY_COLUMNS = 12;
    public const int DOUBLE_PLAY_COLUMNS = 16;
    public const int PMS_COLUMNS = 9;
    public const int PMS_DOUBLE_PLAY_COLUMNS = 18;

    // Encoded channel constants (base-62, uppercase: (hi << 6) | lo).
    private const ushort C11 = (1 << 6) | 1;
    private const ushort C12 = (1 << 6) | 2;
    private const ushort C13 = (1 << 6) | 3;
    private const ushort C14 = (1 << 6) | 4;
    private const ushort C15 = (1 << 6) | 5;
    private const ushort C16 = (1 << 6) | 6;
    private const ushort C17 = (1 << 6) | 7;
    private const ushort C18 = (1 << 6) | 8;
    private const ushort C19 = (1 << 6) | 9;
    private const ushort C21 = (2 << 6) | 1;
    private const ushort C22 = (2 << 6) | 2;
    private const ushort C23 = (2 << 6) | 3;
    private const ushort C24 = (2 << 6) | 4;
    private const ushort C25 = (2 << 6) | 5;
    private const ushort C26 = (2 << 6) | 6;
    private const ushort C27 = (2 << 6) | 7;
    private const ushort C28 = (2 << 6) | 8;
    private const ushort C29 = (2 << 6) | 9;

    private static readonly ushort[] pms_double_play_only_channels = [C21, C26, C27, C28, C29];
    private static readonly ushort[] second_player_channels = [C21, C22, C23, C24, C25, C26, C28, C29];
    private static readonly ushort[] seven_key_only_channels = [C18, C19];

    public static BmsLayoutVariant InferVariant(IEnumerable<ushort> channels, string? pathOrExtension = null)
    {
        var visibleChannels = channels.Select(NormaliseChannel)
            .ToHashSet();

        var extension = pathOrExtension == null ? string.Empty : Path.GetExtension(pathOrExtension);

        if (string.IsNullOrEmpty(extension) && pathOrExtension?.StartsWith(".", StringComparison.Ordinal) == true)
            extension = pathOrExtension;

        if (extension.Equals(".pms", StringComparison.OrdinalIgnoreCase))
            return visibleChannels.Overlaps(pms_double_play_only_channels) ? BmsLayoutVariant.Pms9KDouble : BmsLayoutVariant.Pms9K;

        if (visibleChannels.Overlaps(second_player_channels))
            return visibleChannels.Overlaps(seven_key_only_channels) ? BmsLayoutVariant.Bme7KDouble : BmsLayoutVariant.Bms5KDouble;

        return visibleChannels.Overlaps(seven_key_only_channels) ? BmsLayoutVariant.Bme7K : BmsLayoutVariant.Bms5K;
    }

    public static int InferTotalColumns(IEnumerable<ushort> channels, string? pathOrExtension = null)
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

    public static bool TryMapPlayableChannel(ushort channel, int totalColumns, out int column)
    {
        var visibleChannel = NormaliseChannel(channel);

        var success = totalColumns switch
        {
            PMS_COLUMNS => tryMapPmsSingleChannel(visibleChannel, out column),
            PMS_DOUBLE_PLAY_COLUMNS => tryMapPmsDoubleChannel(visibleChannel, out column),
            BMS5_DOUBLE_PLAY_COLUMNS => tryMapBms5DoubleChannel(visibleChannel, out column),
            _ => TryMapBmsChannel(visibleChannel, out column),
        };

        if (success && column >= totalColumns)
        {
            column = -1;
            return false;
        }

        return success;
    }

    public static bool TryMapVisibleChannel(ushort channel, int totalColumns, out int column)
    {
        var hi = BmsChartParser.Hi(channel);
        if (hi is not (1 or 2))
        {
            column = -1;
            return false;
        }

        var success = totalColumns switch
        {
            PMS_COLUMNS => tryMapPmsSingleChannel(channel, out column),
            PMS_DOUBLE_PLAY_COLUMNS => tryMapPmsDoubleChannel(channel, out column),
            BMS5_DOUBLE_PLAY_COLUMNS => tryMapBms5DoubleChannel(channel, out column),
            _ => TryMapBmsChannel(channel, out column),
        };

        if (success && column >= totalColumns)
        {
            column = -1;
            return false;
        }

        return success;
    }

    public static bool TryMapBmsChannel(ushort channel, out int column)
    {
        column = channel switch
        {
            C16 => 0,
            C11 => 1,
            C12 => 2,
            C13 => 3,
            C14 => 4,
            C15 => 5,
            C18 => 6,
            C19 => 7,
            C21 => 8,
            C22 => 9,
            C23 => 10,
            C24 => 11,
            C25 => 12,
            C28 => 13,
            C29 => 14,
            C26 => 15,
            _ => -1,
        };

        return column >= 0;
    }

    /// <summary>
    /// the gap index for column gap idx for 2p, example
    /// col have left side gap and right side gap.
    /// the function will get the new left side gap and right side gap from colX Left and colY Right
    /// </summary>
    /// <param name="idx"></param>
    /// <param name="columns"></param>
    /// <returns>
    /// colX, colY
    /// </returns>
    public static (int Left, int Right) RemapColum2PGapIdx(int idx, int columns)
    {
        var lIdx = idx switch
        {
            0 => 1,
            1 => 0,
            _ => idx,
        };

        var rIdx = idx switch
        {
            0 => columns - 1,               // Scratch → stage right edge
            _ when idx == columns - 1 => 0, // Last key → scratch-key1 gap
            _ => idx,                       // Internal keys → same as non-2P
        };

        return (lIdx, rIdx);
    }

    public static bool Is2P(BmsLayoutVariant variant) => variant switch
    {
        BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P => true,
        _ => false,
    };

    public static BmsLayoutVariant SecondPlayerVariant(BmsLayoutVariant variant) => variant switch
    {
        BmsLayoutVariant.Bms5K => BmsLayoutVariant.Bms5K2P,
        BmsLayoutVariant.Bme7K => BmsLayoutVariant.Bme7K2P,
        _ => variant,
    };

    public static bool IsScratchColumn(int column, BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bme7K2P => column == 0,
        BmsLayoutVariant.Bms5KDouble => column is 0 or 11,
        BmsLayoutVariant.Bme7KDouble => column is 0 or 15,
        _ => false,
    };

    public static int GetManiaKeyCount(BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => 5,
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => 7,
        BmsLayoutVariant.Pms9K => 9,
        BmsLayoutVariant.Bms5KDouble => 10,
        BmsLayoutVariant.Bme7KDouble => 14,
        BmsLayoutVariant.Pms9KDouble => 18,
        _ => 7,
    };

    public static int MapToManiaColumn(int column, BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P => Math.Clamp(column - 1, 0, 4),
        BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P => Math.Clamp(column - 1, 0, 6),
        BmsLayoutVariant.Bms5KDouble => column switch
        {
            0 => 0,
            >= 1 and <= 5 => column - 1,
            >= 6 and <= 10 => column - 1,
            11 => 9,
            _ => Math.Clamp(column, 0, 9),
        },
        BmsLayoutVariant.Bme7KDouble => column switch
        {
            0 => 0,
            >= 1 and <= 7 => column - 1,
            >= 8 and <= 14 => column - 1,
            15 => 13,
            _ => Math.Clamp(column, 0, 13),
        },
        _ => column,
    };

    /// <summary>Normalise LN/mine channels to their visible equivalents (5x→1x, 6x→2x, Dx→1x, Ex→2x).</summary>
    internal static ushort NormaliseChannel(ushort key) => BmsChartParser.Hi(key) switch
    {
        5 => BmsChartParser.Pack(1, BmsChartParser.Lo(key)),  // 5x → 1x
        6 => BmsChartParser.Pack(2, BmsChartParser.Lo(key)),  // 6x → 2x
        13 => BmsChartParser.Pack(1, BmsChartParser.Lo(key)), // Dx → 1x
        14 => BmsChartParser.Pack(2, BmsChartParser.Lo(key)), // Ex → 2x
        _ => key,
    };

    private static bool tryMapPmsSingleChannel(ushort channel, out int column)
    {
        column = channel switch
        {
            C11 => 0,
            C12 => 1,
            C13 => 2,
            C14 => 3,
            C15 => 4,
            C18 or C22 => 5,
            C19 or C23 => 6,
            C16 or C24 => 7,
            C17 or C25 => 8,
            _ => -1,
        };

        return column >= 0;
    }

    private static bool tryMapBms5DoubleChannel(ushort channel, out int column)
    {
        column = channel switch
        {
            C16 => 0,
            C11 => 1,
            C12 => 2,
            C13 => 3,
            C14 => 4,
            C15 => 5,
            C21 => 6,
            C22 => 7,
            C23 => 8,
            C24 => 9,
            C25 => 10,
            C26 => 11,
            _ => -1,
        };

        return column >= 0;
    }

    private static bool tryMapPmsDoubleChannel(ushort channel, out int column)
    {
        var hi = BmsChartParser.Hi(channel);
        var lo = BmsChartParser.Lo(channel);
        if (hi is 1 or 2 && lo is >= 1 and <= 9)
        {
            column = (hi == 2 ? PMS_COLUMNS : 0) + lo - 1;
            return true;
        }

        column = -1;
        return false;
    }
}
