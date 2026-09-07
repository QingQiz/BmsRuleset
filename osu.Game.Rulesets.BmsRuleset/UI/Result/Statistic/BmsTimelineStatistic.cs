using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

public sealed partial class BmsTimelineStatistic : CompositeDrawable, IBmsResultStatistic
{
    private const int bucket_count = 300;
    private const float subplot_height = 96;
    private static readonly OsuColour colours = new();

    private static readonly Color4 note_colour = colours.Blue;
    private static readonly Color4 ln_colour = colours.GreenLight;
    private static readonly Color4 scratch_colour = colours.Yellow;
    private readonly ScoreInfo? score;
    private readonly IBeatmap? playableBeatmap;
    private readonly IReadOnlyList<(ScoreInfo? Score, IBeatmap Beatmap)>? stages;
    private TimelineData data = null!;
    private readonly List<Drawable> plots = [];
    private readonly List<Drawable> legends = [];

    internal BmsTimelineStatistic(TimelineData data)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        this.data = data;
    }

    public BmsTimelineStatistic(ScoreInfo score, IBeatmap playableBeatmap)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        this.score = score;
        this.playableBeatmap = playableBeatmap;
    }

    internal BmsTimelineStatistic(IReadOnlyList<(ScoreInfo? Score, IBeatmap Beatmap)> stages)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        this.stages = stages;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        data ??= score != null
            ? CreateData(score, playableBeatmap!)
            : CreateCourseData(stages!);

        rebuild();
    }

    internal void SetData(TimelineData newData)
    {
        data = newData;
        if (LoadState >= LoadState.Ready)
            rebuild();
    }

    private void rebuild()
    {
        var height = plots.FirstOrDefault()?.Height ?? subplot_height;
        plots.Clear();
        legends.Clear();
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 8),
            Children =
            [
                createSubplot(BmsStrings.Notes, data.Notes, data.StageBoundaries),
                createSubplot(BmsStrings.Judgement, data.Judgements, data.StageBoundaries),
                createSubplot(BmsStrings.FastSlow, data.FastSlow, data.StageBoundaries),
            ],
        };
        foreach (var plot in plots)
            plot.Height = height;
    }

    internal static TimelineData CreateData(ScoreInfo score, IBeatmap playableBeatmap)
    {
        var variant = playableBeatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;

        var beatmapMax = playableBeatmap.HitObjects.Select(h => h.GetEndTime()).DefaultIfEmpty(0).Max();
        var timingHitEvents = score.HitEvents.ToArray();
        var scoringHitEvents = BmsJudgementEventStore.TryGet(score, out var judgementEvents)
            ? BmsJudgementEventProjection.CreateScoringHitEvents(judgementEvents).ToArray()
            : timingHitEvents;
        var hitEventsMax = timingHitEvents.Length == 0 ? 0 : timingHitEvents.Max(e => e.HitObject.GetEndTime());
        var duration = Math.Max(1, Math.Max(beatmapMax, hitEventsMax));

        var notes = createNotesSubplot(playableBeatmap, variant, duration);
        var judgements = createJudgementSubplot(scoringHitEvents, duration);
        var fastSlow = createFastSlowSubplot(timingHitEvents, duration);
        return new TimelineData(notes, judgements, fastSlow, [], duration);
    }

    internal static TimelineData CreateCourseData(IReadOnlyList<(ScoreInfo? Score, IBeatmap Beatmap)> stages)
    {
        if (stages.Count == 0)
            return new TimelineData(new SubplotData([], []), new SubplotData([], []), new SubplotData([], []), [], 1);

        var stageData = stages.Select(stage => stage.Score != null
            ? CreateData(stage.Score, stage.Beatmap)
            : createUnplayedData(stage.Beatmap)).ToArray();
        var totalDuration = stageData.Sum(stage => stage.Duration);
        var boundaries = new float[stages.Count - 1];
        double elapsed = 0;

        for (var i = 0; i < stageData.Length; i++)
        {
            var stage = stageData[i];

            elapsed += stage.Duration;
            if (i < boundaries.Length)
                boundaries[i] = (float)(elapsed / totalDuration);
        }

        return new TimelineData(
            combineSubplots(stageData.Select(stage => stage.Notes).ToArray()),
            combineSubplots(stageData.Select(stage => stage.Judgements).ToArray()),
            combineSubplots(stageData.Select(stage => stage.FastSlow).ToArray()),
            boundaries,
            totalDuration);
    }

    private static TimelineData createUnplayedData(IBeatmap playableBeatmap)
    {
        var variant = playableBeatmap is BmsBeatmap bms ? bms.LayoutVariant : BmsLayoutVariant.Bms5K;
        var duration = Math.Max(1, playableBeatmap.HitObjects.Select(h => h.GetEndTime()).DefaultIfEmpty(0).Max());

        return new TimelineData(
            createNotesSubplot(playableBeatmap, variant, duration),
            createJudgementSubplot([], duration),
            createFastSlowSubplot([], duration),
            [], duration);
    }

    private static SubplotData combineSubplots(IReadOnlyList<SubplotData> stages)
    {
        var categories = stages.SelectMany(stage => stage.Categories.Select(category => category.Label)).Distinct().ToArray();

        return new SubplotData(
            categories.Select(label =>
            {
                var template = stages.SelectMany(stage => stage.Categories).First(category => category.Label == label);
                var buckets = stages.SelectMany(stage => stage.Categories.FirstOrDefault(category => category.Label == label)?.Buckets ?? new int[bucket_count]).ToArray();
                return new CategoryData(label, template.Colour, buckets);
            }).ToArray(),
            stages.SelectMany(stage => stage.BucketWeights).ToArray());
    }

    private static SubplotData createNotesSubplot(IBeatmap playableBeatmap, BmsLayoutVariant variant, double duration)
    {
        var note = new int[bucket_count];
        var ln = new int[bucket_count];
        var scratch = new int[bucket_count];

        foreach (var h in playableBeatmap.HitObjects.OfType<BmsHitObject>())
        {
            if (h is BmsLandmine)
                continue;

            var b = bucketFor(h.StartTime, duration);

            switch (classifyNote(h, variant))
            {
                case NoteKind.Note: note[b]++; break;

                case NoteKind.LongNote: ln[b]++; break;

                case NoteKind.Scratch: scratch[b]++; break;
            }
        }

        return new SubplotData(
        [
            new CategoryData("Scratch", scratch_colour, scratch),
            new CategoryData("ln", ln_colour, ln),
            new CategoryData("note", note_colour, note),
        ], uniformBucketWeights(duration));
    }

    private static SubplotData createJudgementSubplot(IReadOnlyList<HitEvent> hitEvents, double duration)
    {
        var perfect = new int[bucket_count];
        var great = new int[bucket_count];
        var good = new int[bucket_count];
        var ok = new int[bucket_count];
        var meh = new int[bucket_count];

        foreach (var e in hitEvents.Where(isBmsHit))
        {
            var b = bucketFor(e.HitObject.GetEndTime(), duration);

            switch (e.Result)
            {
                case HitResult.Perfect: perfect[b]++; break;

                case HitResult.Great: great[b]++; break;

                case HitResult.Good: good[b]++; break;

                case HitResult.Ok: ok[b]++; break;

                case HitResult.Meh: meh[b]++; break;
                    // Miss (E-Poor) excluded — IsHit() filters it out.
            }
        }

        return new SubplotData(
        [
            new CategoryData("Poor", BmsHitResultColours.ForHitResult(HitResult.Meh), meh),
            new CategoryData("Bad", BmsHitResultColours.ForHitResult(HitResult.Ok), ok),
            new CategoryData("Good", BmsHitResultColours.ForHitResult(HitResult.Good), good),
            new CategoryData("Great", BmsHitResultColours.ForHitResult(HitResult.Great), great),
            new CategoryData("Perfect", BmsHitResultColours.ForHitResult(HitResult.Perfect), perfect),
        ], uniformBucketWeights(duration));
    }

    private static SubplotData createFastSlowSubplot(IReadOnlyList<HitEvent> hitEvents, double duration)
    {
        var fast = new int[bucket_count];
        var slow = new int[bucket_count];

        foreach (var e in hitEvents.Where(isBmsHit))
        {
            var b = bucketFor(e.HitObject.GetEndTime(), duration);

            if (e.TimeOffset < 0) fast[b]++;
            else if (e.TimeOffset > 0) slow[b]++;
        }

        return new SubplotData(
        [
            new CategoryData("fast", BmsResultColours.FAST, fast),
            new CategoryData("slow", BmsResultColours.SLOW, slow),
        ], uniformBucketWeights(duration));
    }

    private static float[] uniformBucketWeights(double duration) =>
        Enumerable.Repeat((float)(duration / bucket_count), bucket_count).ToArray();

    private static bool isBmsHit(HitEvent e) => e.HitObject is BmsHitObject and not BmsLandmine && e.Result.IsBasic() && e.Result.IsHit();

    private static int bucketFor(double time, double duration) => Math.Clamp((int)Math.Floor(time / duration * bucket_count), 0, bucket_count - 1);

    // The scratch lane takes priority over long-note vs short note.
    private static NoteKind classifyNote(BmsHitObject h, BmsLayoutVariant variant)
    {
        if (BmsLayout.IsScratchColumn(h.Column, variant)) return NoteKind.Scratch;
        if (h is BmsLongNote) return NoteKind.LongNote;

        return NoteKind.Note;
    }

    void IBmsResultStatistic.FitSummaryToHeight(float height)
    {
        var plotHeight = Math.Max(0, (height - legends.Sum(legend => legend.DrawHeight) - 22) / 3);
        foreach (var plot in plots)
            plot.Height = plotHeight;
    }

    private Drawable createSubplot(LocalisableString title, SubplotData subplot, IReadOnlyList<float> stageBoundaries)
    {
        var legend = createLegend(title, subplot);
        var plot = createPlot(subplot, stageBoundaries);
        legends.Add(legend);
        plots.Add(plot);
        return new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 2),
            Children =
            [
                legend,
                plot,
            ],
        };
    }

    private static Drawable createLegend(LocalisableString title, SubplotData subplot)
    {
        var items = new List<Drawable>
        {
            new OsuSpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = title,
                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
            },
        };

        foreach (var c in subplot.Categories)
        {
            items.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Children =
                [
                    new Circle
                    {
                        Size = new Vector2(8),
                        Colour = c.Colour,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new OsuSpriteText
                    {
                        Text = localiseCategory(c.Label),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 11),
                    },
                ],
            });
        }

        return new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Full,
            Spacing = new Vector2(10, 2),
            Children = items,
        };
    }

    private static LocalisableString localiseCategory(string category) => category switch
    {
        "note" => BmsStrings.Note,
        "ln" => BmsStrings.LongNote,
        "Scratch" => BmsStrings.Scratch,
        "mine" => BmsStrings.Mine,
        "Poor" => BmsStrings.Poor,
        "Bad" => BmsStrings.Bad,
        "Good" => BmsStrings.Good,
        "Great" => BmsStrings.Great,
        "Perfect" => BmsStrings.Perfect,
        "fast" => BmsStrings.Fast,
        "slow" => BmsStrings.Slow,
        _ => category,
    };

    private static Drawable createPlot(SubplotData subplot, IReadOnlyList<float> stageBoundaries)
    {
        var bucketCount = subplot.Categories.Select(category => category.Buckets.Length).DefaultIfEmpty(bucket_count).Min();
        var columnWidths = CreateColumnWidths(subplot);
        var maxTotal = Math.Max(1, Enumerable.Range(0, bucketCount)
            .Select(b => subplot.Categories.Sum(c => c.Buckets[b]))
            .DefaultIfEmpty(0)
            .Max());

        var bars = new GridContainer
        {
            Name = "Timeline data",
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            Scale = new Vector2(1, 0),
            ColumnDimensions = columnWidths.Count == bucketCount
                ? columnWidths.Select(width => new Dimension(GridSizeMode.Relative, width)).ToArray()
                : Enumerable.Range(0, bucketCount).Select(_ => new Dimension()).ToArray(),
            Content = new[] { Enumerable.Range(0, bucketCount).Select(b => createBar(subplot, b, maxTotal)).ToArray() },
        };
        bars.OnLoadComplete += _ => bars.ScaleTo(Vector2.One, 450, Easing.OutQuint);
        var children = new List<Drawable>
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.18f },
            bars,
        };

        children.AddRange(stageBoundaries.Select(createStageBoundary));

        return new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = subplot_height,
            Children = children,
        };
    }

    internal static IReadOnlyList<float> CreateColumnWidths(SubplotData subplot)
    {
        var totalWeight = subplot.BucketWeights.Sum();
        if (totalWeight <= 0)
            return [];

        return subplot.BucketWeights.Select(weight => weight / totalWeight).ToArray();
    }

    private static Drawable createStageBoundary(float fraction) => new Box
    {
        RelativeSizeAxes = Axes.Y,
        RelativePositionAxes = Axes.X,
        X = fraction,
        Width = 1,
        Colour = Color4.White,
        Alpha = 0.18f,
    };

    private static Drawable createBar(SubplotData subplot, int bucket, int maxTotal)
    {
        var bar = new Container { RelativeSizeAxes = Axes.Both };
        var total = subplot.Categories.Sum(c => c.Buckets[bucket]);

        if (total == 0) return bar;

        float cumulative = 0;

        // Categories are ordered bottom-to-top; first iterated sits at the bottom.
        foreach (var cat in subplot.Categories)
        {
            var count = cat.Buckets[bucket];
            if (count == 0) continue;

            var height = (float)count / maxTotal;
            bar.Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                RelativePositionAxes = Axes.Y,
                Y = -cumulative,
                Height = height,
                Colour = cat.Colour,
                Alpha = 0.86f,
            });
            cumulative += height;
        }

        return bar;
    }

    private enum NoteKind
    {
        Note,
        LongNote,
        Scratch
    }

    internal sealed record TimelineData(
        SubplotData Notes,
        SubplotData Judgements,
        SubplotData FastSlow,
        IReadOnlyList<float> StageBoundaries,
        double Duration);

    internal sealed record SubplotData(IReadOnlyList<CategoryData> Categories, IReadOnlyList<float> BucketWeights);

    internal sealed record CategoryData(string Label, Color4 Colour, int[] Buckets);
}
