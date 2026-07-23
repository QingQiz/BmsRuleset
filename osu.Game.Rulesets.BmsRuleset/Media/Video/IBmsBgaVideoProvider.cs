using System.IO;
using osu.Framework.Graphics;
using osu.Framework.Timing;

namespace osu.Game.Rulesets.BmsRuleset.Media.Video;

internal sealed record BmsBgaVideoRequest(
    string Path,
    Stream Stream,
    IFrameBasedClock Clock,
    double EventStartTime);

internal interface IBmsBgaVideoProvider
{
    string Name { get; }

    bool CanCreate(BmsBgaVideoRequest request);

    Drawable? Create(BmsBgaVideoRequest request);
}
