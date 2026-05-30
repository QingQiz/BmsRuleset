using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;

/// <summary>
/// Draws a single legacy note cap: normal note, mine, LN head, or LN tail.
/// </summary>
/// <remarks>
/// The texture is scaled to the current lane width on X. On Y it uses legacy
/// <see cref="LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale" /> semantics:
/// if that value exists, the height is calculated as though the note was drawn at that fixed
/// reference width, independent of the actual lane width.
/// </remarks>
internal sealed partial class LegacyBmsNotePiece : CompositeDrawable
{
    private readonly BmsLegacySkinTransformer transformer;
    private readonly BmsSkinComponentLookup lookup;
    private readonly float? widthForNoteHeightScale;
    private Drawable? noteAnimation;

    public LegacyBmsNotePiece(BmsLegacySkinTransformer transformer, BmsSkinComponentLookup lookup)
    {
        this.transformer = transformer;
        this.lookup = lookup;
        widthForNoteHeightScale = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale)?.Value;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Origin = Anchor.TopLeft;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        InternalChild = noteAnimation = transformer.GetLegacyAnimation(getImageName())?.With(d =>
        {
            d.Anchor = Anchor.TopLeft;
            d.Origin = Anchor.TopLeft;
        }) ?? Empty();
    }

    protected override void Update()
    {
        base.Update();

        if (noteAnimation == null)
            return;

        var texture = noteAnimation switch
        {
            Sprite sprite => sprite.Texture,
            TextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
            _ => null,
        };

        if (texture == null)
            return;

        var noteWidth = widthForNoteHeightScale ?? DrawWidth;
        noteAnimation.Scale = Vector2.Divide(new Vector2(DrawWidth, noteWidth), Math.Max(1, texture.DisplayWidth));
    }

    private string getImageName() => lookup.Component switch
    {
        BmsSkinComponents.Mine => transformer.GetMineImageName(lookup),
        BmsSkinComponents.HoldNoteHead => transformer.GetHoldNoteHeadImageName(lookup),
        BmsSkinComponents.HoldNoteTail => transformer.GetHoldNoteTailImageName(lookup),
        _ => transformer.GetNoteImageName(lookup),
    };
}
