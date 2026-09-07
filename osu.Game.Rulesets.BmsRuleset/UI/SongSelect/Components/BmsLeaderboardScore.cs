// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Adapted from osu! Screens/Select/BeatmapLeaderboardScore.cs at 3c1c96f742e7aae2ff67a7361e058fe91ca3b955.
// A local copy preserves the native composition because the upstream row is sealed.

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
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Backgrounds;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Users;
using osu.Game.Users.Drawables;
using osu.Game.Utils;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

internal sealed partial class BmsLeaderboardScore : OsuClickableContainer, IHasContextMenu, IHasCustomTooltip<ScoreInfo>
{
    public const int HEIGHT = 50;

    public readonly ScoreInfo Score;

    public Bindable<IReadOnlyList<Mod>> SelectedMods { get; } = new([]);

    public int? Rank { get; init; }

    public HighlightType? Highlight { get; init; }

    public Action<ScoreInfo>? ShowReplay { get; init; }

    // Course scores belong to a separate store and must never use ScoreManager's file actions.
    internal Action? DeleteScore { get; init; }

    internal LocalisableString DeleteConfirmation { get; init; } = BmsStrings.LeaderboardDeleteConfirmation;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    [Resolved]
    private IDialogOverlay? dialogOverlay { get; set; }

    [Resolved]
    private ScoreManager scoreManager { get; set; } = null!;

    [Resolved]
    private OsuGame? game { get; set; }

    [Resolved]
    private IAPIProvider api { get; set; } = null!;

    private const float grade_width = 48;
    private const float username_min_width = 120;
    private const float statistics_regular_min_width = 165;
    private const float statistics_compact_min_width = 90;
    private const float rank_label_width = 48;

    private const int corner_radius = 10;
    private const float lamp_width = 4;
    private const int transition_duration = 200;

    private static readonly Color4 personal_best_gradient_left = Color4Extensions.FromHex("#66FFCC");
    private static readonly Color4 personal_best_gradient_right = Color4Extensions.FromHex("#51A388");

    private Colour4 foregroundColour;
    private Colour4 backgroundColour;
    private ColourInfo totalScoreBackgroundGradient;

    private TruncatingSpriteText timestamp = null!;
    private TruncatingSpriteText username = null!;
    private OsuSpriteText totalScoreText = null!;

    private Box background = null!;
    private Box foreground = null!;

    private ClickableAvatar innerAvatar = null!;

    private Container centreContent = null!;
    private Container rightContent = null!;

    private FillFlowContainer<Drawable> modsContainer = null!;

    private Box totalScoreBackground = null!;

    private FillFlowContainer statisticsContainer = null!;
    private Container highlightGradient = null!;
    private Container rankLabelStandalone = null!;
    private Container rankLabelOverlay = null!;

    private readonly bool sheared;

    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
    {
        var inputRectangle = DrawRectangle;

        inputRectangle = inputRectangle.Inflate(new MarginPadding { Vertical = BmsBeatmapLeaderboardWedge.SPACING_BETWEEN_SCORES / 2 });

        return inputRectangle.Contains(ToLocalSpace(screenSpacePos));
    }

    public BmsLeaderboardScore(ScoreInfo score, bool sheared = true)
    {
        Score = score;

        this.sheared = sheared;

        Shear = sheared ? OsuGame.SHEAR : Vector2.Zero;
        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
    }

