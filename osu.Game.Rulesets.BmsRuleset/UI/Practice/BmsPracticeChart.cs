using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Objects;
using osuTK;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.UI.Practice;

/// <summary>Dense charts use a single geometry batch to keep note count out of scene-graph cost.</summary>
public partial class BmsPracticeChart : CompositeDrawable, IHasTooltip
{
    private const float time_gutter = 48;
    private const float miniature_width = 48;
    private const float density_width = 40;
    private const float overview_gap = 8;
    private const float edge_margin = 18;
    private Colour4 accent;
    private Colour4 endColour;
    private OverlayColourProvider palette = new(OverlayColourScheme.Purple);
    private readonly BmsReplayVisualizer.PlotLayer plot = new();
    private readonly Container labels = new();
    private readonly Dictionary<TouchSource, Vector2> touches = [];
    private readonly Dictionary<BmsHitObject, double> followingNoteIntervals = [];
    private BmsBeatmap? beatmap;
    private BmsPracticeSettings settings = new();
    private double[] snapTimes = [];
    private BmsPracticeGridLine[] gridLines = [];
    private BmsPracticeDensity? density;
    private (int Row, BmsPracticeDensityWindow Window)[] densityRows = [];
    private int densityRowCount;
    private int snapDivisor = 16;
    private bool dirty = true;
    private Vector2 previousSize;
    private DragMode dragMode;
    private Vector2 dragPosition;
    private float dragBoundaryOffset;
    private double dragOverviewOffset;
    private bool draggingDensity;
    private bool touchOverviewGesture;
    private bool disableSnap;
    private bool dragActive;
    private bool placeOnClick;
    private bool touchGestureMoved;
    private Vector2 touchStartPosition;

    public readonly BmsPracticeViewport Viewport = new();
    public Action<double, double>? RangeChanged { get; set; }
    public Action? ViewChanged { get; set; }
    public Action? ActiveBoundaryChanged { get; set; }
    public bool? ActiveBoundary { get; private set; }
    public float TimelineLeft => time_gutter;
    public float TimelineRight => chartRight;
    public float OverviewLeft => DensityLeft - OverviewColumns * (miniature_width + overview_gap);
    public float DensityLeft => DrawWidth - density_width;
    public int SnapDivisor
    {
        get => snapDivisor;
        set
        {
            if (snapDivisor == value) return;
            snapDivisor = value;
            updateGrid();
        }
    }
    public override bool AcceptsFocus => true;
    public LocalisableString TooltipText
    {
        get
        {
            if (density == null || GetContainingInputManager() is not { } input)
                return default;
            var position = ToLocalSpace(input.CurrentState.Mouse.Position);
            if (position.X < DensityLeft || position.Y < edge_margin || position.Y > DrawHeight - edge_margin)
                return default;
            var row = Math.Clamp((int)((DrawHeight - edge_margin - position.Y) / Math.Max(1, DrawHeight - edge_margin * 2) * densityRowCount), 0, Math.Max(0, densityRowCount - 1));
            var window = densityRows.FirstOrDefault(entry => entry.Row == row).Window;
            if (window.NoteCount == 0)
                window = density.GetWindow(overviewTime(position.Y));
            return BmsStrings.PracticeDensityWindow(window.StartTime, window.EndTime, window.NotesPerSecond);
        }
    }
    public double SelectedEnd => Math.Max(settings.EndTime, beatmap?.HitObjects.Where(h => settings.Contains(h.StartTime))
        .Select(h => h.GetEndTime()).DefaultIfEmpty(settings.EndTime).Max() ?? settings.EndTime);

