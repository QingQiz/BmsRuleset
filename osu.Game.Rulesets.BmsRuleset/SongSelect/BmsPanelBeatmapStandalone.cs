// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsPanelBeatmapStandalone : Panel
{
    public const float HEIGHT = CarouselItem.DEFAULT_HEIGHT * 1.6f;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    [Resolved]
    private ISongSelect? songSelect { get; set; }

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

    private IBindable<StarDifficulty>? starDifficultyBindable;
    private CancellationTokenSource? starDifficultyCancellationSource;

    private ScheduledDelegate? scheduledBackgroundRetrieval;

    private ConstrainedIconContainer difficultyIcon = null!;
    private BmsStandaloneBeatmapContent panelContent = null!;

    private Box backgroundBorder = null!;

    private BeatmapInfo beatmap => ((GroupedBeatmap)Item!.Model).Beatmap;

    public BmsPanelBeatmapStandalone()
    {
        PanelXOffset = 20;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Height = HEIGHT;

        Icon = difficultyIcon = new ConstrainedIconContainer
        {
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4f, Right = 3f },
            Colour = colourProvider.Background5,
        };

        Background = backgroundBorder = new Box
        {
            RelativeSizeAxes = Axes.Both,
        };

        Content.Child = panelContent = new BmsStandaloneBeatmapContent(Selected, includeSetMetadata: true);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => updateKeyCount());
        mods.BindValueChanged(_ => updateKeyCount(), true);

        Selected.BindValueChanged(s =>
        {
            Expanded.Value = s.NewValue;
        }, true);
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        var beatmapSet = beatmap.BeatmapSet!;

        scheduledBackgroundRetrieval = Scheduler.AddDelayed(b => panelContent.BeatmapBackground.Beatmap = beatmaps.GetWorkingBeatmap(b), beatmap, 50);

        panelContent.TitleText.Text = new RomanisableString(beatmapSet.Metadata.TitleUnicode, beatmapSet.Metadata.Title);
        panelContent.ArtistText.Text = new RomanisableString(beatmapSet.Metadata.ArtistUnicode, beatmapSet.Metadata.Artist);
        panelContent.UpdateButton!.BeatmapSet = beatmapSet;
        panelContent.StatusPill!.Status = beatmap.Status;

        difficultyIcon.Icon = beatmap.Ruleset.CreateInstance().CreateIcon();
        difficultyIcon.Show();

        panelContent.LocalRank.Beatmap = beatmap;
        panelContent.LocalRank.RefreshBmsScores();
        difficultyIcon.Parent!.Alpha = 0;
        panelContent.LocalRank.AttachLamp((Container)backgroundBorder.Parent!);
        panelContent.DifficultyText.Text = beatmap.DifficultyName;
        panelContent.AuthorText.Text = BeatmapsetsStrings.ShowDetailsMappedBy(beatmap.Metadata.Author.Username);

        computeStarRating();
        panelContent.SpreadDisplay.Beatmap.Value = beatmap;
        updateKeyCount();
    }

    protected override void FreeAfterUse()
    {
        base.FreeAfterUse();

        scheduledBackgroundRetrieval?.Cancel();
        scheduledBackgroundRetrieval = null;
        panelContent.BeatmapBackground.Beatmap = null;
        panelContent.UpdateButton!.BeatmapSet = null;
        panelContent.LocalRank.Beatmap = null;
        difficultyIcon.Parent!.Alpha = 1;
        panelContent.LocalRank.DetachLamp();
        starDifficultyBindable = null;
        panelContent.SpreadDisplay.Beatmap.Value = null;

        starDifficultyCancellationSource?.Cancel();
    }

    private void computeStarRating()
    {
        starDifficultyCancellationSource?.Cancel();
        starDifficultyCancellationSource = new CancellationTokenSource();

        if (Item == null)
            return;

        starDifficultyBindable = difficultyCache.GetBindableDifficulty(beatmap, starDifficultyCancellationSource.Token, BmsSongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);
        starDifficultyBindable.BindValueChanged(starDifficulty =>
        {
            panelContent.StarRatingDisplay.Current.Value = starDifficulty.NewValue;
            panelContent.SpreadDisplay.StarDifficulty.Value = starDifficulty.NewValue;
        }, true);
    }

    protected override void Update()
    {
        base.Update();

        if (Item?.IsVisible != true)
        {
            starDifficultyCancellationSource?.Cancel();
            starDifficultyCancellationSource = null;
        }

        // Dirty hack to make sure we don't take up spacing in parent fill flow when not displaying a rank.
        // I can't find a better way to do this.
        panelContent.MainFill.Margin = new MarginPadding { Left = 1 / panelContent.StarRatingDisplay.Scale.X * (panelContent.LocalRank.HasRank ? 0 : -3) };

        var diffColour = panelContent.StarRatingDisplay.DisplayedDifficultyColour;

        AccentColour = diffColour;
        panelContent.SpreadDisplay.Current.Colour = diffColour;

        backgroundBorder.Colour = diffColour;
        difficultyIcon.Colour = panelContent.StarRatingDisplay.DisplayedDifficultyTextColour;
    }

    private void updateKeyCount()
    {
        if (Item == null)
            return;

        var rulesetInstance = ruleset.Value.CreateInstance();

        if (rulesetInstance.AvailableVariants.Count() > 1)
        {
            var variant = rulesetInstance.GetVariantForBeatmap(beatmap, mods.Value);
            var variantName = rulesetInstance.GetVariantName(variant);

            panelContent.KeyCountText.Alpha = 1;
            panelContent.KeyCountText.Text = LocalisableString.Interpolate($"[{variantName}] ");
        }
        else
            panelContent.KeyCountText.Alpha = 0;
    }

    public override MenuItem[] ContextMenuItems
    {
        get
        {
            if (Item == null)
                return [];

            List<MenuItem> items = [];

            if (songSelect != null)
                items.AddRange(songSelect.GetForwardActions(beatmap));

            return items.ToArray();
        }
    }
}