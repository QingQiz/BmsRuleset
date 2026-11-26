using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Embedded;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Configuration;

internal enum BmsStageImage
{
    HitTarget,
    LeftBorder,
    RightBorder,
    BottomBorder,
    ColumnBackground,
    Note,
    HoldHead,
    HoldBody,
    HoldTail,
    Mine,
    Key,
    KeyDown,
    Light,
    HitLight,
    HoldLight,
}

internal static class BmsStageImageScope
{
    internal static bool IsStageWide(this BmsStageImage image) => image is BmsStageImage.HitTarget or BmsStageImage.LeftBorder or BmsStageImage.RightBorder or BmsStageImage.BottomBorder;
}

internal sealed class BmsStageSkin(Func<BmsStageStyle> getStyle)
{
    internal BmsStageStyle Style => getStyle();

    internal string? GetImage(BmsStageImage image, int? column = null) => Style.GetImage(image, column);

    internal static Drawable? GetDrawable(ISkinSource skin, BmsStageImage image, int? column = null)
    {
        var name = (skin as BmsEmbeddedSkinSource)?.StageSkin?.GetImage(image, column);
        return string.IsNullOrEmpty(name) ? null : skin.GetAnimation(name, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true);
    }
}
