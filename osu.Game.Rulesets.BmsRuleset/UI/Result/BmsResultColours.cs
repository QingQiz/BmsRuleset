using osu.Framework.Graphics.Colour;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result;

internal static class BmsResultColours
{
    internal static readonly Color4 ACCENT = new(50, 217, 241, 255);
    internal static readonly Color4 FAST = new(90, 175, 255, 255);
    internal static readonly Color4 SLOW = new(255, 130, 92, 255);
    internal static readonly ColourInfo PROGRESS = ColourInfo.GradientHorizontal(new Color4(64, 174, 255, 255), ACCENT);
}