    [BackgroundDependencyLoader]
    private void load(IRenderer renderer)
    {
        foregroundColour = colourProvider.Background5;
        backgroundColour = colourProvider.Background3;
        totalScoreBackgroundGradient = ColourInfo.GradientHorizontal(backgroundColour.Opacity(0), backgroundColour);

        var nativeContent = new Container
        {
            Masking = true,
            CornerRadius = corner_radius,
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                background = new Box
                {
                    Alpha = 0.4f,
                    RelativeSizeAxes = Axes.Both,
                    Colour = backgroundColour,
                },
                rankLabelStandalone = new Container
                {
                    Width = rank_label_width,
                    RelativeSizeAxes = Axes.Y,
                    Children =
                    [
                        highlightGradient = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Right = -10f },
                            Alpha = Highlight != null ? 1 : 0,
                            Colour = getHighlightColour(Highlight),
                            Child = new Box { RelativeSizeAxes = Axes.Both },
                        },
                        new RankLabel(Rank, sheared, darkText: Highlight == HighlightType.Own)
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                    ],
                },
                centreContent = new Container
                {
                    Name = @"Centre container",
                    RelativeSizeAxes = Axes.Both,
                    Child = new Container
                    {
                        Masking = true,
                        CornerRadius = corner_radius,
                        RelativeSizeAxes = Axes.Both,
                        Children =
                        [
                            foreground = new Box
                            {
                                Alpha = 0.4f,
                                RelativeSizeAxes = Axes.Both,
                                Colour = foregroundColour,
                            },
                            new UserCoverBackground
                            {
                                RelativeSizeAxes = Axes.Both,
                                User = Score.User,
                                Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Colour = ColourInfo.GradientHorizontal(Colour4.White.Opacity(0.5f), Colour4.FromHex(@"222A27").Opacity(1)),
                            },
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ColumnDimensions =
                                [
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(),
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(GridSizeMode.AutoSize),
                                ],
                                Content = (Drawable[][])
                                [
                                    [
                                        new Container
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            CornerRadius = corner_radius,
                                            Masking = true,
                                            Children =
                                            [
                                                new DelayedLoadWrapper(innerAvatar = new ClickableAvatar(Score.User)
                                                {
                                                    Anchor = Anchor.Centre,
                                                    Origin = Anchor.Centre,
                                                    Scale = new Vector2(1.1f),
                                                    Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                                    RelativeSizeAxes = Axes.Both,
                                                })
                                                {
                                                    RelativeSizeAxes = Axes.None,
                                                    Size = new Vector2(HEIGHT),
                                                },
                                                rankLabelOverlay = new Container
                                                {
                                                    Name = "Leaderboard rank overlay",
                                                    RelativeSizeAxes = Axes.Both,
                                                    Alpha = 0,
                                                    Children =
                                                    [
                                                        new Box
                                                        {
                                                            RelativeSizeAxes = Axes.Both,
                                                            Colour = Colour4.Black.Opacity(0.5f),
                                                        },
                                                        new RankLabel(Rank, sheared, false)
                                                        {
                                                            AutoSizeAxes = Axes.Both,
                                                            Anchor = Anchor.Centre,
                                                            Origin = Anchor.Centre,
                                                        },
                                                    ],
                                                },
                                            ],
                                        },
                                        new FillFlowContainer
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Padding = new MarginPadding { Horizontal = corner_radius },
                                            Children =
                                            [
                                                new FillFlowContainer
                                                {
                                                    Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                                    Direction = FillDirection.Horizontal,
                                                    Spacing = new Vector2(5),
                                                    AutoSizeAxes = Axes.Both,
                                                    Masking = true,
                                                    Children =
                                                    [
                                                        new UpdateableFlag(Score.User.CountryCode)
                                                        {
                                                            Anchor = Anchor.CentreLeft,
                                                            Origin = Anchor.CentreLeft,
                                                            Size = new Vector2(20, 14),
                                                        },
                                                        new UpdateableTeamFlag(Score.User.Team)
                                                        {
                                                            Anchor = Anchor.CentreLeft,
                                                            Origin = Anchor.CentreLeft,
                                                            Size = new Vector2(30, 15),
                                                        },
                                                        timestamp = new TruncatingSpriteText
                                                        {
                                                            Name = "Leaderboard timestamp",
                                                            Font = OsuFont.Style.Caption1.With(weight: FontWeight.SemiBold),
                                                            ShowTooltip = false,
                                                            Anchor = Anchor.CentreLeft,
                                                            Origin = Anchor.CentreLeft,
                                                            Colour = colourProvider.Content2,
                                                            UseFullGlyphHeight = false,
                                                        },
                                                    ],
                                                },
                                                username = new TruncatingSpriteText
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                                    Name = "Leaderboard username",
                                                    Text = Score.User.Username,
                                                    Font = OsuFont.Style.Heading2,
                                                },
                                            ],
                                        },
                                        modsContainer = new FillFlowContainer<Drawable>
                                        {
                                            Name = "Leaderboard mods",
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            AutoSizeAxes = Axes.Both,
                                            Direction = FillDirection.Horizontal,
                                            Spacing = new Vector2(2, 0),
                                            Padding = new MarginPadding { Right = 10 },
                                            Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                        },
                                        new Container
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Child = statisticsContainer = new FillFlowContainer
                                            {
                                                Name = @"Statistics container",
                                                Padding = new MarginPadding { Right = 10 },
                                                Spacing = new Vector2(20, 0),
                                                Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                                Anchor = Anchor.CentreRight,
                                                Origin = Anchor.CentreRight,
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Children =
                                                [
                                                    new ScoreComponentLabel(BmsStrings.MaxCombo, BmsStrings.ResultNumber(Score.MaxCombo),
                                                        Score.MaxCombo == Score.GetMaximumAchievableCombo(), 60),
                                                    new ScoreComponentLabel(BmsStrings.ResultAccuracy, BmsStrings.ResultPercentage(Score.Accuracy), Score.Accuracy == 1,
                                                        55),
                                                ],
                                                Alpha = 0,
                                            },
                                        },
                                    ],
                                ],
                            },
                        ],
                    },
                },
                rightContent = new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Name = @"Right content",
                    RelativeSizeAxes = Axes.Y,
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Children =
                        [
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Right = grade_width },
                                Child = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = ColourInfo.GradientHorizontal(backgroundColour.Opacity(0), OsuColour.ForRank(Score.Rank)),
                                },
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Y,
                                Width = grade_width,
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                Colour = OsuColour.ForRank(Score.Rank),
                            },
                            new TrianglesV2
                            {
                                Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                SpawnRatio = 2,
                                Velocity = 0.7f,
                                Colour = ColourInfo.GradientHorizontal(backgroundColour.Opacity(0), OsuColour.ForRank(Score.Rank).Darken(0.2f)),
                            },
                            new Container
                            {
                                Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                RelativeSizeAxes = Axes.Y,
                                Name = "Leaderboard grade badge",
                                Width = grade_width,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Name = "Leaderboard grade",
                                    Colour = DrawableRank.GetRankLetterColour(Score.Rank),
                                    Font = OsuFont.Numeric.With(size: 14),
                                    Text = BmsStrings.LeaderboardGrade(BmsRankDisplay.GetRankLetter(Score.Rank)),
                                    ShadowColour = Color4.Black.Opacity(0.3f),
                                    ShadowOffset = new Vector2(0, 0.08f),
                                    Shadow = true,
                                    UseFullGlyphHeight = false,
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Padding = new MarginPadding { Right = grade_width },
                                Child = new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = true,
                                    CornerRadius = corner_radius,
                                    Children =
                                    [
                                        totalScoreBackground = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = totalScoreBackgroundGradient,
                                        },
                                        new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Colour = ColourInfo.GradientHorizontal(backgroundColour.Opacity(0), OsuColour.ForRank(Score.Rank).Opacity(0.5f)),
                                        },
                                        new FillFlowContainer
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Direction = FillDirection.Vertical,
                                            Padding = new MarginPadding { Horizontal = corner_radius },
                                            Spacing = new Vector2(0f, -2f),
                                            Children =
                                            [
                                                totalScoreText = new OsuSpriteText
                                                {
                                                    Anchor = Anchor.TopRight,
                                                    Origin = Anchor.TopRight,
                                                    UseFullGlyphHeight = false,
                                                    Name = "Leaderboard EXSCORE",
                                                    Text = BmsStrings.ResultNumber(BmsExScore.Calculate(Score, BmsExScore.Calculate(Score.MaximumStatistics))),
                                                    Font = OsuFont.Style.Subtitle.With(weight: FontWeight.Light, fixedWidth: true),
                                                    Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                                                },
                                            ],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                },
            ],
        };
        Child = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = corner_radius,
            Children =
            [
                new BmsLampDisplay(BmsLampCalculator.Calculate(Score), createLampTexture(renderer))
                {
                    RelativeSizeAxes = Axes.Y,
                    Height = 1,
                    Width = corner_radius + lamp_width,
                },
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = lamp_width },
                    Child = nativeContent,
                },
            ],
        };
        innerAvatar.OnLoadComplete += d => d.FadeInFromZero(200);
    }

    private static Texture createLampTexture(IRenderer renderer)
    {
        const int resolution = 4;
        var image = new Image<Rgba32>((corner_radius + (int)lamp_width) * resolution, HEIGHT * resolution);

        // Only the strip between the two translated corner arcs is opaque; no lamp pixels sit beneath the native background.
        for (var y = 0; y < image.Height; y++)
        {
            var localY = (y + 0.5f) / resolution;
            var cornerY = Math.Max(0, corner_radius - Math.Min(localY, HEIGHT - localY));
            var left = corner_radius - MathF.Sqrt(corner_radius * corner_radius - cornerY * cornerY);
            for (var x = 0; x < image.Width; x++)
            {
                var localX = (x + 0.5f) / resolution;
                var alpha = Math.Clamp(Math.Min(localX - left, left + lamp_width - localX) * resolution + 0.5f, 0, 1);
                image[x, y] = new Rgba32(1f, 1f, 1f, alpha);
            }
        }

        var texture = new DisposableTexture(renderer.CreateTexture(image.Width, image.Height));
        texture.SetData(new TextureUpload(image));
        return texture;
    }

    private ColourInfo getHighlightColour(HighlightType? highlightType, float lightenAmount = 0)
    {
        switch (highlightType)
        {
            case HighlightType.Own:
                return ColourInfo.GradientHorizontal(personal_best_gradient_left.Lighten(lightenAmount), personal_best_gradient_right.Lighten(lightenAmount));

            case HighlightType.Friend:
                return ColourInfo.GradientHorizontal(colours.Pink1.Lighten(lightenAmount), colours.Pink3.Lighten(lightenAmount));

            default:
                return Colour4.White;
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        updateTimestamp();
        Scheduler.AddDelayed(updateTimestamp, 1000, true);
        rightContent.Width = 150;
        modsContainer.ChildrenEnumerable = Score.Mods.AsOrdered().Select(mod => new ModIcon(mod, showTooltip: false, showExtendedInformation: true)
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Scale = new Vector2(0.32f),
        });
    }

    private void updateTimestamp() => timestamp.Text = BmsStrings.LeaderboardTimeAgo(Score.Date, DateTimeOffset.UtcNow);

    protected override bool OnHover(HoverEvent e)
    {
        updateState();
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        updateState();
        base.OnHoverLost(e);
    }

    private void updateState()
    {
        var lightenedGradient = ColourInfo.GradientHorizontal(backgroundColour.Opacity(0).Lighten(0.2f), backgroundColour.Lighten(0.2f));

        foreground.FadeColour(IsHovered ? foregroundColour.Lighten(0.2f) : foregroundColour, transition_duration, Easing.OutQuint);
        background.FadeColour(IsHovered ? backgroundColour.Lighten(0.2f) : backgroundColour, transition_duration, Easing.OutQuint);
        totalScoreBackground.FadeColour(IsHovered ? lightenedGradient : totalScoreBackgroundGradient, transition_duration, Easing.OutQuint);
        highlightGradient.FadeColour(getHighlightColour(Highlight, IsHovered ? 0.2f : 0), transition_duration, Easing.OutQuint);

        if (IsHovered && currentMode != DisplayMode.Full)
            rankLabelOverlay.FadeIn(transition_duration, Easing.OutQuint);
        else
            rankLabelOverlay.FadeOut(transition_duration, Easing.OutQuint);
    }

    private DisplayMode? currentMode;

    protected override void Update()
    {
        base.Update();

        timestamp.MaxWidth = Math.Max(0, username.DrawWidth - timestamp.X);
        timestamp.Alpha = timestamp.MaxWidth >= 50 ? 1 : 0;
        modsContainer.Scale = new Vector2(Math.Min(1, DrawWidth * 0.25f / Math.Max(1, modsContainer.Width)));

        var mode = getCurrentDisplayMode();

        totalScoreText.Scale = new Vector2(mode == DisplayMode.Minimal ? 0.8f : 1);
        rightContent.Width = Math.Max(mode == DisplayMode.Minimal ? 120 : 150,
            totalScoreText.DrawWidth * totalScoreText.Scale.X + grade_width + corner_radius * 2);

        if (currentMode != mode)
            updateDisplayMode(mode);

        centreContent.Padding = new MarginPadding
        {
            Left = rankLabelStandalone.DrawWidth,
            Right = rightContent.DrawWidth,
        };
    }

    private void updateDisplayMode(DisplayMode mode)
    {
        var duration = currentMode == null ? 0 : transition_duration;
        if (mode >= DisplayMode.Full)
            rankLabelStandalone.FadeIn(duration, Easing.OutQuint).ResizeWidthTo(rank_label_width, duration, Easing.OutQuint);
        else
            rankLabelStandalone.FadeOut(duration, Easing.OutQuint).ResizeWidthTo(0, duration, Easing.OutQuint);

        if (mode >= DisplayMode.Regular)
        {
            statisticsContainer.FadeIn(duration, Easing.OutQuint).MoveToX(0, duration, Easing.OutQuint);
            statisticsContainer.Direction = FillDirection.Horizontal;
            statisticsContainer.ScaleTo(1, duration, Easing.OutQuint);
        }
        else if (mode >= DisplayMode.Compact)
        {
            statisticsContainer.FadeIn(duration, Easing.OutQuint).MoveToX(0, duration, Easing.OutQuint);
            statisticsContainer.Direction = FillDirection.Vertical;
            statisticsContainer.ScaleTo(0.8f, duration, Easing.OutQuint);
        }
        else
            statisticsContainer.FadeOut(duration, Easing.OutQuint).MoveToX(statisticsContainer.DrawWidth, duration, Easing.OutQuint);

        currentMode = mode;
        updateState();
    }

    private DisplayMode getCurrentDisplayMode()
    {
        // Use the unscaled score width so compact scaling cannot toggle the display mode each frame.
        var availableWidth = DrawWidth - lamp_width - modsContainer.DrawWidth * modsContainer.Scale.X
                             - Math.Max(150, totalScoreText.DrawWidth + grade_width + corner_radius * 2);
        if (availableWidth >= username_min_width + statistics_regular_min_width + rank_label_width)
            return DisplayMode.Full;

        if (availableWidth >= username_min_width + statistics_regular_min_width)
            return DisplayMode.Regular;

        if (availableWidth >= username_min_width + statistics_compact_min_width)
            return DisplayMode.Compact;

        return DisplayMode.Minimal;
    }

    ITooltip<ScoreInfo> IHasCustomTooltip<ScoreInfo>.GetCustomTooltip() => new BmsLeaderboardScoreTooltip(colourProvider);

    ScoreInfo IHasCustomTooltip<ScoreInfo>.TooltipContent => Score;

    MenuItem[] IHasContextMenu.ContextMenuItems
    {
        get
        {
            var items = new List<MenuItem>();
            var copyableMods = Score.Mods.Where(mod => mod.Type != ModType.System).ToArray();
            if (copyableMods.Length > 0)
                items.Add(new OsuMenuItem(BmsStrings.LeaderboardUseMods, MenuItemType.Highlighted,
                    () => SelectedMods.Value = copyableMods.Select(mod => mod.DeepClone()).ToArray()));

            if (DeleteScore == null && Score.OnlineID > 0)
                items.Add(new OsuMenuItem(BmsStrings.LeaderboardCopyLink, MenuItemType.Standard,
                    () => game?.CopyToClipboard($@"{api.Endpoints.WebsiteUrl}/scores/{Score.OnlineID}")));

            if (DeleteScore == null && Score.Files.Count == 0)
                return items.ToArray();

            if (items.Count > 0)
                items.Add(new OsuMenuItemSpacer());

            if (DeleteScore == null)
            {
                if (ShowReplay != null)
                    items.Add(new OsuMenuItem(BmsStrings.LeaderboardWatchReplay, MenuItemType.Standard, () => ShowReplay(Score)));
                items.Add(new OsuMenuItem(BmsStrings.LeaderboardExport, MenuItemType.Standard, () => scoreManager.Export(Score)));
            }

            items.Add(new OsuMenuItem(BmsStrings.LeaderboardDelete, MenuItemType.Destructive,
                () => dialogOverlay?.Push(new ScoreDeleteDialog(DeleteConfirmation, DeleteScore ?? (() => scoreManager.Delete(Score))))));
            return items.ToArray();
        }
    }

    private enum DisplayMode
    {
        Minimal,
        Compact,
        Regular,
        Full,
    }

    private partial class ScoreComponentLabel(LocalisableString name, LocalisableString value, bool perfect, float minWidth)
        : Container
    {
        private FillFlowContainer content = null!;

        public override bool Contains(Vector2 screenSpacePos) => content.Contains(screenSpacePos);

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, OverlayColourProvider colourProvider)
        {
            AutoSizeAxes = Axes.Both;
            Child = content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Children =
                [
                    new OsuSpriteText
                    {
                        Colour = colourProvider.Content2,
                        Text = name,
                        Font = OsuFont.Style.Caption2.With(weight: FontWeight.SemiBold),
                    },
                    new OsuSpriteText
                    {
                        // Keep the column width stable when the formatted value is wider than its label.
                        BypassAutoSizeAxes = Axes.X,
                        Text = value,
                        Font = OsuFont.Style.Body,
                        Colour = perfect ? colours.Lime1 : Color4.White,
                    },
                    Empty().With(d => d.Width = minWidth),
                ],
            };
        }
    }

    private partial class RankLabel : Container, IHasTooltip
    {
        private readonly bool darkText;
        private readonly OsuSpriteText text;

        public RankLabel(int? rank, bool sheared, bool darkText)
        {
            this.darkText = darkText;
            if (rank >= 1000)
                TooltipText = BmsStrings.LeaderboardPosition(rank.Value);

            Child = text = new TruncatingSpriteText
            {
                Shear = sheared ? -OsuGame.SHEAR : Vector2.Zero,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = OsuFont.Style.Heading2,
                Text = rank.HasValue ? BmsStrings.LeaderboardPosition(rank.Value.FormatRank()) : BmsStrings.LeaderboardUnknown,
                MaxWidth = rank_label_width - 8,
                Shadow = !darkText,
            };
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            text.Colour = darkText ? colourProvider.Background3 : colourProvider.Content1;
        }

        public LocalisableString TooltipText { get; }
    }

    private partial class ScoreDeleteDialog : DeletionDialog
    {
        internal ScoreDeleteDialog(LocalisableString confirmation, Action delete)
        {
            BodyText = confirmation;
            DangerousAction = delete;
        }
    }

    public enum HighlightType
    {
        Own,
        Friend,
    }
}
