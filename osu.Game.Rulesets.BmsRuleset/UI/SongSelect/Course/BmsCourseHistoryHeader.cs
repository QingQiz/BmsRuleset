using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.Leaderboards;
using osu.Game.Screens.Play.Leaderboards;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class BmsCourseHistoryHeader : CompositeDrawable
{
    private ShearedDropdown<LeaderboardSortMode> sortDropdown = null!;
    private ShearedToggleButton selectedModsToggle = null!;

    internal IBindable<LeaderboardSortMode> Sorting => sortDropdown.Current;

    internal IBindable<bool> FilterBySelectedMods => selectedModsToggle.Active;

    [BackgroundDependencyLoader]
    private void load(OsuConfigManager config)
    {
        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Left = Screens.Select.SongSelect.WEDGE_CONTENT_MARGIN, Right = 5 },
            Child = new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.X,
                Height = 30,
                Spacing = new Vector2(5),
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Left = 258 },
                Children =
                [
                    selectedModsToggle = new ShearedToggleButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        AutoSizeAxes = Axes.X,
                        Text = UserInterfaceStrings.SelectedMods,
                        Height = 30,
                        Margin = new MarginPadding { Left = -9.2f },
                    },
                    sortDropdown = new ShearedDropdown<LeaderboardSortMode>(BeatmapLeaderboardWedgeStrings.Sort)
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.4f,
                        Items = Enum.GetValues<LeaderboardSortMode>(),
                    },
                    new CourseScopeDropdown
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.4f,
                        Current = { Value = BeatmapLeaderboardScope.Local },
                    },
                ],
            },
        };

        config.BindWith(OsuSetting.BeatmapLeaderboardSortMode, sortDropdown.Current);
        config.BindWith(OsuSetting.BeatmapDetailModsFilter, selectedModsToggle.Active);
    }

    private partial class CourseScopeDropdown : ShearedDropdown<BeatmapLeaderboardScope>
    {
        internal CourseScopeDropdown()
            : base(BeatmapLeaderboardWedgeStrings.Scope)
        {
            Items = [BeatmapLeaderboardScope.Local];
        }

        protected override LocalisableString GenerateItemText(BeatmapLeaderboardScope item) => item.GetLocalisableDescription();
    }
}