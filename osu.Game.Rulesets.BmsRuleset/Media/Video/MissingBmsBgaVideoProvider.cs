using osu.Framework.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class MissingBmsBgaVideoProvider : IBmsBgaVideoProvider
{
    public bool TryCreate(BmsBgaVideoRequest request, out Drawable? drawable)
    {
        BmsLogger.Log($"[BGA] No video provider accepted '{request.Path}'.");
        drawable = null;
        return true;
    }
}
