using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;

public sealed class BmsSkinConfiguration(BmsSkinConfigurationSection section)
{

    public BmsSkinConfigurationSection Section { get; } = section;

    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Color4> Colours { get; } = new(StringComparer.Ordinal);

    public BmsLayoutVariant? Layout { get; set; }

    public int? Keys { get; set; }

    public int SpecialStyle { get; set; }

    public bool TryGet<TValue>(LegacyManiaSkinConfigurationLookups lookup, int? column, out IBindable<TValue>? value)
        where TValue : notnull
    {
        value = null;

        object? resolved = lookup switch
        {
            LegacyManiaSkinConfigurationLookups.ColumnWidth => getArrayValue("ColumnWidth", column),
            LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale => getFloatValue("WidthForNoteHeightScale", true),
            LegacyManiaSkinConfigurationLookups.HitPosition => getPositionFromBottom("HitPosition", 240, 480),
            LegacyManiaSkinConfigurationLookups.ComboPosition => getFloatValue("ComboPosition", true),
            LegacyManiaSkinConfigurationLookups.ScorePosition => getFloatValue("ScorePosition", true),
            LegacyManiaSkinConfigurationLookups.LightPosition => getPositionFromBottom("LightPosition"),
            LegacyManiaSkinConfigurationLookups.ShowJudgementLine => getBoolValue("JudgementLine"),
            LegacyManiaSkinConfigurationLookups.ExplosionImage => getImageValue("LightingN"),
            LegacyManiaSkinConfigurationLookups.ColumnLineColour => getColourValue("ColourColumnLine"),
            LegacyManiaSkinConfigurationLookups.JudgementLineColour => getColourValue("ColourJudgementLine"),
            LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour => column == null ? null : getColumnColourValue("Colour", column.Value + 1),
            LegacyManiaSkinConfigurationLookups.ColumnLightColour => column == null ? null : getColumnColourValue("ColourLight", column.Value + 1),
            LegacyManiaSkinConfigurationLookups.ComboBreakColour => getColourValue("ColourBreak"),
            LegacyManiaSkinConfigurationLookups.BarLineColour => getColourValue("ColourBarline"),
            LegacyManiaSkinConfigurationLookups.MinimumColumnWidth => getMinimumColumnWidth(),
            LegacyManiaSkinConfigurationLookups.BarLineHeight => getFloatValue("BarlineHeight", false),
            LegacyManiaSkinConfigurationLookups.NoteImage => getColumnImage("NoteImage", column),
            LegacyManiaSkinConfigurationLookups.HoldNoteHeadImage => getColumnImage("NoteImage", column, "H") ?? getColumnImage("NoteImage", column),
            LegacyManiaSkinConfigurationLookups.HoldNoteTailImage => getColumnImage("NoteImage", column, "T") ?? getColumnImage("NoteImage", column),
            LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage => getColumnImage("NoteImage", column, "L") ?? getColumnImage("NoteImage", column),
            LegacyManiaSkinConfigurationLookups.Hit100 => getColumnImage("MineImage", column) ?? getImageValue("MineImage"),
            LegacyManiaSkinConfigurationLookups.HoldNoteLightImage => getImageValue("LightingL"),
            LegacyManiaSkinConfigurationLookups.KeyImage => getColumnImage("KeyImage", column),
            LegacyManiaSkinConfigurationLookups.KeyImageDown => getColumnImage("KeyImage", column, "D"),
            LegacyManiaSkinConfigurationLookups.LeftStageImage => getImageValue("StageLeft"),
            LegacyManiaSkinConfigurationLookups.RightStageImage => getImageValue("StageRight"),
            LegacyManiaSkinConfigurationLookups.BottomStageImage => getImageValue("StageBottom"),
            LegacyManiaSkinConfigurationLookups.LightImage => getImageValue("StageLight"),
            LegacyManiaSkinConfigurationLookups.HitTargetImage => getImageValue("StageHint"),
            LegacyManiaSkinConfigurationLookups.Hit300g => getImageValue(Section == BmsSkinConfigurationSection.Bms ? "HitPGreat" : "Hit300g"),
            LegacyManiaSkinConfigurationLookups.Hit300 => getImageValue(Section == BmsSkinConfigurationSection.Bms ? "HitGreat" : "Hit300"),
            LegacyManiaSkinConfigurationLookups.Hit200 => getImageValue(Section == BmsSkinConfigurationSection.Bms ? "HitGood" : "Hit200"),
            LegacyManiaSkinConfigurationLookups.Hit50 => getImageValue(Section == BmsSkinConfigurationSection.Bms ? "HitBad" : "Hit50"),
            LegacyManiaSkinConfigurationLookups.Hit0 => getImageValue(Section == BmsSkinConfigurationSection.Bms ? "HitPoor" : "Hit0"),
            LegacyManiaSkinConfigurationLookups.KeysUnderNotes => getBoolValue("KeysUnderNotes"),
            LegacyManiaSkinConfigurationLookups.LightFramePerSecond => getIntValue("StageLightFramePerSecond") ?? getIntValue("LightFramePerSecond"),
            LegacyManiaSkinConfigurationLookups.LeftColumnSpacing => getLeftSpacing(column),
            LegacyManiaSkinConfigurationLookups.RightColumnSpacing => getRightSpacing(column),
            LegacyManiaSkinConfigurationLookups.LeftLineWidth => getArrayValue("ColumnLineWidth", column, false),
            LegacyManiaSkinConfigurationLookups.RightLineWidth => column == null ? null : getArrayValue("ColumnLineWidth", column + 1, false),
            LegacyManiaSkinConfigurationLookups.ExplosionScale => getLightScale("LightingNWidth", column),
            LegacyManiaSkinConfigurationLookups.HoldNoteLightScale => getLightScale("LightingLWidth", column),
            _ => null,
        };

        if (resolved == null)
            return false;

        value = SkinUtils.As<TValue>(resolved switch
        {
            string stringValue => new Bindable<string>(stringValue),
            bool boolValue => new Bindable<bool>(boolValue),
            int intValue => new Bindable<int>(intValue),
            float floatValue => new Bindable<float>(floatValue),
            Color4 colourValue => new Bindable<Color4>(colourValue),
            _ => throw new InvalidOperationException($"Unsupported BMS skin value type {resolved.GetType()}"),
        });
        return value != null;
    }

