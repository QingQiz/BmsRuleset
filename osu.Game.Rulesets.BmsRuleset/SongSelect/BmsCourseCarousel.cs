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

        if (top.Model is BmsGroupedCourse || bottom.Model is BmsGroupedCourse)
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
                break;
        }
    }

    protected override void HandleItemSelected(object? model)
    {
        base.HandleItemSelected(model);
        CourseSelected?.Invoke((model as BmsGroupedCourse)?.Course);
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

        if (filter.GroupItems.TryGetValue(group, out var children))
        {
            foreach (var child in children)
                child.IsVisible = !collapsed;
        }
    }

}

internal sealed record BmsCourseTableGroup(int Order, string TableName) : GroupDefinition(Order, TableName);

internal sealed record BmsGroupedCourse(BmsCourseTableGroup Group, BmsCourseDefinition Course);

internal sealed class BmsCourseCarouselFilter(Func<string> getSearchTerm, Func<string, bool> isTableCollapsed) : ICarouselFilter
{
    internal IReadOnlyDictionary<BmsCourseTableGroup, IReadOnlyList<CarouselItem>> GroupItems { get; private set; } = new Dictionary<BmsCourseTableGroup, IReadOnlyList<CarouselItem>>();

    public int BeatmapItemsCount { get; private set; }

    public Task<List<CarouselItem>> Run(IEnumerable<CarouselItem> items, CancellationToken cancellationToken)
    {
        var matchingCourses = items.Select(item => (BmsCourseDefinition)item.Model)
            .Where(course => course.Matches(getSearchTerm()))
            .ToArray();
        var result = new List<CarouselItem>();
        var newGroupItems = new Dictionary<BmsCourseTableGroup, IReadOnlyList<CarouselItem>>();
        var order = 0;

        foreach (var table in matchingCourses.GroupBy(course => course.TableName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var group = new BmsCourseTableGroup(order++, table.Key);
            var collapsed = isTableCollapsed(table.Key);
            var groupItem = new CarouselItem(group)
            {
                DrawHeight = PanelGroup.HEIGHT,
                DepthLayer = -2,
                IsExpanded = !collapsed,
                NestedItemCount = table.Count(),
            };
            var children = table.Select(course => new CarouselItem(new BmsGroupedCourse(group, course))
            {
                DrawHeight = PanelBeatmapStandalone.HEIGHT,
                IsVisible = !collapsed,
            }).ToArray();

            result.Add(groupItem);
            result.AddRange(children);
            newGroupItems[group] = children;
        }

        GroupItems = newGroupItems;
        BeatmapItemsCount = matchingCourses.Length;
        return Task.FromResult(result);
    }
}
