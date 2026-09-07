// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

internal partial class BmsPanelBeatmapStandalone : BmsBeatmapPanel
{
    public const float HEIGHT = CarouselItem.DEFAULT_HEIGHT * 1.6f;

    [Resolved]
    private OverlayColourProvider colourProvider { get; set; } = null!;

    [Resolved]
    private ISongSelect? songSelect { get; set; }

    private Box backgroundBorder = null!;

    private BeatmapInfo beatmap => CurrentBeatmap!;

    protected override bool BindSelectedToExpanded => true;

    public BmsPanelBeatmapStandalone()
    {
        PanelXOffset = 20;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var difficultyIcon = new ConstrainedIconContainer
        {
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4f, Right = 3f },
            Colour = colourProvider.Background5,
        };

        InitialisePanel(HEIGHT, true, colourProvider, difficultyIcon, backgroundBorder = new Box
        {
            RelativeSizeAxes = Axes.Both,
        });
    }

    protected override void PrepareBeatmap()
    {
        // Carousel reuses a panel by replacing Item and calling PrepareForUse without
        // FreeAfterUse, so clear any previous background request before starting a new one.
        CurrentBeatmap = ((GroupedBeatmap)Item!.Model).Beatmap;
        var beatmapSet = beatmap.BeatmapSet!;
        ScheduleBackgroundRetrieval(beatmap);

        TitleText.Text = new RomanisableString(beatmapSet.Metadata.TitleUnicode, beatmapSet.Metadata.Title);
        ArtistText.Text = new RomanisableString(beatmapSet.Metadata.ArtistUnicode, beatmapSet.Metadata.Artist);
        UpdateButton!.BeatmapSet = beatmapSet;
        StatusPill!.Status = beatmap.Status;

        DifficultyIcon.Icon = beatmap.Ruleset.CreateInstance().CreateIcon();
        DifficultyIcon.Show();

        LocalRank.Beatmap = beatmap;
        DifficultyIcon.Parent!.Alpha = 0;
        LocalRank.AttachLamp((Container)backgroundBorder.Parent!);
        DifficultyText.Text = beatmap.DifficultyName;
        AuthorText.Text = BeatmapsetsStrings.ShowDetailsMappedBy(beatmap.Metadata.Author.Username);

        SpreadDisplayControl.Beatmap.Value = beatmap;
    }

    protected override void UpdateDifficultyColour(osuTK.Graphics.Color4 diffColour)
    {
        backgroundBorder.Colour = diffColour;
        DifficultyIcon.Colour = StarRatingDisplay.DisplayedDifficultyTextColour;
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
