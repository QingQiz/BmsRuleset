// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Presentation adapted from osu.Game.Beatmaps.Drawables.DifficultyIcon and DifficultyIconTooltip.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Scoring;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal sealed partial class BmsResultDifficultyIcon : CompositeDrawable, IHasCustomTooltip<BmsResultDifficultyIcon>
{
    internal readonly Bindable<StarDifficulty> Current = new();

    internal bool ShowTooltip { get; init; } = true;

    private readonly ScoreInfo score;
    private readonly Box background;

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    internal BmsResultDifficultyIcon(ScoreInfo score)
    {
        this.score = score;
        Current.Value = new StarDifficulty(score.BeatmapInfo!.StarRating, 0);
        Size = new Vector2(20);
        InternalChildren =
        [
            new CircularContainer
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                EdgeEffect = new EdgeEffectParameters
                {
                    Colour = Color4.Black.Opacity(0.06f),
                    Type = EdgeEffectType.Shadow,
                    Radius = 3,
                },
                Child = background = new Box { RelativeSizeAxes = Axes.Both },
            },
            new ConstrainedIconContainer
            {
                RelativeSizeAxes = Axes.Both,
                // Results already identify BMS; resolving a native OnlineID would discard the community ruleset icon.
                Icon = new BmsRulesetIcon(),
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Current.BindValueChanged(difficulty => background.FadeColour(colours.ForStarDifficulty(difficulty.NewValue.Stars), 200), true);
        background.FinishTransforms();
    }

    ITooltip<BmsResultDifficultyIcon> IHasCustomTooltip<BmsResultDifficultyIcon>.GetCustomTooltip() => new DifficultyTooltip();

    BmsResultDifficultyIcon IHasCustomTooltip<BmsResultDifficultyIcon>.TooltipContent => ShowTooltip ? this : null!;

    private partial class DifficultyTooltip : VisibilityContainer, ITooltip<BmsResultDifficultyIcon>
    {
        private FillFlowContainer contentFlow = null!;
        private BmsResultDifficultyIcon? displayedContent;

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 5;
            InternalChildren =
            [
                new Box
                {
                    Colour = colours.Gray3,
                    RelativeSizeAxes = Axes.Both,
                },
                contentFlow = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    AutoSizeDuration = 200,
                    AutoSizeEasing = Easing.OutQuint,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(10),
                    Spacing = new Vector2(5),
                },
            ];
        }

        public void SetContent(BmsResultDifficultyIcon content)
        {
            if (ReferenceEquals(displayedContent, content))
                return;

            displayedContent = content;
            var score = content.score;
            var beatmap = score.BeatmapInfo!;
            var rate = ModUtils.CalculateRateWithMods(score.Mods);
            contentFlow.Children =
            [
                new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                    Text = beatmap.DifficultyName,
                },
                new StarRatingDisplay(content.Current.Value, StarRatingDisplaySize.Small)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Current = { BindTarget = content.Current },
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5),
                    ChildrenEnumerable = score.Ruleset.CreateInstance().GetBeatmapAttributesForDisplay(beatmap, score.Mods)
                        .Select(attribute => new OsuSpriteText
                        {
                            Font = OsuFont.Style.Caption1,
                            Text = BmsStrings.DifficultyAttribute(attribute.Acronym, attribute.AdjustedValue),
                        }),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5),
                    Children =
                    [
                        new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 14),
                            Text = BmsStrings.DifficultyLength((beatmap.Length / rate).ToFormattedDuration()),
                        },
                        new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 14),
                            Text = BmsStrings.DifficultyBpm(Math.Round(beatmap.BPM * rate)),
                        },
                    ],
                },
            ];
        }

        public void Move(Vector2 pos) => Position = pos;

        protected override void PopIn() => this.FadeIn(200, Easing.OutQuint);

        protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);
    }
}
