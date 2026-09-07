// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Adapted from osu! Screens/Select/BeatmapLeaderboardScore.Tooltip.cs at 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

// Keep osu!'s overlapping panels and typography so BMS shares the native tooltip presentation.
internal sealed partial class BmsLeaderboardScoreTooltip(OverlayColourProvider colourProvider)
    : VisibilityContainer, ITooltip<ScoreInfo>
{
    private const float spacing = 20f;
    private const int corner_radius = 10;

    private DateAndStatisticsPanel dateAndStatistics = null!;
    private ModsPanel modsPanel = null!;
    private TotalScoreRankPanel totalScoreRankPanel = null!;

    [Cached]
    private readonly OverlayColourProvider colourProvider = colourProvider;

    [BackgroundDependencyLoader]
    private void load()
    {
        Width = 210;
        AutoSizeAxes = Axes.Y;

        InternalChild = new ReverseChildIDFillFlowContainer<Drawable>
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Spacing = new Vector2(0f, -spacing),
            Children =
            [
                dateAndStatistics = new DateAndStatisticsPanel(),
                modsPanel = new ModsPanel(),
                totalScoreRankPanel = new TotalScoreRankPanel(),
            ],
        };
    }

    private ScoreInfo? lastContent;

    public void SetContent(ScoreInfo content)
    {
        if (lastContent != null && lastContent.Equals(content))
            return;

        dateAndStatistics.Score = content;
        modsPanel.Score = content;
        totalScoreRankPanel.Score = content;
        lastContent = content;
    }

    protected override void PopIn() => this.FadeIn(300, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(300, Easing.OutQuint);

    public void Move(Vector2 pos) => Position = pos;

    private partial class DateAndStatisticsPanel : CompositeDrawable
    {
        private static readonly HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss];
        private OsuSpriteText absoluteDate = null!;
        private DrawableDate relativeDate = null!;
        private FillFlowContainer statistics = null!;

        private readonly Bindable<bool> prefer24HourTime = new();

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        private ScoreInfo? score;

        public ScoreInfo Score
        {
            set
            {
                score = value;

                updateAbsoluteDate();
                relativeDate.Date = value.Date;

                var maximum = BmsExScore.Calculate(value.MaximumStatistics);
                statistics.Children =
                [
                    .. results.Select(result => new JudgementRow(result, value.Statistics.GetValueOrDefault(result))),
                    Empty().With(drawable => drawable.Height = 8),
                    new StatisticRow(BmsStrings.LeaderboardClearStatus, BmsStrings.LeaderboardLamp(BmsLampCalculator.Calculate(value))),
                    new StatisticRow(BmsStrings.ExScore, maximum > 0
                        ? BmsStrings.LeaderboardFraction(BmsExScore.Calculate(value, maximum), maximum)
                        : BmsStrings.ResultNumber(BmsExScore.Calculate(value, maximum))),
                    new StatisticRow(BmsStrings.MaxCombo, BmsStrings.ResultNumber(value.MaxCombo)),
                    new StatisticRow(BmsStrings.ResultAccuracy, BmsStrings.ResultPercentage(value.Accuracy)),
                ];
            }
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager configManager)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            CornerRadius = corner_radius;
            Masking = true;

            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.25f),
                Radius = 4f,
            };

            InternalChildren =
            [
                new Box
                {
                    Colour = colourProvider.Background4,
                    RelativeSizeAxes = Axes.Both,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0f, 4f),
                    Margin = new MarginPadding { Top = 8f },
                    Children =
                    [
                        absoluteDate = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                            UseFullGlyphHeight = false,
                        },
                        relativeDate = new RelativeDate(default)
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Colour = colourProvider.Content2,
                            UseFullGlyphHeight = false,
                        },
                        new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            CornerRadius = corner_radius,
                            Masking = true,
                            Margin = new MarginPadding { Top = 4f },
                            Children =
                            [
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colourProvider.Background3,
                                },
                                statistics = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0f, 2f),
                                    Padding = new MarginPadding(8f),
                                },
                            ],
                        },
                    ],
                },
            ];

            configManager.BindWith(OsuSetting.Prefer24HourTime, prefer24HourTime);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            prefer24HourTime.BindValueChanged(_ => updateAbsoluteDate(), true);
        }

        private void updateAbsoluteDate()
        {
            if (score != null)
                absoluteDate.Text = BmsStrings.LeaderboardDate(score.Date.ToLocalTime(), prefer24HourTime.Value);
        }
    }

    internal partial class StatisticRow : CompositeDrawable
    {
        private readonly OsuSpriteText labelText;
        private readonly OsuSpriteText valueText;

        private readonly Color4? colour;

        public StatisticRow(LocalisableString label, LocalisableString value, Color4? colour = null)
        {
            this.colour = colour;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChildren =
            [
                labelText = new OsuSpriteText
                {
                    Text = label,
                    Font = OsuFont.Style.Caption2.With(weight: FontWeight.SemiBold),
                },
                valueText = new OsuSpriteText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Text = value,
                    Colour = Color4.White,
                    Font = OsuFont.Style.Caption2,
                },
            ];
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            labelText.Colour = colour ?? colourProvider.Content2;
            valueText.Colour = Interpolation.ValueAt(0.85f, colourProvider.Content1, colour ?? colourProvider.Content1, 0, 1);
        }
    }

    internal partial class JudgementRow : StatisticRow
    {
        internal HitResult Result { get; }

        internal int Count { get; }

        internal JudgementRow(HitResult result, int count)
            : base(BmsStrings.ResultJudgement(result), BmsStrings.ResultNumber(count), BmsHitResultColours.ForHitResult(result))
        {
            Result = result;
            Count = count;
        }
    }

    private partial class ModsPanel : CompositeDrawable
    {
        private FillFlowContainer modsFlow = null!;

        public ScoreInfo Score
        {
            set
            {
                modsFlow.ChildrenEnumerable = value.Mods.AsOrdered().Select(mod => new ModIcon(mod, showTooltip: false, showExtendedInformation: true)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Scale = new Vector2(0.32f),
                });

                if (value.Mods.Length > 0)
                    Show();
                else
                    Hide();
            }
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            Name = "Tooltip mods";
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            CornerRadius = corner_radius;
            Masking = true;
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.25f),
                Radius = 4f,
            };

            InternalChildren =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background4,
                },
                modsFlow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Margin = new MarginPadding { Top = spacing + 6, Bottom = 6 },
                    Padding = new MarginPadding { Horizontal = 16 },
                    Spacing = new Vector2(2, -4),
                },
            ];
        }
    }

    internal partial class TotalScoreRankPanel : CompositeDrawable
    {
        private Box rankBackground = null!;
        private Container<BmsDrawableRank> rankContainer = null!;
        private OsuSpriteText totalScore = null!;

        public ScoreInfo Score
        {
            set
            {
                rankBackground.Colour = ColourInfo.GradientVertical(
                    OsuColour.ForRank(value.Rank).Opacity(0f),
                    OsuColour.ForRank(value.Rank).Opacity(0.5f));
                rankContainer.Child = new BmsDrawableRank(value.Rank);
                totalScore.Text = BmsStrings.ResultNumber(BmsExScore.Calculate(value, BmsExScore.Calculate(value.MaximumStatistics)));
            }
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            CornerRadius = corner_radius;
            Masking = true;

            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = Color4.Black.Opacity(0.25f),
                Radius = 4f,
            };

            InternalChildren =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4Extensions.FromHex("#353535"),
                },
                rankBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                },
                rankContainer = new Container<BmsDrawableRank>
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Size = new Vector2(32f, 16f),
                    Margin = new MarginPadding { Bottom = 5 },
                },
                totalScore = new OsuSpriteText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Margin = new MarginPadding { Bottom = 25, Top = spacing + 10 },
                    Font = OsuFont.Style.Subtitle.With(weight: FontWeight.Light, fixedWidth: true),
                    UseFullGlyphHeight = false,
                },
            ];
        }
    }

    private partial class RelativeDate(DateTimeOffset date) : DrawableDate(date, OsuFont.Style.Caption1.Size)
    {
        protected override LocalisableString Format() => BmsStrings.LeaderboardRelativeTime(base.Format());
    }
}
