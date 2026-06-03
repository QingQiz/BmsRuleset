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

    public bool IsScratch => ColumnIndex != null && BmsLayout.IsScratchColumn(ColumnIndex.Value, LayoutVariant);

    public int ManiaKeyCount => BmsLayout.GetManiaKeyCount(LayoutVariant);

    public int? ManiaColumnIndex => ColumnIndex == null ? null : BmsLayout.MapToManiaColumn(ColumnIndex.Value, LayoutVariant);

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
