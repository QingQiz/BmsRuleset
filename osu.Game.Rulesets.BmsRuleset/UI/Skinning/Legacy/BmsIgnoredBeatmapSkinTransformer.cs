using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;

/// <summary>
/// Keeps beatmap visuals out of BMS gameplay while allowing imported mania hitsounds.
/// </summary>
internal sealed class BmsIgnoredBeatmapSkinTransformer(ISkin skin) : SkinTransformer(skin)
{
    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

    public override Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

    public override ISample? GetSample(ISampleInfo sampleInfo) => sampleInfo is HitSampleInfo { UseBeatmapSamples: true }
        ? base.GetSample(sampleInfo) : null;

    public override IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup) => null;
}
