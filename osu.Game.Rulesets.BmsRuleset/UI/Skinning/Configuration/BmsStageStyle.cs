using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;

internal sealed class BmsStageStyle
{
    internal const string STAGE_WIDE_KEY = "stage";
    internal const float MAX_COLUMN_WIDTH = 200;
    internal const float MAX_GAP = 100;
    internal const float MAX_LINE_WIDTH = 20;
    internal const float MAX_LIGHT_SCALE = 10;
    internal const float MAX_HIT_TARGET_HEIGHT_SCALE = 10;

    public bool KeepTotalWidth { get; set; } = true;
    public Dictionary<int, float> Widths { get; set; } = [];
    public Dictionary<int, float> NoteWidthForHeightScales { get; set; } = [];
    public Dictionary<int, float> HitLightScales { get; set; } = [];
    public Dictionary<int, float> HoldLightScales { get; set; } = [];
    public Dictionary<int, float> Gaps { get; set; } = [];
    public Dictionary<int, float> SeparatorWidths { get; set; } = [];
    public string? SeparatorColour { get; set; }
    public Dictionary<string, string> Images { get; set; } = [];
    public float? HitTargetHeightScale { get; set; }
    public float? BarHeight { get; set; }
    public string? BarColour { get; set; }
    public bool? ShowMeasureLines { get; set; }

    internal Colour4? GetSeparatorColour() => SeparatorColour != null && Colour4.TryParseHex(SeparatorColour, out var colour) ? colour : null;

    internal Colour4? GetBarColour() => BarColour != null && Colour4.TryParseHex(BarColour, out var colour) ? colour : null;

    internal void Normalize(BmsLayoutVariant layout)
    {
        var columns = BmsLayout.GetTotalColumns(layout);
        Widths ??= [];
        NoteWidthForHeightScales ??= [];
        HitLightScales ??= [];
        HoldLightScales ??= [];
        Gaps ??= [];
        SeparatorWidths ??= [];
        Images ??= [];

        normalize(Widths, columns, 1, MAX_COLUMN_WIDTH);
        normalize(NoteWidthForHeightScales, columns, 1, MAX_COLUMN_WIDTH);
        normalize(HitLightScales, columns, 0, MAX_LIGHT_SCALE);
        normalize(HoldLightScales, columns, 0, MAX_LIGHT_SCALE);
        normalize(Gaps, columns - 1, 0, MAX_GAP);
        normalize(SeparatorWidths, columns - 1, 0, MAX_LINE_WIDTH);

        HitTargetHeightScale = valid(HitTargetHeightScale, 0.1f, MAX_HIT_TARGET_HEIGHT_SCALE);
        BarHeight = valid(BarHeight, 0, MAX_LINE_WIDTH);

        if (GetSeparatorColour() == null)
            SeparatorColour = null;

        if (GetBarColour() == null)
            BarColour = null;
    }

    private static void normalize(Dictionary<int, float> values, int count, float min, float max)
    {
        foreach (var index in values.Keys.ToArray())
        {
            if (index < 0 || index >= count || valid(values[index], min, max) == null)
                values.Remove(index);
        }
    }

    private static float? valid(float? value, float min, float max) => value is { } number && float.IsFinite(number) && number >= min && number <= max ? number : null;

    internal string? GetImage(BmsStageImage image, int? column)
    {
        if (Images.TryGetValue($"{image}:{column?.ToString() ?? STAGE_WIDE_KEY}", out var exact))
            return string.IsNullOrEmpty(exact) ? null : exact;

        if (column != null && Images.TryGetValue($"{image}:{STAGE_WIDE_KEY}", out var shared))
            return shared;

        return null;
    }

    internal void SetImage(BmsStageImage image, int? column, string? name)
    {
        var key = $"{image}:{column?.ToString() ?? STAGE_WIDE_KEY}";
        if (!string.IsNullOrEmpty(name))
            Images[key] = name;
        else if (column != null && Images.ContainsKey($"{image}:{STAGE_WIDE_KEY}"))
            Images[key] = string.Empty; // An individual lane can opt out of shared artwork.
        else
            Images.Remove(key);
    }
}
