using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Skinning;

public class BmsSkinComponentLookup(
    BmsSkinComponents component,
    BmsLayoutVariant layoutVariant = BmsLayoutVariant.Bme7K,
    int? columnIndex = null,
    bool isLongNote = false
)
    : SkinComponentLookup<BmsSkinComponents>(component)
{
    public readonly BmsLayoutVariant LayoutVariant = layoutVariant;

    public readonly int? ColumnIndex = columnIndex;

    public readonly bool IsLongNote = isLongNote;

    public bool IsScratch => ColumnIndex != null && IsScratchColumn(ColumnIndex.Value, LayoutVariant);

    public int ManiaKeyCount => GetManiaKeyCount(LayoutVariant);

    public int? ManiaColumnIndex => ColumnIndex == null ? null : MapToManiaColumn(ColumnIndex.Value, LayoutVariant);

    public int? StageColumnIndex => ColumnIndex == null ? null : MapToStageColumn(ColumnIndex.Value, LayoutVariant);

    public static bool IsScratchColumn(int column, BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bme7K => column == 0,
        BmsLayoutVariant.Bms5KDouble => column is 0 or 11,
        BmsLayoutVariant.Bme7KDouble => column is 0 or 15,
        _ => false,
    };

    public static int GetManiaKeyCount(BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K => 5,
        BmsLayoutVariant.Bme7K => 7,
        BmsLayoutVariant.Bms5KDouble => 10,
        BmsLayoutVariant.Bme7KDouble => 14,
        BmsLayoutVariant.Pms9K => 9,
        BmsLayoutVariant.Pms9KDouble => 18,
        _ => 7,
    };

    public static int MapToManiaColumn(int column, BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5K => Math.Clamp(column - 1, 0, 4),
        BmsLayoutVariant.Bme7K => Math.Clamp(column - 1, 0, 6),
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

    public static int MapToStageColumn(int column, BmsLayoutVariant layoutVariant) => layoutVariant switch
    {
        BmsLayoutVariant.Bms5KDouble => column < 6 ? column : column - 6,
        BmsLayoutVariant.Bme7KDouble => column < 8 ? column : column - 8,
        BmsLayoutVariant.Pms9KDouble => column < 9 ? column : column - 9,
        _ => column,
    };
}

public enum BmsSkinComponents
{
    ColumnBackground,
    HitTarget,
    KeyArea,
    Mine,
    Note,
    HoldNoteHead,
    HoldNoteTail,
    HoldNoteBody,
    HitExplosion,
    StageBackground,
    StageForeground,
    BarLine,
}
