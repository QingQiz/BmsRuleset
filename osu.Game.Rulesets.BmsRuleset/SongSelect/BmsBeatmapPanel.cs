// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal abstract partial class BmsBeatmapPanel : Panel
{
    [Resolved]
    protected IBindable<RulesetInfo> Ruleset { get; private set; } = null!;

    [Resolved]
    protected IBindable<IReadOnlyList<Mod>> Mods { get; private set; } = null!;

    [Resolved]
    protected BeatmapManager Beatmaps { get; private set; } = null!;

    [Resolved]
    protected BeatmapDifficultyCache DifficultyCache { get; private set; } = null!;

    protected PanelSetBackground BeatmapBackground { get; private set; } = null!;

    protected BmsPanelLocalRankDisplay LocalRank { get; private set; } = null!;

    protected OsuSpriteText TitleText { get; private set; } = null!;

    protected OsuSpriteText ArtistText { get; private set; } = null!;

    protected OsuSpriteText KeyCountText { get; private set; } = null!;

    protected OsuSpriteText DifficultyText { get; private set; } = null!;

    protected OsuSpriteText AuthorText { get; private set; } = null!;

    protected StarRatingDisplay StarRatingDisplay { get; private set; } = null!;

    protected BmsPanelBeatmapStandalone.SpreadDisplay SpreadDisplayControl { get; private set; } = null!;

    protected FillFlowContainer MainFill { get; private set; } = null!;

    protected PanelUpdateBeatmapButton? UpdateButton { get; private set; }

    protected BeatmapSetOnlineStatusPill? StatusPill { get; private set; }

    protected ConstrainedIconContainer DifficultyIcon { get; private set; } = null!;

    protected BeatmapInfo? CurrentBeatmap { get; set; }

    private ScheduledDelegate? scheduledBackgroundRetrieval;
    private CancellationTokenSource? backgroundCancellationSource;
    private IBindable<StarDifficulty>? starDifficultyBindable;
    private CancellationTokenSource? starDifficultyCancellationSource;

    protected virtual bool BindSelectedToExpanded => false;

    protected abstract void PrepareBeatmap();

    protected void InitialisePanel(float height, bool includeSetMetadata, OverlayColourProvider colourProvider,
                                   ConstrainedIconContainer difficultyIcon, Drawable background)
    {
        Height = height;
        DifficultyIcon = difficultyIcon;
        Icon = difficultyIcon;
        Background = background;
        BeatmapBackground = new PanelSetBackground();
        LocalRank = new BmsPanelLocalRankDisplay
        {
            Scale = new Vector2(0.8f),
            Origin = Anchor.CentreLeft,
            Anchor = Anchor.CentreLeft,
        };
        SpreadDisplayControl = new BmsPanelBeatmapStandalone.SpreadDisplay
        {
            Origin = Anchor.CentreLeft,
            Anchor = Anchor.CentreLeft,
            Selected = { BindTarget = Selected },
        };

        var details = new FillFlowContainer
        {
            Direction = FillDirection.Horizontal,
            AutoSizeAxes = Axes.Both,
            Padding = new MarginPadding { Top = 2, Bottom = 2 },
        };

        if (includeSetMetadata)
        {
            details.Add(StatusPill = new BeatmapSetOnlineStatusPill
            {
                Animated = false,
                Origin = Anchor.BottomLeft,
                Anchor = Anchor.BottomLeft,
                TextSize = OsuFont.Style.Caption2.Size,
                Margin = new MarginPadding { Right = 4f },
            });
            details.Add(UpdateButton = new PanelUpdateBeatmapButton
            {
                Scale = new Vector2(0.8f),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Margin = new MarginPadding { Right = 4f, Bottom = -1f },
            });
        }

        details.AddRange(
        [
            KeyCountText = new OsuSpriteText
            {
                Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Alpha = 0,
            },
            DifficultyText = new OsuSpriteText
            {
                Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Margin = new MarginPadding { Right = 3f },
            },
        ]);

        if (includeSetMetadata)
        {
            details.Add(AuthorText = new OsuSpriteText
            {
                Colour = colourProvider.Content2,
                Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
            });
        }
        else
            AuthorText = new OsuSpriteText();

        MainFill = new FillFlowContainer
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Direction = FillDirection.Vertical,
            Padding = new MarginPadding { Bottom = 4.8f },
            AutoSizeAxes = Axes.Both,
            Children =
            [
                TitleText = new OsuSpriteText
                {
                    Font = OsuFont.Style.Heading2.With(typeface: Typeface.TorusAlternate, weight: FontWeight.Bold),
                },
                ArtistText = new OsuSpriteText
                {
                    Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                    Padding = new MarginPadding { Top = -2 },
                },
                details,
                new FillFlowContainer
                {
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(3),
                    AutoSizeAxes = Axes.Both,
                    Children =
                    [
                        StarRatingDisplay = new StarRatingDisplay(default, StarRatingDisplaySize.Small, animated: true)
                        {
                            Origin = Anchor.CentreLeft,
                            Anchor = Anchor.CentreLeft,
                            Scale = new Vector2(0.875f),
                        },
                        SpreadDisplayControl,
                    ],
                },
            ],
        };

        Content.Children =
        [
            BeatmapBackground,
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Spacing = new Vector2(5),
                Margin = new MarginPadding { Left = 6.5f },
                Direction = FillDirection.Horizontal,
                Children =
                [
                    LocalRank,
                    MainFill,
                ],
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Ruleset.BindValueChanged(_ => UpdateKeyCount());
        Mods.BindValueChanged(_ => UpdateKeyCount(), true);

        if (BindSelectedToExpanded)
        {
            Selected.BindValueChanged(s => Expanded.Value = s.NewValue, true);
        }
    }

    protected sealed override void PrepareForUse()
    {
        ResetBeatmapState();
        base.PrepareForUse();
        PrepareBeatmap();
        computeStarRating();
        UpdateKeyCount();
    }

    protected sealed override void FreeAfterUse()
    {
        ResetBeatmapState();
        base.FreeAfterUse();
    }

    protected void ScheduleBackgroundRetrieval(BeatmapInfo beatmap)
    {
        backgroundCancellationSource?.Cancel();
        var backgroundCancellation = backgroundCancellationSource = new CancellationTokenSource();
        scheduledBackgroundRetrieval = Scheduler.AddDelayed(b =>
        {
            if (!ReferenceEquals(CurrentBeatmap, b))
                return;

            var working = Beatmaps.GetWorkingBeatmap(b);

            if (working is not BmsWorkingBeatmap bmsWorking)
            {
                BeatmapBackground.Beatmap = working;
                return;
            }

            bmsWorking.PrepareBackgroundMetadataAsync(backgroundCancellation.Token).ContinueWith(task => Scheduler.Add(() =>
            {
                if (!task.IsCompletedSuccessfully || backgroundCancellation.IsCancellationRequested || IsDisposed
                    || !ReferenceEquals(CurrentBeatmap, b))
                    return;

                BeatmapBackground.Beatmap = bmsWorking;
            }), TaskScheduler.Default);
        }, beatmap, 50);
    }

    protected override void Update()
    {
        base.Update();

        if (Item?.IsVisible != true)
        {
            starDifficultyCancellationSource?.Cancel();
            starDifficultyCancellationSource = null;
        }

        if (CurrentBeatmap == null)
            return;

        // Keep the star rating and local rank aligned even when the rank display changes width.
        MainFill.Margin = new MarginPadding
        {
            Left = 1 / StarRatingDisplay.Scale.X * (LocalRank.HasRank ? 0 : -3),
        };

        var diffColour = StarRatingDisplay.DisplayedDifficultyColour;
        AccentColour = diffColour;
        SpreadDisplayControl.Current.Colour = diffColour;
        UpdateDifficultyColour(diffColour);
    }

    protected virtual void UpdateDifficultyColour(Color4 diffColour)
    {
    }

    protected void UpdateKeyCount()
    {
        if (CurrentBeatmap == null)
        {
            if (KeyCountText != null)
                KeyCountText.Alpha = 0;
            return;
        }

        var rulesetInstance = Ruleset.Value.CreateInstance();

        if (rulesetInstance.AvailableVariants.Count() > 1)
        {
            var variant = rulesetInstance.GetVariantForBeatmap(CurrentBeatmap, Mods.Value);
            KeyCountText.Alpha = 1;
            KeyCountText.Text = LocalisableString.Interpolate($"[{rulesetInstance.GetVariantName(variant)}] ");
        }
        else
            KeyCountText.Alpha = 0;
    }

    private void computeStarRating()
    {
        starDifficultyCancellationSource?.Cancel();
        starDifficultyCancellationSource = new CancellationTokenSource();

        if (CurrentBeatmap == null)
            return;

        var targetBeatmap = CurrentBeatmap;
        starDifficultyBindable = DifficultyCache.GetBindableDifficulty(
            targetBeatmap,
            starDifficultyCancellationSource.Token,
            BmsSongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);
        starDifficultyBindable.BindValueChanged(starDifficulty =>
        {
            if (!ReferenceEquals(CurrentBeatmap, targetBeatmap))
                return;

            StarRatingDisplay.Current.Value = starDifficulty.NewValue;
            SpreadDisplayControl.StarDifficulty.Value = starDifficulty.NewValue;
        }, true);
    }

    protected virtual void ResetBeatmapState()
    {
        scheduledBackgroundRetrieval?.Cancel();
        scheduledBackgroundRetrieval = null;
        backgroundCancellationSource?.Cancel();
        backgroundCancellationSource = null;
        starDifficultyCancellationSource?.Cancel();
        starDifficultyCancellationSource = null;
        starDifficultyBindable = null;
        CurrentBeatmap = null;

        if (BeatmapBackground == null)
            return;

        BeatmapBackground.Beatmap = null;
        UpdateButton?.BeatmapSet = null;
        LocalRank.Beatmap = null;
        LocalRank.DetachLamp();
        SpreadDisplayControl.Beatmap.Value = null;
        SpreadDisplayControl.StarDifficulty.Value = default;
        SpreadDisplayControl.Current.Colour = Color4.White;
        StarRatingDisplay.Current.Value = default;
        MainFill.Margin = new MarginPadding();
        KeyCountText.Alpha = 0;
        DifficultyText.Colour = Color4.White;
        DifficultyIcon.Parent?.Alpha = 1;
    }

    protected override void Dispose(bool isDisposing)
    {
        backgroundCancellationSource?.Cancel();
        starDifficultyCancellationSource?.Cancel();
        base.Dispose(isDisposing);
    }
}