    public BmsPracticeChart()
    {
        RelativeSizeAxes = Axes.Both;
        Masking = true;
        AddInternal(plot);
        AddInternal(labels);
        plot.RelativeSizeAxes = labels.RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(OsuColour colours, OverlayColourProvider? colourProvider)
    {
        palette = colourProvider ?? palette;
        accent = colours.PinkLight;
        endColour = colours.Yellow;
        dirty = true;
    }

    public void SetBeatmap(BmsBeatmap value, BmsPracticeSettings range, bool resetView)
    {
        beatmap = value;
        settings = range;
        updateGrid();
        followingNoteIntervals.Clear();
        foreach (var column in value.HitObjects.Where(note => note is not BmsLandmine and not BmsInvisibleNote).GroupBy(note => note.Column))
        {
            var following = double.PositiveInfinity;
            var current = double.PositiveInfinity;
            foreach (var note in column.OrderByDescending(note => note.StartTime))
            {
                if (note.StartTime < current)
                {
                    following = current;
                    current = note.StartTime;
                }
                followingNoteIntervals[note] = following - note.StartTime;
            }
        }
        var bounds = BmsPracticeBeatmap.GetTimeBounds(value);
        Viewport.SetBounds(bounds.StartTime, bounds.EndTime, resetView);
        density = new BmsPracticeDensity(value.HitObjects, bounds.StartTime, bounds.EndTime);
        densityRows = [];
        densityRowCount = 0;
        dirty = true;
        ViewChanged?.Invoke();
    }

    private void updateGrid()
    {
        gridLines = BmsPracticeGrid.Create(beatmap?.TimingMap, snapDivisor);
        snapTimes = gridLines.Select(line => line.Time).Distinct().Order().ToArray();
        dirty = true;
    }

    public double SnapTime(double time) => Math.Clamp(BmsPracticeGrid.Snap(beatmap?.TimingMap, time, snapDivisor), Viewport.Minimum, Viewport.Maximum);

    public void SetSelection(BmsPracticeSettings range)
    {
        settings = range;
        dirty = true;
    }

    public void SelectBoundary(bool start)
    {
        ActiveBoundary = start;
        dirty = true;
        ActiveBoundaryChanged?.Invoke();
    }

    public void LocateBoundary(bool start)
    {
        SelectBoundary(start);
        var time = start ? settings.StartTime : settings.EndTime;
        Viewport.Set(time - Viewport.Duration / 2, Viewport.Duration);
        invalidateView();
    }

    public void FitAll() { Viewport.FitAll(); invalidateView(); }
    public void FitSelection() { Viewport.Fit(settings.StartTime, SelectedEnd); invalidateView(); }
    public void Zoom(double factor) { Viewport.Zoom(factor); invalidateView(); }
    public void SetZoom(double fraction)
    {
        var full = Viewport.Maximum - Viewport.Minimum;
        var duration = full * Math.Pow(Viewport.MinimumDuration / full, fraction);
        Viewport.Zoom(duration / Viewport.Duration);
        invalidateView();
    }

    private void invalidateView()
    {
        dirty = true;
        ViewChanged?.Invoke();
    }

    protected override void Update()
    {
        base.Update();
        if (dragActive && dragMode is DragMode.Start or DragMode.End && DrawHeight > 0)
        {
            var edge = dragPosition.Y < 32 ? (dragPosition.Y - 32) / 32
                : dragPosition.Y > DrawHeight - 32 ? (dragPosition.Y - DrawHeight + 32) / 32 : 0;
            if (edge != 0)
            {
                Viewport.Pan(-Math.Clamp(edge, -2, 2) * Viewport.Duration * Time.Elapsed / 1800);
                updateBoundary(dragPosition.Y);
                invalidateView();
            }
        }
        if (dirty || previousSize != DrawSize)
        {
            previousSize = DrawSize;
            dirty = false;
            rebuild();
        }
    }

    private float chartRight => OverviewLeft - 12;
    private Colour4 noteColour(int column) => BmsLayout.IsScratchColumn(column, beatmap!.LayoutVariant) ? Colour4.FromHex("F3BA83")
        : column % 2 == 0 ? Colour4.FromHex("DBE6EA") : Colour4.FromHex("86B8E3");
    private int visualColumn(int column) => beatmap != null && BmsLayout.Is2P(beatmap.LayoutVariant)
        ? column == 0 ? beatmap.TotalColumns - 1 : column - 1
        : column;
    private double timeAt(float y) => Viewport.End - (y - edge_margin) / Math.Max(1, DrawHeight - edge_margin * 2) * Viewport.Duration;
    private float yAt(double time) => edge_margin + (float)((Viewport.End - time) / Viewport.Duration) * Math.Max(1, DrawHeight - edge_margin * 2);
    private double overviewTime(float y) => Viewport.Maximum - (y - edge_margin) / Math.Max(1, DrawHeight - edge_margin * 2) * (Viewport.Maximum - Viewport.Minimum);

    private void rebuild()
    {
        plot.Clear(false);
        labels.Clear();
        if (beatmap == null || DrawWidth <= time_gutter + 50 || DrawHeight <= 36)
            return;

        rectangle(0, 0, DrawWidth, DrawHeight, palette.Background6);
        var laneWidth = (chartRight - time_gutter) / Math.Max(1, beatmap.TotalColumns);
        for (var column = 0; column < beatmap.TotalColumns; column++)
        {
            var scratch = BmsLayout.IsScratchColumn(column, beatmap.LayoutVariant);
            rectangle(time_gutter + visualColumn(column) * laneWidth, 0, laneWidth, DrawHeight,
                scratch ? palette.Background2 : column % 2 == 0 ? palette.Background3 : palette.Background4);
        }

        var selectionTop = Math.Clamp(yAt(settings.EndTime), 0, DrawHeight);
        var selectionBottom = Math.Clamp(yAt(settings.StartTime), 0, DrawHeight);
        rectangle(time_gutter, selectionTop, chartRight - time_gutter, selectionBottom - selectionTop, accent.Opacity(0.10f));
        var boundaryLabels = boundaryLabelPositions();
        var lastLabelY = float.NegativeInfinity;
        for (var i = 0; i < gridLines.Length; i++)
        {
            var line = gridLines[i];
            if (line.Time < Viewport.Start || line.Time > Viewport.End) continue;
            var y = yAt(line.Time);
            var spacing = i + 1 < gridLines.Length ? Math.Abs(yAt(gridLines[i + 1].Time) - y) : 10;
            var beatLine = line.Subdivision % Math.Max(1, snapDivisor / 4) == 0;
            if (!line.IsMeasureLine && spacing < (beatLine ? 1.5f : 4)) continue;
            rectangle(time_gutter, y, chartRight - time_gutter, line.IsMeasureLine ? 2 : 1,
                Colour4.White.Opacity(line.IsMeasureLine ? 0.35f : beatLine ? 0.15f : 0.07f));
            if (line.IsMeasureLine && Math.Abs(y - lastLabelY) >= 18
                && !overlapsBoundaryLabel(y - 8, 13, boundaryLabels.Start) && !overlapsBoundaryLabel(y - 8, 13, boundaryLabels.End))
            {
                label(BmsStrings.PracticeMeasure(line.Measure + 1), 6, y - 8, Colour4.White.Opacity(0.7f), 13);
                lastLabelY = y;
            }
        }

        foreach (var note in beatmap.HitObjects)
        {
            var end = note.GetEndTime();
            if (end < Viewport.Start || note.StartTime > Viewport.End)
                continue;
            var selected = settings.Contains(note.StartTime);
            var colour = note is BmsLandmine ? Colour4.FromHex("F2798C") : noteColour(note.Column);
            colour = colour.Opacity(selected ? 1 : 0.25f);
            var x = time_gutter + visualColumn(note.Column) * laneWidth;
            var width = laneWidth;
            var y = yAt(note.StartTime);
            if (note is BmsLongNote)
            {
                var top = Math.Clamp(yAt(end), 0, DrawHeight);
                var bottom = Math.Clamp(y, 0, DrawHeight);
                var bodyColour = colour.Opacity(selected ? 0.8f : 0.4f);
                if (bottom > top)
                    rectangle(x, top, width, bottom - top, bodyColour);
            }
            if (note is BmsLandmine)
            {
                rectangle(x, y - 13, width, 3, colour);
                rectangle(x + width * 0.45f, y - 14, Math.Max(2, width * 0.1f), 14, colour);
                rectangle(x, y - 3, width, 3, colour);
            }
            else
            {
                var height = noteHeight(note);
                rectangle(x, y - height, width, height, colour);
            }
        }

        drawBoundary(settings.StartTime, true, boundaryLabels.Start);
        drawBoundary(settings.EndTime, false, boundaryLabels.End);
        drawMiniature();
        rectangle(DensityLeft, edge_margin, density_width, DrawHeight - edge_margin * 2, Colour4.White.Opacity(0.06f));
        drawDensity(DensityLeft);
        drawOverviewRange(DensityLeft, density_width, Viewport.Minimum, Viewport.Maximum);
        plot.Commit();
    }

    private float noteHeight(BmsHitObject note)
    {
        var height = 12 * Math.Min(1, 5000 / Viewport.Duration);
        if (followingNoteIntervals.TryGetValue(note, out var interval) && double.IsFinite(interval))
        {
            // Leave rhythm gaps visible when zoomed-out heads share less than one normal note height.
            var distance = interval / Viewport.Duration * Math.Max(1, DrawHeight - edge_margin * 2);
            height = Math.Min(height, distance * 0.6);
        }
        return (float)height;
    }

    private void drawDensity(float x)
    {
        if (density == null || density.MaximumNotesPerSecond <= 0)
            return;

        var height = DrawHeight - edge_margin * 2;
        var rowCount = Math.Max(1, (int)(height / 2));
        if (densityRowCount != rowCount)
        {
            densityRowCount = rowCount;
            var rows = new Dictionary<int, BmsPracticeDensityWindow>();
            foreach (var window in density.Windows)
            {
                var first = Math.Clamp((int)((window.StartTime - Viewport.Minimum) / (Viewport.Maximum - Viewport.Minimum) * rowCount), 0, rowCount - 1);
                var last = Math.Clamp((int)Math.Ceiling((Math.Min(window.EndTime, Viewport.Maximum) - Viewport.Minimum)
                                                      / (Viewport.Maximum - Viewport.Minimum) * rowCount) - 1, first, rowCount - 1);
                // Keep brief dense bursts visible when several seconds share a screen row.
                for (var row = first; row <= last; row++)
                {
                    if (!rows.TryGetValue(row, out var current) || window.NotesPerSecond > current.NotesPerSecond)
                        rows[row] = window;
                }
            }
            densityRows = rows.OrderBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value)).ToArray();
        }

