using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osuTK;
using CommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class BmsCourseHistoryScore : CompositeDrawable, IHasContextMenu
{
    private readonly ScoreInfo score;
    private readonly Action<ScoreInfo> presentScore;
    private readonly Action deleteScore;
    private readonly Bindable<IReadOnlyList<Mod>> selectedMods = new();

    [Resolved]
    private IDialogOverlay? dialogOverlay { get; set; }

    internal BmsCourseHistoryScore(
        ScoreInfo score,
        int rank,
        Action<ScoreInfo> presentScore,
        Action deleteScore,
        IBindable<IReadOnlyList<Mod>> selectedMods)
    {
        this.score = score;
        this.presentScore = presentScore;
        this.deleteScore = deleteScore;
        RelativeSizeAxes = Axes.X;
        Height = BeatmapLeaderboardScore.HEIGHT;
        Shear = Vector2.Zero;

        var scoreDisplay = new BeatmapLeaderboardScore(score)
        {
            Rank = rank,
            Shear = Vector2.Zero,
            Action = () => presentScore(score),
        }.WithBmsRank();

        ((IBindable<IReadOnlyList<Mod>>)this.selectedMods).BindTo(selectedMods);
        ((IBindable<IReadOnlyList<Mod>>)scoreDisplay.SelectedMods).BindTo(this.selectedMods);
        InternalChild = new InputBlockedContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = scoreDisplay,
        };
    }

    protected override bool OnClick(ClickEvent e)
    {
        presentScore(score);
        return true;
    }

    MenuItem[] IHasContextMenu.ContextMenuItems
    {
        get
        {
            var items = new List<MenuItem>();
            var copyableMods = score.Mods.Where(mod => mod.Type != ModType.System).ToArray();

            if (copyableMods.Length > 0)
                items.Add(new OsuMenuItem(SongSelectStrings.UseTheseMods, MenuItemType.Highlighted, () => selectedMods.Value = copyableMods));

            if (items.Count > 0)
                items.Add(new OsuMenuItemSpacer());

            items.Add(new OsuMenuItem(CommonStrings.ButtonsDelete, MenuItemType.Destructive, () => dialogOverlay?.Push(new BmsCourseResultDeleteDialog(deleteScore))));
            return items.ToArray();
        }
    }

    internal partial class BmsCourseResultDeleteDialog : DeletionDialog
    {
        internal BmsCourseResultDeleteDialog(Action deleteScore)
        {
            BodyText = BmsStrings.CourseHistoryDeleteConfirmation;
            DangerousAction = deleteScore;
        }
    }
}
