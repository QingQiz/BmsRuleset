using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseStagePanel : Panel
{
    private BmsCourseStage? stage;
    private BmsStandaloneBeatmapContent panelContent = null!;
    private ConstrainedIconContainer difficultyIcon = null!;
    private Container backgroundContainer = null!;
    private Box missingBackground = null!;
    private Color4 availableIconColour;
    private Color4 defaultAccentColour;
    private ScheduledDelegate? scheduledBackgroundRetrieval;
    private CancellationTokenSource? backgroundCancellationSource;
    private IBindable<StarDifficulty>? starDifficultyBindable;
    private CancellationTokenSource? starDifficultyCancellationSource;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    internal BeatmapInfo? ResolvedBeatmap { get; private set; }

    public BmsCourseStagePanel()
    {
        PanelXOffset = 40;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = PanelBeatmapStandalone.HEIGHT;
        AccentColour = defaultAccentColour = colourProvider.Highlight1;
        availableIconColour = colourProvider.Background5;

        Icon = difficultyIcon = new ConstrainedIconContainer
        {
            Icon = new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle },
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = Color4.OrangeRed,
            Alpha = 1,
            AlwaysPresent = true,
        };

        Background = backgroundContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                missingBackground = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Highlight1,
                    Alpha = 0,
                },
            ],
        };
        Content.Child = panelContent = new BmsStandaloneBeatmapContent(Selected, includeSetMetadata: false);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => updateKeyCount());
        mods.BindValueChanged(_ => updateKeyCount(), true);
    }

    protected override void PrepareForUse()
    {
        // Carousel reuse can call PrepareForUse directly when replacing the stage item.
        // Cancel the previous background request before publishing the new stage.
        scheduledBackgroundRetrieval?.Cancel();
        scheduledBackgroundRetrieval = null;
        backgroundCancellationSource?.Cancel();
        backgroundCancellationSource = null;
        panelContent.BeatmapBackground.Beatmap = null;

        resetModelState();
        base.PrepareForUse();

        var model = (BmsGroupedCourseStage)Item!.Model;
        stage = model.Stage;
        ResolvedBeatmap = model.Beatmap;
        Name = $"Course stage {model.StageIndex + 1} song panel";

        var metadata = ResolvedBeatmap?.BeatmapSet?.Metadata ?? ResolvedBeatmap?.Metadata;
        panelContent.TitleText.Text = metadata == null
            ? stage.Title
            : new RomanisableString(metadata.TitleUnicode, metadata.Title);
        panelContent.ArtistText.Text = metadata == null
            ? stage.Artist ?? string.Empty
            : new RomanisableString(metadata.ArtistUnicode, metadata.Artist);
        panelContent.DifficultyText.Text = ResolvedBeatmap?.DifficultyName ?? (stage.IsAvailable ? stage.Difficulty : BmsStrings.CourseStageMissing);
        panelContent.DifficultyText.Colour = stage.IsAvailable ? Color4.White : Color4.OrangeRed;

        difficultyIcon.Icon = ResolvedBeatmap?.Ruleset.CreateInstance().CreateIcon()
                              ?? new SpriteIcon { Icon = FontAwesome.Solid.ExclamationTriangle };
        difficultyIcon.Colour = ResolvedBeatmap != null ? availableIconColour : Color4.OrangeRed;
        difficultyIcon.Alpha = ResolvedBeatmap != null ? 0 : 1;

        panelContent.LocalRank.Beatmap = ResolvedBeatmap;
        if (ResolvedBeatmap != null)
            panelContent.LocalRank.RefreshBmsScores();
        panelContent.LocalRank.Alpha = ResolvedBeatmap != null ? 1 : 0;
        panelContent.LocalRank.AttachLamp(backgroundContainer);
        missingBackground.Alpha = ResolvedBeatmap != null ? 0 : 1;
        panelContent.StarRatingDisplay.Alpha = ResolvedBeatmap != null ? 1 : 0;
        panelContent.SpreadDisplay.Alpha = ResolvedBeatmap != null ? 1 : 0;
        panelContent.SpreadDisplay.Beatmap.Value = ResolvedBeatmap;
        panelContent.KeyCountText.Alpha = 0;
        panelContent.StarRatingDisplay.Current.Value = default;

        if (ResolvedBeatmap != null)
        {
            var backgroundCancellation = backgroundCancellationSource = new CancellationTokenSource();
            scheduledBackgroundRetrieval = Scheduler.AddDelayed(b =>
            {
                if (!ReferenceEquals(ResolvedBeatmap, b))
                    return;

                var working = beatmaps.GetWorkingBeatmap(b);

                if (working is not BmsWorkingBeatmap bmsWorking)
                {
                    panelContent.BeatmapBackground.Beatmap = working;
                    return;
                }

                bmsWorking.PrepareBackgroundMetadataAsync(backgroundCancellation.Token).ContinueWith(task => Scheduler.Add(() =>
                {
                    if (!task.IsCompletedSuccessfully || backgroundCancellation.IsCancellationRequested || IsDisposed
                        || !ReferenceEquals(ResolvedBeatmap, b))
                        return;

                    panelContent.BeatmapBackground.Beatmap = bmsWorking;
                }), TaskScheduler.Default);
            }, ResolvedBeatmap, 50);
        }

        computeStarRating();
        updateKeyCount();
    }

    protected override void FreeAfterUse()
    {
        resetModelState();
        base.FreeAfterUse();
    }

    private void resetModelState()
    {
        scheduledBackgroundRetrieval?.Cancel();
        scheduledBackgroundRetrieval = null;
        backgroundCancellationSource?.Cancel();
        backgroundCancellationSource = null;
        starDifficultyCancellationSource?.Cancel();
        starDifficultyCancellationSource = null;
        starDifficultyBindable = null;
        panelContent.BeatmapBackground.Beatmap = null;
        panelContent.LocalRank.DetachLamp();
        panelContent.LocalRank.Beatmap = null;
        panelContent.LocalRank.Alpha = 0;
        panelContent.SpreadDisplay.Beatmap.Value = null;
        panelContent.SpreadDisplay.StarDifficulty.Value = default;
        panelContent.SpreadDisplay.Current.Colour = Color4.White;
        panelContent.StarRatingDisplay.Current.Value = default;
        panelContent.MainFill.Margin = new MarginPadding();
        panelContent.KeyCountText.Alpha = 0;
        AccentColour = defaultAccentColour;
        ResolvedBeatmap = null;
        stage = null;
    }

    protected override void Update()
    {
        base.Update();

        if (stage == null || ResolvedBeatmap == null)
            return;

        AccentColour = panelContent.StarRatingDisplay.DisplayedDifficultyColour;
        panelContent.SpreadDisplay.Current.Colour = panelContent.StarRatingDisplay.DisplayedDifficultyColour;
        panelContent.MainFill.Margin = new MarginPadding
        {
            Left = 1 / panelContent.LocalRank.Scale.X * (panelContent.LocalRank.HasRank ? 0 : -3),
        };
    }

    protected override void Dispose(bool isDisposing)
    {
        starDifficultyCancellationSource?.Cancel();
        base.Dispose(isDisposing);
    }

    private void computeStarRating()
    {
        starDifficultyCancellationSource?.Cancel();

        if (ResolvedBeatmap == null)
            return;

        var targetBeatmap = ResolvedBeatmap;
        starDifficultyCancellationSource = new CancellationTokenSource();
        starDifficultyBindable = difficultyCache.GetBindableDifficulty(
            targetBeatmap,
            starDifficultyCancellationSource.Token,
            osu.Game.Screens.Select.SongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);
        starDifficultyBindable.BindValueChanged(difficulty =>
        {
            if (!ReferenceEquals(ResolvedBeatmap, targetBeatmap))
                return;

            panelContent.StarRatingDisplay.Current.Value = difficulty.NewValue;

            panelContent.SpreadDisplay.StarDifficulty.Value = difficulty.NewValue;
        }, true);
    }

    private void updateKeyCount()
    {
        if (ResolvedBeatmap == null || stage == null)
            return;

        var rulesetInstance = ruleset.Value.CreateInstance();

        if (rulesetInstance.AvailableVariants.Count() > 1)
        {
            var variant = rulesetInstance.GetVariantForBeatmap(ResolvedBeatmap, mods.Value);
            panelContent.KeyCountText.Alpha = 1;
            panelContent.KeyCountText.Text = LocalisableString.Interpolate($"[{rulesetInstance.GetVariantName(variant)}] ");
        }
        else
            panelContent.KeyCountText.Alpha = 0;
    }

    public override MenuItem[] ContextMenuItems => [];

    protected override bool OnClick(ClickEvent e) => true;

    internal static BeatmapInfo? QueryBeatmap(BeatmapManager beatmaps, string hash) => hash.Length switch
    {
        32 => beatmaps.QueryBeatmap(info => info.MD5Hash == hash),
        64 => beatmaps.QueryBeatmap(info => info.Hash == hash),
        _ => null,
    };
}
