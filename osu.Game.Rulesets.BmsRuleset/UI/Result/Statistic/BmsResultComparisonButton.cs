using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultComparisonButton : IconButton, IHasPopover
{
    private readonly ScoreInfo score;

    internal BmsResultComparisonButton(ScoreInfo score)
    {
        this.score = score;
        Name = "Result historical best comparison";
        Size = new Vector2(28);
        Icon = FontAwesome.Solid.ExchangeAlt;
        IconScale = new Vector2(0.8f);
        IconColour = Colour4.White.Opacity(0.65f);
        IconHoverColour = BmsResultColours.ACCENT;
        TooltipText = BmsStrings.ResultCompareBest;
        Action = this.ShowPopover;
    }

    public Popover GetPopover() => new BmsResultComparisonPopover(score);
}
