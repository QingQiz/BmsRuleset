using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <summary>
///     Default (non-skin) judgement text piece for BMS.
///     Shows BMS-native label text in the centre of the stage when no legacy skin provides
///     a custom judgement sprite. Labels are sourced from <see cref="BmsRuleset.HIT_RESULT_LABELS"/>.
/// </summary>
public partial class BmsDefaultJudgementPiece : CompositeDrawable, IAnimatableJudgement
{
    private readonly HitResult result;

    private SpriteText text = null!;

    public BmsDefaultJudgementPiece(HitResult result)
    {
        this.result = result;
        AutoSizeAxes = Axes.Both;
        Origin = Anchor.Centre;
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colours)
    {
        var label = BmsRuleset.HIT_RESULT_LABELS.TryGetValue(result, out var l)
            ? l
            : result.ToString().ToUpperInvariant();

        AddInternal(text = new OsuSpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Font = OsuFont.Numeric.With(size: 18, weight: FontWeight.Bold),
            Text = label,
            Colour = colours.ForHitResult(result),
            Scale = new Vector2(0.85f, 1),
        });
    }

    /// <inheritdoc />
    public void PlayAnimation()
    {
        if (result.IsMiss())
        {
            this.ScaleTo(1.6f);
            this.ScaleTo(1, 100, Easing.In);
            this.MoveTo(Vector2.Zero);
            this.MoveToOffset(new Vector2(0, 80), 600, Easing.InQuint);
            this.RotateTo(0);
            this.RotateTo(30, 600, Easing.InQuint);
        }
        else
        {
            this.ScaleTo(1.2f);
            this.ScaleTo(1, 100, Easing.OutElastic);
        }

        this.FadeOutFromOne(600);
    }

    /// <inheritdoc />
    public Drawable? GetAboveHitObjectsProxiedContent() => null;
}
