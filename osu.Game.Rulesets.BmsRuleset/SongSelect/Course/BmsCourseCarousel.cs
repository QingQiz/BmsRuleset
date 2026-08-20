using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

[Cached]
internal partial class BmsCourseCarousel : Carousel<BmsCourseDefinition>
{
    internal event Action<BmsCourseDefinition?>? CourseSelected;

    internal event Action? CourseActivated;

    internal event Action<int>? MatchesChanged;

    internal BmsCourseDefinition? SelectedCourse => (CurrentSelection as BmsGroupedCourse)?.Course;

    internal BmsCourseDefinition? KeyboardSelectedCourse => (keyboardSelectedModel as BmsGroupedCourse)?.Course;

    internal BmsCourseTableGroup? KeyboardSelectedGroup => keyboardSelectedModel as BmsCourseTableGroup;

    internal BmsCourseTableGroup? ExpandedGroup { get; private set; }

    private readonly BmsCourseCarouselFilter filter;
    private readonly DrawablePool<BmsCoursePanel> coursePanelPool = new(100);
    private readonly DrawablePool<BmsCourseTablePanel> tablePanelPool = new(20);
    private readonly DrawablePool<BmsCourseStagePanel> stagePanelPool = new(20);

    private object? keyboardSelectedModel;
    private BmsGroupedCourse? pendingKeyboardSelection;
    private string searchTerm = string.Empty;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    internal BmsCourseCarousel()
    {
        DebounceDelay = 100;
        DistanceOffscreenToPreload = 100;
        Scroll.ScrollbarPaddingBottom = 70;

        Filters =
        [
            filter = new BmsCourseCarouselFilter(() => searchTerm, resolveStageBeatmap),
        ];

        AddInternal(tablePanelPool);
        AddInternal(coursePanelPool);
        AddInternal(stagePanelPool);
    }

    internal void SetCourses(IEnumerable<BmsCourseDefinition> courses) => Items.ReplaceRange(0, Items.Count, courses);

    internal void Search(string term)
    {
        if (searchTerm == term)
            return;

        searchTerm = term;
        FilterAsync();
    }

    internal void Refresh() => Schedule(() => FilterAsync());

    internal IReadOnlyList<BeatmapInfo> GetResolvedBeatmaps(string courseId) => filter.GroupItems
        .SelectMany(pair => pair.Value)
        .Where(items => items.Course.Course.Id == courseId)
        .SelectMany(items => items.StageItems)
        .Select(item => ((BmsGroupedCourseStage)item.Model).Beatmap)
        .OfType<BeatmapInfo>()
        .DistinctBy(beatmap => beatmap.Hash)
        .ToArray();

    internal bool MoveKeyboardSelection(KeyBindingPressEvent<GlobalAction> e, int direction)
    {
        var courses = GetCarouselItems()?
            .Where(item => item.IsVisible && item.Model is BmsGroupedCourse)
            .ToArray() ?? [];

        if (courses.Length == 0)
            return false;

        var currentId = KeyboardSelectedCourse?.Id ?? SelectedCourse?.Id;
        var currentIndex = Array.FindIndex(courses, item => item.Model is BmsGroupedCourse grouped
                                                            && grouped.Course.Id == currentId);
        var nextIndex = currentIndex < 0
            ? (direction > 0 ? 0 : courses.Length - 1)
            : (currentIndex + direction + courses.Length) % courses.Length;

        pendingKeyboardSelection = (BmsGroupedCourse)courses[nextIndex].Model;
        keyboardSelectedModel = pendingKeyboardSelection;

        // Let the base carousel retain its traversal sound, scrolling and deferred input behaviour.
        return base.OnPressed(e);
    }

    protected override float GetSpacingBetweenPanels(CarouselItem top, CarouselItem bottom)
    {
        if (top.Model is BmsCourseTableGroup ^ bottom.Model is BmsCourseTableGroup)
            return BeatmapCarousel.SPACING * 2;

        if (bottom.Model is BmsGroupedCourse && bottom.IsExpanded)
            return BeatmapCarousel.SPACING * 2;

        if (top.Model is BmsGroupedCourse && top.IsExpanded && bottom.Model is not BmsGroupedCourseStage)
            return BeatmapCarousel.SPACING * 2;

        if (top.Model is BmsGroupedCourseStage && bottom.Model is BmsGroupedCourse)
            return BeatmapCarousel.SPACING * 2;

        return -BeatmapCarousel.SPACING;
    }

