using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsResultComparisonPopover : OsuPopover
{
    private static readonly HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss];
    private readonly ScoreInfo score;
    private readonly Container comparison;
    private CancellationTokenSource? cancellation;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    internal BmsResultComparisonPopover(ScoreInfo score)
    {
        this.score = score;
        AllowableAnchors = [Anchor.CentreRight, Anchor.TopRight, Anchor.TopCentre];
        Body.CornerRadius = 12;
        Body.BorderThickness = 1;
        Body.BorderColour = Colour4.White.Opacity(0.08f);
        Child = new FillFlowContainer
        {
            Name = "Result comparison popover content",
            Width = 600,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 12),
            Children =
            [
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 28,
                    Children =
                    [
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Right = 36 },
                            Child = new BmsResultFittedText(BmsStrings.ResultCompareBest, 26),
                        },
                        new IconButton
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Icon = FontAwesome.Solid.Times,
                            Size = new Vector2(28),
                            IconScale = new Vector2(0.7f),
                            TooltipText = BmsStrings.ResultComparisonClose,
                            Action = this.HidePopover,
                        },
                    ],
                },
                comparison = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Child = message(BmsStrings.ResultComparisonLoading),
                },
            ],
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Background.Colour = new Colour4(26, 30, 36, 255);
        cancellation = new CancellationTokenSource();
        _ = loadComparisonAsync(cancellation.Token);
    }

    protected virtual Task<ScoreInfo?> LoadBestScoreAsync() => realm.RunAsync(r =>
    {
        if (string.IsNullOrEmpty(score.BeatmapHash))
            return null;

        // Re-imported copies may have a different local ID, but still represent the result being viewed.
        var candidates = r.All<ScoreInfo>()
            .Where(candidate => candidate.BeatmapHash == score.BeatmapHash && !candidate.DeletePending)
            .AsEnumerable()
            .Where(candidate =>
                (candidate.UserID == score.UserID || candidate.UserID <= 1)
                && candidate.ID != score.ID
                && (score.OnlineID <= 0 || candidate.OnlineID != score.OnlineID)
                && (score.LegacyOnlineID <= 0 || candidate.LegacyOnlineID != score.LegacyOnlineID))
            .ForRuleset(score.Ruleset.ShortName, score.BeatmapInfo)
            .Where(candidate => candidate.Mods.All(mod => mod.UserPlayable))
            .ToArray();

        var profile = GameplayProfile.FromMods(score.Mods);
        var maximum = candidates.Where(candidate => BmsLampScoreSelector.MatchesSelectedMods(candidate, score.Mods))
            .Select(profile.StoredMaximumScore).Append(profile.StoredMaximumScore(score)).Max();
        return BmsLampScoreSelector.SelectBest(candidates, score.Mods, maximum)?.Detach();
    });

    private async Task loadComparisonAsync(CancellationToken token)
    {
        try
        {
            var best = await LoadBestScoreAsync().ConfigureAwait(false);
            Schedule(() =>
            {
                if (IsDisposed || token.IsCancellationRequested)
                    return;

                comparison.Child = best == null ? message(BmsStrings.ResultComparisonEmpty) : createComparison(best);
            });
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to load the BMS result historical best.");
            Schedule(() =>
            {
                if (!IsDisposed && !token.IsCancellationRequested)
                    comparison.Child = message(BmsStrings.ResultComparisonUnavailable);
            });
        }
    }

    private Drawable createComparison(ScoreInfo best)
    {
        var profile = GameplayProfile.FromMods(score.Mods);
        var maximum = new[] { profile.StoredMaximumScore(score), profile.StoredMaximumScore(best) }.Max();
        var maximumCombo = Math.Max(score.GetMaximumAchievableCombo(), best.GetMaximumAchievableCombo());
        var rows = new List<Drawable>
        {
            new BmsResultComparisonRow(profile.ScoreLabel, profile.StoredScore(score, maximum), profile.StoredScore(best, maximum), true, scaleMaximum: maximum),
            new BmsResultComparisonRow(BmsStrings.ResultAccuracy, score.Accuracy, best.Accuracy, true, percentage: true, scaleMaximum: 1),
            new BmsResultComparisonRow(BmsStrings.ResultCombo, score.MaxCombo, best.MaxCombo, true, scaleMaximum: maximumCombo),
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 12,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Height = 1,
                    Colour = Colour4.White.Opacity(0.1f),
                },
            },
        };

        // All judgement counts must share a denominator so lengths remain comparable across categories and scores.
        var judgementMaximum = results.Max(result => Math.Max(score.Statistics.GetValueOrDefault(result), best.Statistics.GetValueOrDefault(result)));
        foreach (var result in results)
        {
            // Missing historical statistics are unknown, rather than six zero-count judgements.
            int? currentCount = score.Statistics.Count > 0 ? score.Statistics.GetValueOrDefault(result) : null;
            int? bestCount = best.Statistics.Count > 0 ? best.Statistics.GetValueOrDefault(result) : null;
            rows.Add(new BmsResultComparisonRow(profile.JudgementLabel(result), currentCount, bestCount,
                result == HitResult.Perfect ? true : result is HitResult.Ok or HitResult.Meh or HitResult.Miss ? false : null,
                profile.JudgementColour(result), scaleMaximum: judgementMaximum));
        }

        return new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Children =
            [
                new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 22,
                    ColumnDimensions = BmsResultComparisonRow.CreateColumns(),
                    Content = new Drawable[][]
                    {
                        [
                            Empty(),
                            Empty(),
                            legend(BmsStrings.ResultComparisonCurrent, BmsResultComparisonRow.CURRENT_COLOUR),
                            legend(BmsStrings.ResultComparisonBest, BmsResultComparisonRow.BEST_COLOUR),
                            new BmsResultFittedText(BmsStrings.ResultComparisonDifference, 15, Anchor.CentreRight),
                        ],
                    },
                },
                new FillFlowContainer
                {
                    Name = "Result comparison chart",
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Children = rows.ToArray(),
                },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 24,
                    ColumnDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 120)],
                    Content = new Drawable[][]
                    {
                        [
                            new BmsResultFittedText(BmsStrings.ResultComparisonDate(best.Date.ToLocalTime()), 14,
                                colour: Colour4.White.Opacity(0.5f)),
                            new BmsResultFittedContainer(new ModDisplay
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Current = { Value = best.Mods },
                                ExpansionMode = ExpansionMode.AlwaysExpanded,
                            }),
                        ],
                    },
                },
            ],
        };
    }

    private static Drawable legend(LocalisableString text, Colour4 colour) => new BmsResultFittedContainer(new FillFlowContainer
    {
        Anchor = Anchor.CentreRight,
        Origin = Anchor.CentreRight,
        AutoSizeAxes = Axes.Both,
        Direction = FillDirection.Horizontal,
        Spacing = new Vector2(6, 0),
        Children =
        [
            new Container
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Size = new Vector2(18),
                Children =
                [
                    new CircularContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Height = BmsResultComparisonRow.BAR_HEIGHT,
                        Masking = true,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = colour,
                        },
                    },
                ],
            },
            new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = text,
                Font = OsuFont.GetFont(size: 15),
                Colour = colour,
            },
        ],
    });

    private static Drawable message(LocalisableString text, float size = 16) => new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: size))
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Colour = Colour4.White.Opacity(0.7f),
        Text = text,
    };

    protected override void PopOut()
    {
        cancellation?.Cancel();
        base.PopOut();
    }

    protected override void Dispose(bool isDisposing)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        base.Dispose(isDisposing);
    }
}
