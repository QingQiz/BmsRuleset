using osu.Framework.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed class MissingBmsBgaVideoProvider : IBmsBgaVideoProvider
{
    public string Name => "missing";

    public bool CanCreate(BmsBgaVideoRequest request) => true;

    public Drawable? Create(BmsBgaVideoRequest request)
    {
        BmsLogger.Log($"[BGA] No video provider accepted '{request.Path}'.");
        return null;
    }
}