    protected override Drawable GetDrawableForDisplay(CarouselItem item)
    {
        switch (item.Model)
        {
            case BmsCourseTableGroup:
                var tablePanel = tablePanelPool.Get();
                tablePanel.CourseCarousel = this;
                return tablePanel;

            case BmsGroupedCourse:
                var coursePanel = coursePanelPool.Get();
                coursePanel.CourseCarousel = this;
                return coursePanel;

            case BmsGroupedCourseStage:
                return stagePanelPool.Get();

            default:
                throw new InvalidOperationException($"Unsupported BMS course carousel model {item.Model.GetType().Name}.");
        }
    }

    protected override void HandleItemActivated(CarouselItem item)
    {
        switch (item.Model)
        {
            case BmsCourseTableGroup group:
                if (ExpandedGroup == group)
                {
                    setExpansionStateOfGroup(group, false);
                    ExpandedGroup = null;
                    updateExpandedCourse(SelectedCourse?.Id);
                    break;
                }

                setExpandedGroup(group);

                // Match BeatmapCarousel: returning to the selected course's group restores keyboard focus to that course.
                if (CurrentSelectionItem?.IsVisible == true && CurrentSelection is BmsGroupedCourse selected)
                    CurrentSelection = selected;

                break;

            case BmsGroupedCourse groupedCourse:
                if (SelectedCourse?.Id != groupedCourse.Course.Id)
                    CurrentSelection = groupedCourse;
                else
                    CourseActivated?.Invoke();
                break;

            case BmsGroupedCourseStage:
                break;
        }
    }

    protected override void HandleItemSelected(object? model)
    {
        base.HandleItemSelected(model);
        var groupedCourse = model as BmsGroupedCourse;
        setExpandedGroup(groupedCourse?.Group);
        updateExpandedCourse(groupedCourse?.Course.Id);
        CourseSelected?.Invoke(groupedCourse?.Course);
    }

    protected override void HandleFilterCompleted()
    {
        base.HandleFilterCompleted();

        var courses = GetCarouselItems()?
            .Select(item => item.Model)
            .OfType<BmsGroupedCourse>()
            .ToArray() ?? [];

        var selectedId = SelectedCourse?.Id;
        var nextSelection = courses.FirstOrDefault(course => course.Course.Id == selectedId)
                            ?? courses.FirstOrDefault();

        CurrentSelection = nextSelection;
        updateExpandedCourse(nextSelection?.Course.Id);
        MatchesChanged?.Invoke(filter.BeatmapItemsCount);
    }

    protected override bool CheckValidForGroupSelection(CarouselItem item) => item.Model is BmsCourseTableGroup;

    protected override bool CheckValidForSetSelection(CarouselItem item) => item.Model is BmsGroupedCourse;

    protected override void FindCarouselItemsForSelection(ref Selection keyboardSelection, ref Selection selection, IList<CarouselItem> items)
    {
        if (pendingKeyboardSelection != null)
        {
            keyboardSelection = new Selection(pendingKeyboardSelection);
            pendingKeyboardSelection = null;
        }

        base.FindCarouselItemsForSelection(ref keyboardSelection, ref selection, items);

        keyboardSelectedModel = keyboardSelection.Model;
    }

    private void setExpandedGroup(BmsCourseTableGroup? group)
    {
        if (ExpandedGroup != null)
            setExpansionStateOfGroup(ExpandedGroup, false);

        ExpandedGroup = group;

        if (ExpandedGroup != null)
            setExpansionStateOfGroup(ExpandedGroup, true);
    }

    private void setExpansionStateOfGroup(BmsCourseTableGroup group, bool expanded)
    {
        var groupItem = GetCarouselItems()?.FirstOrDefault(item => item.Model.Equals(group));
        if (groupItem != null)
            groupItem.IsExpanded = expanded;

        if (filter.GroupItems.TryGetValue(group, out var courses))
        {
            foreach (var course in courses)
            {
                course.CourseItem.IsVisible = expanded;
                course.CourseItem.IsExpanded = false;

                foreach (var stage in course.StageItems)
                    stage.IsVisible = false;
            }
        }

        updateExpandedCourse(SelectedCourse?.Id);
    }

