using System;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;

internal readonly record struct BmsResolvedNoteMetrics(float? ConfiguredReferenceWidth, float? TextureHeightAspect)
{
    public float HeightFor(float drawWidth)
    {
        var referenceWidth = ConfiguredReferenceWidth ?? drawWidth;

        if (TextureHeightAspect != null)
            return Math.Max(1, TextureHeightAspect.Value * referenceWidth);

        return ConfiguredReferenceWidth != null
            ? Math.Max(1, referenceWidth)
            : BmsGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT;
    }
}
