using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultModDisplay : BmsResultFittedContainer
{
    private readonly ScoreInfo score;
    private readonly StarRatingDisplay? stars;

    internal BmsResultModDisplay(ScoreInfo score, bool showStarRating = true)
        : base(new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
        }, Axes.X)
    {
        this.score = score;
        var flow = (FillFlowContainer)InternalChild;
        var beatmap = score.BeatmapInfo!;

        if (showStarRating)
        {
            flow.Add(stars = new StarRatingDisplay(new StarDifficulty(beatmap.StarRating, 0))
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            });
        }

        var difficultyIcon = new BmsResultDifficultyIcon(score)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            ShowTooltip = showStarRating,
        };

        if (stars != null)
            difficultyIcon.Current.BindTo(stars.Current);

        flow.AddRange(
        [
            difficultyIcon,
            new ModDisplay
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                ExpansionMode = ExpansionMode.AlwaysExpanded,
                Current = { Value = score.Mods },
                Scale = new Vector2(0.5f),
            },
        ]);
    }

    [BackgroundDependencyLoader]
    private void load(RealmAccess realm, BeatmapDifficultyCache difficultyCache)
    {
        // Local beatmaps can use mod-adjusted difficulty; remote scores retain their supplied star rating.
        if (stars != null && realm.Run(database => database.Find<BeatmapInfo>(score.BeatmapInfo!.ID)) != null)
            stars.Current.Value = difficultyCache.GetDifficultyAsync(score.BeatmapInfo!, score.Ruleset, score.Mods).GetResultSafely() ?? stars.Current.Value;
    }
}
