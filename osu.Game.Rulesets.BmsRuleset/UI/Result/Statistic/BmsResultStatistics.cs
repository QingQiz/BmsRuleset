using System.Linq;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Utils;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsAccuracyStatistic : StatisticDisplay
{
    private readonly double accuracy;

    internal BmsAccuracyStatistic(double accuracy)
        : base(BmsStrings.Accuracy)
    {
        this.accuracy = accuracy;
    }

    protected override Drawable CreateContent() => new OsuSpriteText
    {
        Font = OsuFont.Torus.With(size: 20, fixedWidth: true),
        Spacing = new Vector2(-2, 0),
        Text = accuracy.FormatAccuracy(),
    };
}

internal partial class BmsExScoreStatistic : ComboStatistic
{
    internal BmsExScoreStatistic(ScoreInfo score)
        : base(calculateExScore(score), calculateMaximumExScore(score))
    {
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        HeaderText.Text = BmsStrings.ExScore.ToUpper();

        foreach (var text in this.ChildrenOfType<OsuSpriteText>().Where(text => text.Text.ToString() == "PERFECT"))
            text.Text = BmsStrings.PerfectScore;
    }

    private static int calculateMaximumExScore(ScoreInfo score) => BmsExScore.Calculate(score.MaximumStatistics);

    private static int calculateExScore(ScoreInfo score)
    {
        var maximum = calculateMaximumExScore(score);
        return BmsExScore.Calculate(score, maximum);
    }
}

internal partial class BmsComboStatistic : ComboStatistic
{
    internal BmsComboStatistic(int combo, int? maximumCombo)
        : base(combo, maximumCombo)
    {
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        HeaderText.Text = BmsStrings.MaxCombo.ToUpper();
    }
}
