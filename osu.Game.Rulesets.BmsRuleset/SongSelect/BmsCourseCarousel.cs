using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Graphics.Carousel;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

[Cached]
internal partial class BmsCourseCarousel : Carousel<BmsCourseDefinition>
{
    internal event Action<BmsCourseDefinition?>? CourseSelected;

    internal event Action? CourseActivated;

    internal event Action<int>? MatchesChanged;

    internal BmsCourseDefinition? SelectedCourse => (CurrentSelection as BmsGroupedCourse)?.Course;

    private readonly BmsCourseCarouselFilter filter;
    private readonly HashSet<string> collapsedTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly object collapsedTablesLock = new();
    private readonly DrawablePool<BmsCoursePanel> coursePanelPool = new(100);
    private readonly DrawablePool<BmsCourseTablePanel> tablePanelPool = new(20);

    private string searchTerm = string.Empty;

    internal BmsCourseCarousel()
    {
        DebounceDelay = 100;
        DistanceOffscreenToPreload = 100;
        Scroll.ScrollbarPaddingBottom = 70;

        Filters =
        [
            filter = new BmsCourseCarouselFilter(() => searchTerm, isTableCollapsed),
        ];

        AddInternal(tablePanelPool);
        AddInternal(coursePanelPool);
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

    protected override float GetSpacingBetweenPanels(CarouselItem top, CarouselItem bottom)
    {
        if (top.Model is BmsCourseTableGroup ^ bottom.Model is BmsCourseTableGroup)
            return BeatmapCarousel.SPACING * 2;

        if (top.Model is BmsGroupedCourse or BmsGroupedCourseStage
            || bottom.Model is BmsGroupedCourse or BmsGroupedCourseStage)
            return BeatmapCarousel.SPACING;

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

            case BmsGroupedCourseStage stage:
                return new BmsCourseStagePanel(stage.StageIndex + 1, stage.Stage);

            default:
                throw new InvalidOperationException($"Unsupported BMS course carousel model {item.Model.GetType().Name}.");
        }
    }

    protected override void HandleItemActivated(CarouselItem item)
    {
        switch (item.Model)
        {
            case BmsCourseTableGroup group:
                toggleGroup(group, item);
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
        var course = (model as BmsGroupedCourse)?.Course;
        updateExpandedCourse(course?.Id);
        CourseSelected?.Invoke(course);
    }

    protected override void HandleFilterCompleted()
    {
        base.HandleFilterCompleted();

        var visibleCourses = GetCarouselItems()?
            .Where(item => item.IsVisible)
            .Select(item => item.Model)
            .OfType<BmsGroupedCourse>()
            .ToArray() ?? [];

        var selectedId = SelectedCourse?.Id;
        var nextSelection = visibleCourses.FirstOrDefault(course => course.Course.Id == selectedId)
                            ?? visibleCourses.FirstOrDefault();

        CurrentSelection = nextSelection;
        updateExpandedCourse(nextSelection?.Course.Id);
        MatchesChanged?.Invoke(filter.BeatmapItemsCount);
    }

    protected override bool CheckValidForGroupSelection(CarouselItem item) => item.Model is BmsCourseTableGroup;

    protected override bool CheckValidForSetSelection(CarouselItem item) => item.Model is BmsGroupedCourse;

    private bool isTableCollapsed(string tableName)
    {
        lock (collapsedTablesLock)
            return collapsedTables.Contains(tableName);
    }

    private void toggleGroup(BmsCourseTableGroup group, CarouselItem groupItem)
    {
        var collapsed = !isTableCollapsed(group.TableName);

        lock (collapsedTablesLock)
        {
            if (collapsed)
                collapsedTables.Add(group.TableName);
            else
                collapsedTables.Remove(group.TableName);
        }

        groupItem.IsExpanded = !collapsed;

        if (filter.GroupItems.TryGetValue(group, out var courses))
        {
            foreach (var course in courses)
            {
                course.CourseItem.IsVisible = !collapsed;
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
            var groupVisible = !isTableCollapsed(group.TableName);

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

}

internal sealed record BmsCourseTableGroup(int Order, string TableName, string Mark)
    : GroupDefinition(Order, string.IsNullOrEmpty(Mark) ? TableName : $"{Mark}  {TableName}");

internal sealed record BmsGroupedCourse(BmsCourseTableGroup Group, BmsCourseDefinition Course);

internal sealed record BmsGroupedCourseStage(
    BmsCourseTableGroup Group,
    BmsCourseDefinition Course,
    int StageIndex,
    BmsCourseStage Stage);

internal sealed record BmsCourseCarouselItems(
    BmsGroupedCourse Course,
    CarouselItem CourseItem,
    IReadOnlyList<CarouselItem> StageItems);

internal sealed class BmsCourseCarouselFilter(Func<string> getSearchTerm, Func<string, bool> isTableCollapsed) : ICarouselFilter
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
            var collapsed = isTableCollapsed(table.Key);
            var groupItem = new CarouselItem(group)
            {
                DrawHeight = PanelGroup.HEIGHT,
                DepthLayer = -2,
                IsExpanded = !collapsed,
                NestedItemCount = table.Count(),
            };
            var courses = table.Select(course =>
            {
                var groupedCourse = new BmsGroupedCourse(group, course);
                var courseItem = new CarouselItem(groupedCourse)
                {
                    DrawHeight = PanelBeatmapStandalone.HEIGHT,
                    DepthLayer = -1,
                    IsVisible = !collapsed,
                    NestedItemCount = course.Stages.Count,
                };
                var stageItems = course.Stages.Select((stage, stageIndex) => new CarouselItem(
                    new BmsGroupedCourseStage(group, course, stageIndex, stage))
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
