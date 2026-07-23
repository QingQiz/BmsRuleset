using osu.Framework.Graphics;
using osu.Framework.Logging;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class MissingBmsBgaVideoProvider : IBmsBgaVideoProvider
{
    public string Name => "missing";

    public bool CanCreate(BmsBgaVideoRequest request) => true;

    public Drawable? Create(BmsBgaVideoRequest request)
    {
        Logger.Log($"[BGA] No video provider accepted '{request.Path}'.", "bms-bga");
        return null;
    }
}
