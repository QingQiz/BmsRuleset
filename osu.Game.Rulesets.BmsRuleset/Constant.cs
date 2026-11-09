using System;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset;

public static class Constant
{
    public static readonly string[] BMS_EXTENSIONS = [".bms", ".bme", ".bml", ".pms", ".bmson"];
    public const string AUTHOR = "QINGQIZ";
    public const string SHORT_NAME = "bms";

    internal static bool IsBmsonFile(string? filename) => filename?.EndsWith(".bmson", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsChartFile(string filename)
        => BMS_EXTENSIONS.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