    private void updateExpandedCourse(string? selectedCourseId)
    {
        foreach (var (group, courses) in filter.GroupItems)
        {
            var groupVisible = group == ExpandedGroup;

            foreach (var course in courses)
            {
                var expanded = groupVisible
                               && course.CourseItem.IsVisible
                               && course.Course.Course.Id == selectedCourseId;

                course.CourseItem.IsExpanded = expanded;

                foreach (var stage in course.StageItems)
                    stage.IsVisible = expanded;
            }
        }
    }

    private BeatmapInfo? resolveStageBeatmap(BmsCourseStage stage)
    {
        if (!stage.IsAvailable || string.IsNullOrEmpty(stage.BeatmapHash))
            return null;

        return BmsCourseStagePanel.QueryBeatmap(beatmaps, stage.BeatmapHash);
    }

}

internal sealed record BmsCourseTableGroup(int Order, string TableName, string Mark)
    : GroupDefinition(Order, string.IsNullOrEmpty(Mark) ? TableName : BmsStrings.CourseTableGroup(TableName, Mark));

internal sealed record BmsGroupedCourse(BmsCourseTableGroup Group, BmsCourseDefinition Course, bool HasMissingStage);

internal sealed record BmsGroupedCourseStage(
    BmsCourseTableGroup Group,
    BmsCourseDefinition Course,
    int StageIndex,
    BmsCourseStage Stage,
    BeatmapInfo? Beatmap);

internal sealed record BmsCourseCarouselItems(
    BmsGroupedCourse Course,
    CarouselItem CourseItem,
    IReadOnlyList<CarouselItem> StageItems);

internal sealed class BmsCourseCarouselFilter(
    Func<string> getSearchTerm,
    Func<BmsCourseStage, BeatmapInfo?> resolveStageBeatmap) : ICarouselFilter
{
    internal IReadOnlyDictionary<BmsCourseTableGroup, IReadOnlyList<BmsCourseCarouselItems>> GroupItems { get; private set; }
        = new Dictionary<BmsCourseTableGroup, IReadOnlyList<BmsCourseCarouselItems>>();

    public int BeatmapItemsCount { get; private set; }

    public Task<List<CarouselItem>> Run(IEnumerable<CarouselItem> items, CancellationToken cancellationToken)
    {
        var matchingCourses = items.Select(item => (BmsCourseDefinition)item.Model)
            .Where(course => course.Matches(getSearchTerm()))
            .ToArray();
        var result = new List<CarouselItem>();
        var newGroupItems = new Dictionary<BmsCourseTableGroup, IReadOnlyList<BmsCourseCarouselItems>>();
        var order = 0;

        foreach (var table in matchingCourses.GroupBy(course => course.TableName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var group = new BmsCourseTableGroup(order++, table.Key, table.First().TableMark);
            var groupItem = new CarouselItem(group)
            {
                DrawHeight = PanelGroup.HEIGHT,
                DepthLayer = -2,
                NestedItemCount = table.Count(),
            };
            var courses = table.Select(course =>
            {
                var resolvedStages = course.Stages.Select((stage, stageIndex) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new BmsGroupedCourseStage(group, course, stageIndex, stage, resolveStageBeatmap(stage));
                }).ToArray();
                var groupedCourse = new BmsGroupedCourse(group, course, resolvedStages.Any(stage => stage.Beatmap == null));
                var courseItem = new CarouselItem(groupedCourse)
                {
                    DrawHeight = BmsCoursePanel.HEIGHT,
                    DepthLayer = -1,
                    IsVisible = false,
                    NestedItemCount = course.Stages.Count,
                };
                var stageItems = resolvedStages.Select(stage => new CarouselItem(stage)
                {
                    DrawHeight = PanelBeatmapStandalone.HEIGHT,
                    IsVisible = false,
                }).ToArray();

                return new BmsCourseCarouselItems(groupedCourse, courseItem, stageItems);
            }).ToArray();

            result.Add(groupItem);

            foreach (var course in courses)
            {
                result.Add(course.CourseItem);
                result.AddRange(course.StageItems);
            }

            newGroupItems[group] = courses;
        }

        GroupItems = newGroupItems;
        BeatmapItemsCount = matchingCourses.Length;
        return Task.FromResult(result);
    }
}
