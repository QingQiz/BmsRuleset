using System;
using System.Linq;
using osu.Framework.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Objects;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Practice;

public partial class BmsPracticeChart
{
    public int OverviewColumns
    {
        get
        {
            // Preserve usable gameplay lanes while spending spare width on a less compressed overview.
            var minimumTimelineWidth = Math.Max(160, (beatmap?.TotalColumns ?? 8) * 16);
            var capacity = Math.Max(1, (int)((DrawWidth - time_gutter - minimumTimelineWidth - density_width - 12) / (miniature_width + overview_gap)));
            var targetDuration = Math.Max(75000, (DrawHeight - edge_margin * 2) * 100);
            var desired = (int)Math.Clamp(Math.Ceiling((Viewport.Maximum - Viewport.Minimum) / targetDuration), 1, 8);
            return Math.Min(desired, capacity);
        }
    }

    private double miniatureDuration => (Viewport.Maximum - Viewport.Minimum) / OverviewColumns;
    private double miniatureStart(int column) => Viewport.Minimum + column * miniatureDuration;
    private double miniatureEnd(int column) => Viewport.Minimum + (column + 1) * miniatureDuration;
    private float miniatureLeft(int column) => OverviewLeft + column * (miniature_width + overview_gap);
    private int miniatureColumnAt(double time) => (int)Math.Clamp(Math.Floor((time - Viewport.Minimum) / miniatureDuration), 0, OverviewColumns - 1);
    private float miniatureY(double time, int column) => overviewRangeY(time, miniatureStart(column), miniatureEnd(column));
    private float overviewRangeY(double time, double start, double end)
        => edge_margin + (float)Math.Clamp((end - time) / (end - start), 0, 1) * Math.Max(1, DrawHeight - edge_margin * 2);

    internal Vector2 MiniaturePositionAt(double time)
    {
        var column = miniatureColumnAt(time);
        return new Vector2(miniatureLeft(column) + miniature_width / 2, miniatureY(time, column));
    }

    private void drawMiniature()
    {
        if (beatmap == null)
            return;

        var laneWidth = miniature_width / Math.Max(1, beatmap.TotalColumns);
        for (var section = 0; section < OverviewColumns; section++)
        {
            var start = miniatureStart(section);
            var end = miniatureEnd(section);
            var left = miniatureLeft(section);
            for (var lane = 0; lane < beatmap.TotalColumns; lane++)
                rectangle(left + visualColumn(lane) * laneWidth, edge_margin, laneWidth, DrawHeight - edge_margin * 2,
                    lane % 2 == 0 ? palette.Background2 : palette.Background4);

            foreach (var note in beatmap.HitObjects)
            {
                if (note is BmsInvisibleNote || note.StartTime >= end || note.GetEndTime() < start)
                    continue;
                var x = left + visualColumn(note.Column) * laneWidth;
                var y = miniatureY(note.StartTime, section);
                var colour = noteColour(note.Column);
                if (note is BmsLongNote hold)
                {
                    var top = miniatureY(hold.EndTime, section);
                    rectangle(x, top, laneWidth, y - top, colour.Opacity(0.6f));
                }
                if (note.StartTime >= start)
                    rectangle(x, Math.Clamp(y - 1, edge_margin, DrawHeight - edge_margin - 1), laneWidth, 1, colour.Opacity(0.8f));
            }

            drawOverviewRange(left, miniature_width, start, end);
            if (OverviewColumns > 1 && gridLines.Length > 0)
            {
                var measure = gridLines.LastOrDefault(line => line.IsMeasureLine && line.Time <= start);
                label(BmsStrings.PracticeMeasure(measure.Measure + 1), left, DrawHeight - edge_margin + 3, palette.Light2, 11);
            }
        }
    }

    private void drawOverviewRange(float x, float width, double start, double end)
    {
        if (settings.EndTime > start && settings.StartTime < end)
        {
            var top = overviewRangeY(settings.EndTime, start, end);
            var bottom = overviewRangeY(settings.StartTime, start, end);
            rectangle(x, top, width, bottom - top, accent.Opacity(0.18f));
        }
        if (settings.StartTime >= start && settings.StartTime < end)
            rectangle(x, Math.Clamp(overviewRangeY(settings.StartTime, start, end) - 1, edge_margin, DrawHeight - edge_margin - 1), width, 1, accent);
        if (settings.EndTime > start && settings.EndTime <= end)
            rectangle(x, Math.Clamp(overviewRangeY(settings.EndTime, start, end) - 1, edge_margin, DrawHeight - edge_margin - 1), width, 1, endColour);
        if (Viewport.End <= start || Viewport.Start >= end)
            return;
        var viewTop = overviewRangeY(Viewport.End, start, end);
        var viewBottom = overviewRangeY(Viewport.Start, start, end);
        var height = Math.Min(DrawHeight - edge_margin * 2, Math.Max(2, viewBottom - viewTop));
        viewTop = Math.Min(viewTop, DrawHeight - edge_margin - height);
        rectangle(x, viewTop, width, height, Colour4.White.Opacity(0.06f));
        rectangle(x, viewTop, width, height, Colour4.White.Opacity(0.7f), true);
    }

    private double overviewTimeAt(Vector2 position, bool density)
    {
        if (density)
            return Math.Clamp(overviewTime(position.Y), Viewport.Minimum, Viewport.Maximum);
        var column = (int)Math.Clamp(Math.Floor((position.X - OverviewLeft) / (miniature_width + overview_gap)), 0, OverviewColumns - 1);
        var fraction = Math.Clamp((position.Y - edge_margin) / Math.Max(1, DrawHeight - edge_margin * 2), 0, 1);
        return miniatureEnd(column) - fraction * miniatureDuration;
    }

    private void beginOverviewDrag(Vector2 position)
    {
        draggingDensity = position.X >= DensityLeft;
        if (!draggingDensity && (position.X - OverviewLeft) % (miniature_width + overview_gap) > miniature_width)
        {
            dragMode = DragMode.None;
            return;
        }
        dragMode = DragMode.Overview;
        var time = overviewTimeAt(position, draggingDensity);
        var column = (int)Math.Clamp(Math.Floor((position.X - OverviewLeft) / (miniature_width + overview_gap)), 0, OverviewColumns - 1);
        var start = draggingDensity ? Viewport.Minimum : miniatureStart(column);
        var end = draggingDensity ? Viewport.Maximum : miniatureEnd(column);
        var top = overviewRangeY(Viewport.End, start, end);
        var bottom = overviewRangeY(Viewport.Start, start, end);
        if (Viewport.End <= start || Viewport.Start >= end || position.Y < top - 2 || position.Y > bottom + 2)
        {
            Viewport.Set(time - Viewport.Duration / 2, Viewport.Duration);
            invalidateView();
        }
        // Every point on the frame is a grab point, including its former resize edges.
        dragOverviewOffset = time - (Viewport.Start + Viewport.Duration / 2);
    }

    private void moveOverviewDrag(Vector2 position)
    {
        var time = overviewTimeAt(position, draggingDensity);
        Viewport.Set(time - dragOverviewOffset - Viewport.Duration / 2, Viewport.Duration);
    }
}
