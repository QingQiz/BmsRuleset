using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class BmsCourseHistoryArea : VisibilityContainer
{
    private const float header_height = 35;
    private const int drawable_batch_size = 10;

    private readonly IBindable<BmsCourseDefinition?> selectedCourse;
    private readonly Action<ScoreInfo, BmsCourseSession?> presentScore;
    private readonly Func<bool> isActive;
    private BmsCourseResultStore? resultStore;
    private BmsCourseHistoryHeader header = null!;
    private FillFlowContainer content = null!;
    private CancellationTokenSource? refreshCancellation;
    private long refreshGeneration;
    private volatile bool refreshPending;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    internal BmsCourseHistoryArea(
        IBindable<BmsCourseDefinition?> selectedCourse,
        Action<ScoreInfo, BmsCourseSession?> presentScore,
        Func<bool> isActive)
    {
        this.selectedCourse = selectedCourse;
        this.presentScore = presentScore;
        this.isActive = isActive;
        RelativeSizeAxes = Axes.X;
        X = -150;
    }

    protected override bool StartHidden => true;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren =
        [
            new ShearAligningWrapper(header = new BmsCourseHistoryHeader
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.X,
                Height = header_height,
            }),
            new ShearAligningWrapper(new Container
            {
                Shear = -OsuGame.SHEAR,
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = header_height },
                Child = new OsuContextMenuContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new OsuScrollContainer
                    {
                        Shear = OsuGame.SHEAR,
                        RelativeSizeAxes = Axes.Both,
                        ScrollbarVisible = false,
                        Child = content = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, BmsBeatmapLeaderboardWedge.SPACING_BETWEEN_SCORES),
                            Padding = new MarginPadding
                            {
                                Top = 5,
                                Left = 80,
                                Bottom = BmsLeaderboardScore.HEIGHT * 3,
                            },
                        },
                    },
                },
            })
            {
                Depth = 1,
            },
        ];

        selectedCourse.BindValueChanged(_ => requestRefresh());
        header.Sorting.BindValueChanged(_ => requestRefresh());
        header.FilterBySelectedMods.BindValueChanged(_ => requestRefresh());
        BmsRulesetRuntime.CourseResultsChanged += resultStoreChanged;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        mods.BindValueChanged(_ =>
        {
            if (header.FilterBySelectedMods.Value)
                requestRefresh();
        });
        Refresh();
    }

    protected override void Dispose(bool isDisposing)
    {
        BmsRulesetRuntime.CourseResultsChanged -= resultStoreChanged;
        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        cancelPendingRefresh();
        base.Dispose(isDisposing);
    }

    protected override void PopIn()
    {
        this.MoveToX(0, Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeIn(Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    protected override void PopOut()
    {
        this.MoveToX(-150, Screens.Select.SongSelect.ENTER_DURATION, Easing.OutQuint)
            .FadeOut(Screens.Select.SongSelect.ENTER_DURATION / 3, Easing.In);
    }

    internal void Refresh()
    {
        refreshPending = false;
        updateResultStore();
        cancelPendingRefresh();

        var course = selectedCourse.Value;
        if (course == null || resultStore == null)
        {
            content.Clear();
            Hide();
            return;
        }

        var store = resultStore;
        var generation = refreshGeneration;
        var cancellation = refreshCancellation = new CancellationTokenSource();
        AlwaysPresent = true;
        var filterBySelectedMods = header.FilterBySelectedMods.Value;
        var selectedModAcronyms = mods.Value.Where(isFilterableMod).Select(mod => mod.Acronym).ToHashSet();
        var sorting = header.Sorting.Value;

        store.GetHistoryAsync(course.Id, cancellation.Token).ContinueWith(historyTask =>
        {
            if (historyTask.IsCanceled || cancellation.IsCancellationRequested)
                return;

            if (historyTask.IsFaulted)
            {
                handleRefreshFailure(course, store, generation, cancellation, historyTask.Exception!);
                return;
            }

            Scheduler.Add(() => beginSessionLoad(
                course,
                store,
                historyTask.Result,
                filterBySelectedMods,
                selectedModAcronyms,
                sorting,
                generation,
                cancellation));
        }, TaskScheduler.Default);
    }

    internal void RefreshIfPending()
    {
        if (refreshPending && isActive())
            Refresh();
    }

    internal void CancelPendingRefresh()
    {
        refreshPending = true;
        cancelPendingRefresh();
    }

    private void beginSessionLoad(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        IReadOnlyList<BmsCourseResult> history,
        bool filterBySelectedMods,
        HashSet<string> selectedModAcronyms,
        LeaderboardSortMode sorting,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!refreshIsCurrent(course, store, generation, cancellation))
            return;

        realm.RunAsync(r =>
        {
            var entries = new List<CourseHistoryEntry>();

            foreach (var result in history)
            {
                cancellation.Token.ThrowIfCancellationRequested();

                if (result.Attempt == null || createHistorySession(course, result.Attempt, r, cancellation.Token) is not { } session)
                    continue;

                var score = BmsCourseScoreAggregation.CreateScore(session);

                if (!filterBySelectedMods || matchesSelectedMods(score, selectedModAcronyms))
                    entries.Add(new CourseHistoryEntry(result, session, score));
            }

            var orderedScores = entries.Select(entry => entry.Score).OrderByCriteria(sorting);
            return orderedScores.Select(score => entries.Single(entry => ReferenceEquals(entry.Score, score))).ToArray();
        }, cancellation.Token).ContinueWith(entriesTask =>
        {
            if (entriesTask.IsCanceled || cancellation.IsCancellationRequested)
                return;

            if (entriesTask.IsFaulted)
            {
                handleRefreshFailure(course, store, generation, cancellation, entriesTask.Exception!);
                return;
            }

            Scheduler.Add(() => beginDrawableLoad(course, store, entriesTask.Result, generation, cancellation));
        }, TaskScheduler.Default);
    }

    private void beginDrawableLoad(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        IReadOnlyList<CourseHistoryEntry> entries,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!refreshIsCurrent(course, store, generation, cancellation))
            return;

        if (entries.Count == 0)
        {
            content.Clear();
            Hide();
            AlwaysPresent = false;
            return;
        }

        loadDrawableBatch(course, store, entries, 0, generation, cancellation);
    }

    private void loadDrawableBatch(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        IReadOnlyList<CourseHistoryEntry> entries,
        int offset,
        long generation,
        CancellationTokenSource cancellation)
    {
        if (!refreshIsCurrent(course, store, generation, cancellation))
            return;

        var drawables = entries.Skip(offset).Take(drawable_batch_size).Select((entry, index) => new BmsLeaderboardScore(entry.Score)
        {
            Rank = offset + index + 1,
            Shear = Vector2.Zero,
            SelectedMods = { BindTarget = mods },
            Action = () => presentScore(entry.Score, entry.Session),
            DeleteScore = () => store.Delete(course.Id, entry.Result),
            DeleteConfirmation = BmsStrings.CourseHistoryDeleteConfirmation,
        }).ToArray();

        LoadComponentsAsync(drawables, loadedDrawables =>
        {
            if (!refreshIsCurrent(course, store, generation, cancellation))
                return;

            if (offset == 0)
                content.Clear();

            content.AddRange(loadedDrawables);
            Show();

            var nextOffset = offset + drawables.Length;
            if (nextOffset < entries.Count)
                Scheduler.Add(() => loadDrawableBatch(course, store, entries, nextOffset, generation, cancellation));
            else
                AlwaysPresent = false;
        }, cancellation.Token);
    }

    private void updateResultStore()
    {
        var current = BmsRulesetRuntime.CourseResults;
        if (ReferenceEquals(resultStore, current))
            return;

        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        resultStore = current;
        if (resultStore != null)
            resultStore.Changed += courseResultChanged;

    }

    private void courseResultChanged(string courseId) => Scheduler.Add(() =>
    {
        if (selectedCourse.Value?.Id == courseId)
            requestRefresh();
    });

    private void resultStoreChanged() => Scheduler.Add(requestRefresh);

    private void requestRefresh()
    {
        refreshPending = true;
        cancelPendingRefresh();
    }

    private void cancelPendingRefresh()
    {
        Interlocked.Increment(ref refreshGeneration);
        // Deliberately no Dispose: in-flight Realm tasks and continuations may still access
        // `cancellation.Token` after cancellation (see refreshIsCurrent), and accessing a token
        // on a disposed source throws ObjectDisposedException. The CTS has a finaliser that
        // releases its kernel handle, and refreshes are infrequent.
        refreshCancellation?.Cancel();
        refreshCancellation = null;
        AlwaysPresent = false;
    }

    private BmsCourseSession? createHistorySession(BmsCourseDefinition course, BmsCourseAttemptData? attempt, Realms.Realm scoreRealm, CancellationToken cancellationToken)
    {
        if (attempt == null || attempt.Stages.Length != course.Stages.Count)
            return null;

        var resolvedStages = new BmsResolvedCourseStage[attempt.Stages.Length];
        var restoredStages = new BmsRestoredCourseStage[attempt.Stages.Length];

        for (var i = 0; i < attempt.Stages.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var beatmap = BmsCourseStagePanel.QueryBeatmap(beatmaps, attempt.Stages[i].BeatmapHash);
            if (beatmap == null)
                return null;

            resolvedStages[i] = new BmsResolvedCourseStage(course.Stages[i], beatmap);
            ScoreInfo? stageScore = null;

            if (attempt.Stages[i].ScoreId is { } scoreId)
                stageScore = scoreRealm.Find<ScoreInfo>(scoreId) is { DeletePending: false } score ? score.DeepClone() : null;

            if (attempt.Stages[i].Status != BmsCourseStageStatus.NotPlayed && stageScore == null)
                return null;

            restoredStages[i] = new BmsRestoredCourseStage(
                attempt.Stages[i].Status,
                stageScore,
                attempt.Stages[i].EndingHealth);
        }

        var ruleset = resolvedStages[0].Beatmap.Ruleset.CreateInstance();
        var courseMods = attempt.ModAcronyms.Select(ruleset.CreateModFromAcronym).OfType<Mod>();
        return BmsCourseSession.Restore(course, resolvedStages, courseMods, attempt.GaugeType, attempt.Status, restoredStages);
    }

    private bool refreshIsCurrent(BmsCourseDefinition course, BmsCourseResultStore store, long generation, CancellationTokenSource cancellation) =>
        !IsDisposed
        && !cancellation.IsCancellationRequested
        && generation == Interlocked.Read(ref refreshGeneration)
        && ReferenceEquals(resultStore, store)
        && selectedCourse.Value?.Id == course.Id
        && isActive();

    private static bool matchesSelectedMods(ScoreInfo score, HashSet<string> selectedAcronyms) =>
        BmsLampScoreSelector.MatchesExactAcronyms(score.Mods, selectedAcronyms, isFilterableMod);

    private static void logRefreshFailure(Exception exception)
    {
        var error = exception is AggregateException aggregate ? aggregate.GetBaseException() : exception;

        if (error is not OperationCanceledException)
            BmsLogger.Error(error, $"Failed to load BMS course history: {error.Message}");
    }

    private void handleRefreshFailure(
        BmsCourseDefinition course,
        BmsCourseResultStore store,
        long generation,
        CancellationTokenSource cancellation,
        Exception exception)
    {
        logRefreshFailure(exception);
        Scheduler.Add(() =>
        {
            if (refreshIsCurrent(course, store, generation, cancellation))
                AlwaysPresent = false;
        });
    }

    private static bool isFilterableMod(Mod mod) => mod.Type != ModType.System && mod is not BmsModGauge;

    private sealed record CourseHistoryEntry(BmsCourseResult Result, BmsCourseSession Session, ScoreInfo Score);
}