    private string? getColumnImage(string prefix, int? column, string suffix = "")
        => column == null ? null : getImageValue($"{prefix}{column.Value}{suffix}") ?? getImageValue($"{prefix}{suffix}");

    private string? getImageValue(string key) => Values.TryGetValue(key, out var result) && !string.IsNullOrWhiteSpace(result) ? result : null;

    private float? getFloatValue(string key, bool scale)
    {
        if (!Values.TryGetValue(key, out var raw) || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return null;

        return scale ? result * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR : result;
    }

    private int? getIntValue(string key)
    {
        if (!Values.TryGetValue(key, out var raw) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return null;

        return result;
    }

    private bool? getBoolValue(string key)
    {
        if (!Values.TryGetValue(key, out var raw))
            return null;

        return raw == "1" || (bool.TryParse(raw, out var result) && result);
    }

    private float? getPositionFromBottom(string key, float min = float.MinValue, float max = float.MaxValue)
    {
        var raw = getFloatValue(key, false);
        return raw == null ? null : (480 - Math.Clamp(raw.Value, min, max)) * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;
    }

    private Color4? getColourValue(string key) => Colours.TryGetValue(key, out var result) ? result : null;

    private Color4? getColumnColourValue(string prefix, int column) => getColourValue($"{prefix}{column}") ?? getColourValue(prefix);

    private float? getArrayValue(string key, int? index, bool scale = true)
    {
        if (index == null || index < 0 || !Values.TryGetValue(key, out var raw))
            return null;

        var values = raw.Split(',');
        if (index.Value >= values.Length || !float.TryParse(values[index.Value], NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            return null;

        return scale ? result * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR : result;
    }

    private float? getMinimumColumnWidth()
    {
        if (!Values.TryGetValue("ColumnWidth", out var raw))
            return null;

        var values = raw.Split(',')
            .Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR : (float?)null)
            .Where(v => v != null)
            .Select(v => v!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Min();
    }

    private float? getLeftSpacing(int? column)
    {
        if (column == null || column == 0 || column >= getColumnCount())
            return null;

        return getArrayValue("ColumnSpacing", column - 1) / 2;
    }

    private float? getRightSpacing(int? column)
    {
        if (column == null || column >= getColumnCount() - 1)
            return null;

        return getArrayValue("ColumnSpacing", column) / 2;
    }

    private int getColumnCount() => Section switch
    {
        BmsSkinConfigurationSection.Bms when Layout != null => BmsLayout.GetTotalColumns(Layout.Value),
        BmsSkinConfigurationSection.Mania when Keys != null => Keys.Value,
        _ => 0,
    };

    private float? getLightScale(string key, int? column)
    {
        var width = getArrayValue(key, column);
        if (width != null)
            return width / LegacyManiaSkinConfiguration.DEFAULT_COLUMN_SIZE;

        var columnWidth = getArrayValue("ColumnWidth", column);
        return columnWidth / LegacyManiaSkinConfiguration.DEFAULT_COLUMN_SIZE;
    }
}

public enum BmsSkinConfigurationSection
{
    Bms,
    Mania,
}
