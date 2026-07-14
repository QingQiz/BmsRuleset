using System;
using System.Linq;

namespace osu.Game.Rulesets.BmsRuleset;

public static class Constant
{
    public static readonly string[] BMS_EXTENSIONS = [".bms", ".bme", ".bml", ".pms"];
    public const string AUTHOR = "QINGQIZ";
    public const string SHORT_NAME = "bms";

    public static bool IsChartFile(string filename)
        => BMS_EXTENSIONS.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));
}