        var rowHeight = height / rowCount;
        foreach (var (row, window) in densityRows)
        {
            var width = (density_width - 4) * (float)(window.NotesPerSecond / density.MaximumNotesPerSecond);
            rectangle(x + 2, DrawHeight - edge_margin - (row + 1) * rowHeight, width, rowHeight, ((Colour4)palette.Light2).Opacity(0.8f));
        }
    }

    private float boundaryLabelHeight => 14;

    private (float Start, float End) boundaryLabelPositions()
    {
        const float padding = 2;
        var maximum = Math.Max(padding, DrawHeight - boundaryLabelHeight - padding);
        var start = Math.Clamp(boundaryY(settings.StartTime, true) - 8, padding, maximum);
        var end = Math.Clamp(boundaryY(settings.EndTime, false) - 8, padding, maximum);
        var separation = boundaryLabelHeight + 4;
        if (start - end < separation)
        {
            // Keep the exact range lines in place while reserving separate, fully visible text blocks.
            end = Math.Clamp((start + end - separation) / 2, padding, Math.Max(padding, maximum - separation));
            start = end + separation;
        }
        return (start, end);
    }

    private bool overlapsBoundaryLabel(float y, float height, float boundaryLabelY)
        => y + height > boundaryLabelY - 4 && y < boundaryLabelY + boundaryLabelHeight + 4;

    private void drawBoundary(double time, bool start, float labelY)
    {
        var y = yAt(time);
        var outside = y < edge_margin || y > DrawHeight - edge_margin;
        y = boundaryY(time, start);
        var colour = start ? accent : endColour;
        rectangle(time_gutter - 10, y - (outside ? 6 : 1), chartRight - time_gutter + 10, outside ? 12 : 2, colour.Opacity(outside ? 0.18f : 1));
        if (HasFocus && ActiveBoundary == start)
            rectangle(time_gutter - 12, y - 6, 12, 12, colour);
        label(start ? BmsStrings.PracticeStartLine : BmsStrings.PracticeEndLine, 6, labelY, colour, 13);
    }

    private float boundaryY(double time, bool start)
    {
        if (settings.StartTime < Viewport.Start && settings.EndTime < Viewport.Start)
            return DrawHeight - edge_margin - (start ? 0 : 24);
        if (settings.StartTime > Viewport.End && settings.EndTime > Viewport.End)
            return edge_margin + (start ? 24 : 0);
        return Math.Clamp(yAt(time), edge_margin, DrawHeight - edge_margin);
    }

    private void rectangle(float x, float y, float width, float height, Colour4 colour, bool outline = false)
    {
        if (height > 0 && width > 0)
            plot.Add(new BmsReplayVisualizer.PlotRectangle(new RectangleF(x, y, width, height), colour, outline));
    }

    private void label(LocalisableString text, float x, float y, Colour4 colour, float size)
        => labels.Add(new OsuSpriteText { Text = text, X = x, Y = y, Colour = colour, Font = OsuFont.GetFont(size: size) });

    protected override bool OnScroll(ScrollEvent e)
    {
        var position = ToLocalSpace(e.ScreenSpaceMousePosition);
        if (e.ControlPressed && position.X < OverviewLeft)
            Viewport.Zoom(Math.Pow(0.8, e.ScrollDelta.Y), 1 - (position.Y - edge_margin) / Math.Max(1, DrawHeight - edge_margin * 2));
        else
            Viewport.Pan(e.ScrollDelta.Y * Viewport.Duration * (e.IsPrecise ? 0.002 : 0.1));
        invalidateView();
        return true;
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left) return false;
        GetContainingFocusManager()?.ChangeFocus(this);
        disableSnap = e.AltPressed;
        beginDrag(ToLocalSpace(e.ScreenSpaceMousePosition));
        return true;
    }

    private void beginDrag(Vector2 position)
    {
        placeOnClick = false;
        dragPosition = position;
        dragBoundaryOffset = 0;
        if (position.X >= OverviewLeft)
        {
            beginOverviewDrag(position);
            return;
        }
        var startY = boundaryY(settings.StartTime, true);
        var endY = boundaryY(settings.EndTime, false);
        var inGutter = position.X < time_gutter - 10;
        if (inGutter)
        {
            var boundaryLabels = boundaryLabelPositions();
            startY = boundaryLabels.Start + 8;
            endY = boundaryLabels.End + 8;
        }
        // Clamped offscreen lines are navigation hints, not draggable endpoints in the current view.
        var startDistance = inGutter || settings.StartTime >= Viewport.Start && settings.StartTime <= Viewport.End
            ? Math.Abs(position.Y - startY) : float.PositiveInfinity;
        var endDistance = inGutter || settings.EndTime >= Viewport.Start && settings.EndTime <= Viewport.End
            ? Math.Abs(position.Y - endY) : float.PositiveInfinity;
        if (Math.Min(startDistance, endDistance) < 20)
        {
            var start = startDistance <= endDistance;
            SelectBoundary(start);
            var boundary = start ? settings.StartTime : settings.EndTime;
            if (position.X < time_gutter - 10)
                dragBoundaryOffset = position.Y - boundaryY(boundary, start);
            if (boundary < Viewport.Start || boundary > Viewport.End)
            {
                LocateBoundary(start);
                dragMode = DragMode.None;
            }
            else dragMode = start ? DragMode.Start : DragMode.End;
        }
        else
        {
            dragMode = DragMode.Pan;
            placeOnClick = ActiveBoundary.HasValue && position.X >= TimelineLeft && position.X <= TimelineRight;
        }
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left || !placeOnClick)
            return false;
        placeBoundary(ToLocalSpace(e.ScreenSpaceMousePosition).Y);
        return true;
    }

    private void placeBoundary(float y)
    {
        placeOnClick = false;
        var time = Math.Clamp(timeAt(y), Viewport.Minimum, Viewport.Maximum);
        if (!disableSnap) time = SnapTime(time);
        setBoundary(time, ActiveBoundary == true, !disableSnap);
    }

    protected override bool OnDragStart(DragStartEvent e)
    {
        placeOnClick = false;
        return dragActive = dragMode != DragMode.None;
    }
    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        // A few overview pixels can span minutes, so fine navigation cannot wait for the framework drag threshold.
        if (dragMode == DragMode.Overview && !dragActive)
        {
            moveDrag(ToLocalSpace(e.ScreenSpaceMousePosition));
            return true;
        }
        return base.OnMouseMove(e);
    }
    protected override void OnDrag(DragEvent e)
    {
        disableSnap = e.AltPressed;
        moveDrag(ToLocalSpace(e.ScreenSpaceMousePosition));
    }
    protected override void OnDragEnd(DragEndEvent e) { dragMode = DragMode.None; dragActive = false; }
    protected override void OnMouseUp(MouseUpEvent e) { dragMode = DragMode.None; dragActive = false; }

    private void moveDrag(Vector2 position)
    {
        var delta = position - dragPosition;
        dragPosition = position;
        switch (dragMode)
        {
            case DragMode.Start:
            case DragMode.End:
                updateBoundary(position.Y);
                break;
            case DragMode.Pan:
                Viewport.Pan(delta.Y / Math.Max(1, DrawHeight - edge_margin * 2) * Viewport.Duration);
                break;
            case DragMode.Overview:
                moveOverviewDrag(position);
                break;
        }
        invalidateView();
    }

    private void updateBoundary(float y)
    {
        var time = Math.Clamp(timeAt(y - dragBoundaryOffset), Viewport.Minimum, Viewport.Maximum);
        if (!disableSnap) time = SnapTime(time);
        setBoundary(time, dragMode == DragMode.Start, !disableSnap);
    }

    private void setBoundary(double time, bool start, bool snap = false)
    {
        var startTime = Math.Clamp(settings.StartTime, Viewport.Minimum, Viewport.Maximum - 0.001);
        var endTime = Math.Clamp(settings.EndTime, startTime + 0.001, Viewport.Maximum);
        if (snap && snapTimes.Length > 0)
        {
            // Crossing a boundary stops at the final eligible grid point, rather than creating an off-grid 1ms gap.
            var first = Array.BinarySearch(snapTimes, start ? Viewport.Minimum : startTime);
            first = first < 0 ? ~first : first + (start ? 0 : 1);
            var last = Array.BinarySearch(snapTimes, start ? endTime : Viewport.Maximum);
            last = last < 0 ? ~last - 1 : last - (start ? 1 : 0);
            if (first > last) return;
            time = Math.Clamp(time, snapTimes[first], snapTimes[last]);
        }
        settings = settings with
        {
            StartTime = start ? Math.Clamp(time, Viewport.Minimum, endTime - 0.001) : startTime,
            EndTime = start ? endTime : Math.Clamp(time, startTime + 0.001, Viewport.Maximum),
        };
        dirty = true;
        RangeChanged?.Invoke(settings.StartTime, settings.EndTime);
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            SelectBoundary(e.Key == Key.Left);
            return true;
        }
        if (ActiveBoundary is { } start && e.Key is Key.Up or Key.Down)
        {
            var current = start ? settings.StartTime : settings.EndTime;
            var later = e.Key == Key.Up;
            if (!e.AltPressed && snapTimes.Length > 0)
            {
                var index = Array.BinarySearch(snapTimes, current);
                index = index < 0 ? ~index + (later ? 0 : -1) : index + (later ? 1 : -1);
                index = Math.Clamp(index, 0, snapTimes.Length - 1);
                var targetIndex = Math.Clamp(index + (later ? 1 : -1) * (e.ShiftPressed ? 3 : 0), 0, snapTimes.Length - 1);
                setBoundary(snapTimes[targetIndex], start, true);
            }
            else
                setBoundary(current + (e.ShiftPressed ? 1 : 10) * (later ? 1 : -1), start);
            return true;
        }
        return base.OnKeyDown(e);
    }

    protected override void OnFocus(FocusEvent e) { dirty = true; base.OnFocus(e); }
    protected override void OnFocusLost(FocusLostEvent e) { dirty = true; base.OnFocusLost(e); }

    protected override bool OnTouchDown(TouchDownEvent e)
    {
        disableSnap = e.AltPressed;
        touches[e.Touch.Source] = ToLocalSpace(e.ScreenSpaceTouch.Position);
        if (touches.Count == 1)
        {
            touchStartPosition = touches.Values.First();
            touchOverviewGesture = touchStartPosition.X >= OverviewLeft;
            touchGestureMoved = false;
            beginDrag(touchStartPosition);
        }
        else
        {
            touchGestureMoved = true;
            placeOnClick = false;
            dragMode = DragMode.None;
            dragActive = false;
        }
        return true;
    }

    protected override void OnTouchMove(TouchMoveEvent e)
    {
        if (!touches.ContainsKey(e.Touch.Source)) return;
        var position = ToLocalSpace(e.ScreenSpaceTouch.Position);
        if (touches.Count == 2)
        {
            touchGestureMoved = true;
            placeOnClick = false;
            var old = touches.Values.ToArray();
            var oldDistance = (old[0] - old[1]).Length;
            touches[e.Touch.Source] = position;
            var current = touches.Values.ToArray();
            var distance = (current[0] - current[1]).Length;
            if (!touchOverviewGesture && oldDistance > 1 && distance > 1)
            {
                Viewport.Zoom(oldDistance / distance, 1 - ((current[0].Y + current[1].Y) / 2 - edge_margin) / Math.Max(1, DrawHeight - edge_margin * 2));
                invalidateView();
            }
        }
        else
        {
            if ((position - touchStartPosition).Length > 4)
            {
                touchGestureMoved = true;
                placeOnClick = false;
            }
            touches[e.Touch.Source] = position;
            if (placeOnClick && !touchGestureMoved)
                return;
            dragActive = true;
            moveDrag(position);
        }
    }

    protected override void OnTouchUp(TouchUpEvent e)
    {
        if (touches.Count == 1 && !touchGestureMoved && placeOnClick)
            placeBoundary(ToLocalSpace(e.ScreenSpaceTouch.Position).Y);
        touches.Remove(e.Touch.Source);
        dragMode = DragMode.None;
        dragActive = false;
        placeOnClick = false;
        if (touches.Count == 1)
        {
            // The remaining finger may browse after a pinch, but that gesture cannot place an endpoint.
            dragPosition = touches.Values.First();
            if (touchOverviewGesture)
                beginOverviewDrag(dragPosition);
            else
                dragMode = DragMode.Pan;
        }
    }

    private enum DragMode { None, Start, End, Pan, Overview }
}
