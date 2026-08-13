using osu.Framework.Graphics.Sprites;

namespace osu.Game.Rulesets.BmsRuleset.UI.Icons;

/// <summary>
/// Glyphs from the ruleset-owned mod icon font.
/// </summary>
public static class BmsIcons
{
    private const string font_name = "bmsIcons";

    public static IconUsage Scratch => get(0xE000);

    public static IconUsage HideScratch => get(0xE001);

    public static IconUsage AutoScratch => get(0xE002);

    public static IconUsage AutoGauge => get(0xE003);

    private static IconUsage get(int codepoint) => new((char)codepoint, font_name);
}
