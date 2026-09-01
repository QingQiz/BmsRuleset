using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Overlays.Mods;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Result.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseSongSelectController : CompositeDrawable, IKeyBindingHandler<GlobalAction>
{
    internal readonly Bindable<Visibility> State = new();

    internal bool IsCourseMode => State.Value == Visibility.Visible;

    internal bool OriginalCarouselAcceptsInput => originalCarouselHostWrapper.InputEnabled;

    internal bool OriginalCarouselHostAlwaysPresent => originalCarouselHostWrapper.AlwaysPresent;

    internal float OriginalCarouselHostAlpha => originalCarouselHostWrapper.Alpha;

    internal Bindable<string> SearchTerm { get; } = new(string.Empty);

    internal BmsCourseCarousel CourseCarousel { get; }

    internal BmsCourseDefinition? SelectedCourse => selectedCourse.Value;

    internal Action? StartRequested { get; set; }

    internal event Action<bool>? CourseModeChanged;

    private readonly BmsCourseCatalog catalog;
    private readonly BmsSoloSongSelect songSelect;
    private readonly Func<ModSelectOverlay?> modSelectAccessor;
    private readonly BeatmapTitleWedge originalTitle;
    private readonly BmsBeatmapDetailsArea originalDetails;
    private readonly FilterControl originalFilter;
    private readonly BeatmapCarousel originalCarousel;
    private readonly NoResultsPlaceholder originalNoResults;
    private readonly Container carouselHost;
    private readonly MarginPadding originalCarouselHostPadding;
    private readonly Drawable originalTitleWrapper;
    private readonly Drawable originalDetailsWrapper;
    private readonly FillFlowContainer wedgesContainer;
    private readonly BmsCourseCarouselHost originalCarouselHostWrapper;

    private readonly Bindable<BmsCourseDefinition?> selectedCourse = new();
    private readonly BmsCourseDetailsArea courseTitle;
    private readonly BmsCourseHistoryArea courseHistory;
    private readonly BmsCourseFilterControl courseFilter;
    private readonly BmsCourseNoResultsPlaceholder courseNoResults;
    private readonly Drawable courseTitleWrapper;
    private readonly Drawable courseHistoryWrapper;

    private FooterButtonRandom? randomButton;
    private bool courseCarouselPrepared;
    private int matchedCourses = -1;
    private Sample? confirmSelectionSample;
    private WorkingBeatmap? beatmapBeforeCourseMode;
    private ScheduledDelegate? pendingCoursePreviewUpdate;
    private Mod[] modsBeforeCourseMode = [];
    private Mod[] userMods = [];
    private Mod[] lockedMods = [];
    private bool adjustingMods;
    private BmsCourseDefinition? modsAdjustedForCourse;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private INotificationOverlay? notifications { get; set; }

    internal BmsCourseSongSelectController(
        BmsCourseCatalog catalog,
        BmsSoloSongSelect songSelect,
        Func<ModSelectOverlay?> modSelectAccessor,
        FillFlowContainer wedgesContainer,
        BeatmapTitleWedge originalTitle,
        BmsBeatmapDetailsArea originalDetails,
        FilterControl originalFilter,
        BeatmapCarousel originalCarousel,
        NoResultsPlaceholder originalNoResults,
        float topPadding)
    {
        this.catalog = catalog;
        this.songSelect = songSelect;
        this.modSelectAccessor = modSelectAccessor;
        this.wedgesContainer = wedgesContainer;
        this.originalTitle = originalTitle;
        this.originalDetails = originalDetails;
        this.originalFilter = originalFilter;
        this.originalCarousel = originalCarousel;
        this.originalNoResults = originalNoResults;
        carouselHost = (Container)originalCarousel.Parent!;
        originalCarouselHostPadding = carouselHost.Padding;

        originalCarouselHostWrapper = new BmsCourseCarouselHost
        {
            RelativeSizeAxes = Axes.Both,
        };
        carouselHost.Remove(originalCarousel, false);
        carouselHost.Add(originalCarouselHostWrapper);
        originalCarouselHostWrapper.Add(originalCarousel);

        originalTitleWrapper = originalTitle.Parent!;
        originalDetailsWrapper = originalDetails.Parent!;

        AlwaysPresent = true;
        Size = new osuTK.Vector2(1);
        Alpha = 0;

        courseTitle = new BmsCourseDetailsArea(selectedCourse)
        {
            TopPadding = topPadding,
        };
        courseHistory = new BmsCourseHistoryArea(
            selectedCourse,
            presentCourseScore,
            () => IsCourseMode && songSelect.IsCurrentScreen());
        courseFilter = new BmsCourseFilterControl(SearchTerm);
        CourseCarousel = new BmsCourseCarousel
        {
            RelativeSizeAxes = Axes.Both,
            BleedTop = FilterControl.HEIGHT_FROM_SCREEN_TOP + 5,
            BleedBottom = Screens.Footer.ScreenFooter.HEIGHT + 5,
            Alpha = 0,
        };
        courseNoResults = new BmsCourseNoResultsPlaceholder();

        courseTitleWrapper = new Graphics.Containers.ShearAligningWrapper(courseTitle)
        {
            BypassAutoSizeAxes = Axes.Y,
        };
        courseHistoryWrapper = new Graphics.Containers.ShearAligningWrapper(courseHistory)
        {
            BypassAutoSizeAxes = Axes.Y,
        };

        wedgesContainer.AddRange([courseTitleWrapper, courseHistoryWrapper]);
        carouselHost.AddRange([CourseCarousel, courseNoResults]);
        ((Container)originalFilter.Parent!).Add(courseFilter);

        CourseCarousel.CourseSelected += courseSelected;
        CourseCarousel.CourseActivated += courseActivated;
        CourseCarousel.MatchesChanged += matchesChanged;
        SearchTerm.BindValueChanged(searchChanged);
        catalog.Changed += catalogChanged;
        songSelect.Mods.BindValueChanged(onModsChanged);
        CourseCarousel.SetCourses(catalog.Courses);
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audio, RealmAccess realm)
    {
        confirmSelectionSample = audio.Samples.Get(@"SongSelect/confirm-selection");
        BmsRulesetRuntime.EnsureDifficultyTableStore(host, realm);
    }

    internal void AttachRandomButton(FooterButtonRandom? button)
    {
        randomButton = button;

        if (randomButton == null || !IsCourseMode)
            return;

        randomButton.Enabled.Value = false;
    }

    internal void ToggleMode()
    {
        songSelect.ReplaceCourseMode(!IsCourseMode);
    }

    internal void StartCourse(OsuScreen songSelect)
    {
        var course = SelectedCourse;

        if (!IsCourseMode || course == null || !songSelect.IsCurrentScreen())
            return;

        var gaugeType = BmsCourseSession.ResolveCourseGaugeType(songSelect.Mods.Value);

        var resolvedStages = course.Stages.Select(resolveStage).ToArray();

        var missingStages = course.Stages
            .Where((_, index) => resolvedStages[index] == null)
            .ToArray();

        if (missingStages.Length > 0)
        {
            var downloadUrls = ResolveMissingStageDownloadUrls(missingStages, BmsRulesetRuntime.DifficultyTableStore);

            if (downloadUrls == null)
                notifications?.Post(new SimpleNotification { Text = BmsStrings.CourseCannotStartMissingStages });
            else
            {
                foreach (var url in downloadUrls)
                    host.OpenUrlExternally(url);
            }

            return;
        }

        var mods = BmsCourseSession.CreateCourseMods(songSelect.Mods.Value, gaugeType, course.Constraints);
        var session = new BmsCourseSession(course, resolvedStages.Cast<BmsResolvedCourseStage>(), mods, gaugeType);
        var originalBeatmap = beatmapBeforeCourseMode ?? songSelect.Beatmap.Value;

        confirmSelectionSample?.Play();
        // The current global beatmap is the course preview while course mode is visible. Restore the
        // beatmap captured before entering course mode so SongSelect does not refetch a course stage
        // before returning to the normal carousel.
        songSelect.Push(new BmsCourseSessionScreen(session, originalBeatmap, songSelect.Mods.Value));

        BmsResolvedCourseStage? resolveStage(BmsCourseStage stage)
        {
            if (!stage.IsAvailable || string.IsNullOrEmpty(stage.BeatmapHash))
                return null;

            var beatmap = BmsCourseStagePanel.QueryBeatmap(beatmaps, stage.BeatmapHash);
            return beatmap == null ? null : new BmsResolvedCourseStage(stage, beatmap);
        }
    }

    internal static IReadOnlyList<string>? ResolveMissingStageDownloadUrls(
        IEnumerable<BmsCourseStage> missingStages,
        DifficultyTableStore? store)
    {
        var urls = new List<string>();
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stage in missingStages)
        {
            if (string.IsNullOrEmpty(stage.BeatmapHash))
                return null;

            var unavailable = UnavailableTableBeatmapFactory.Resolve(stage.BeatmapHash, store);
            var url = unavailable is { } resolved
                ? UnavailableTableBeatmapFactory.GetDownloadUrl(resolved.Entry)
                : null;

            if (url == null)
                return null;

            if (seenUrls.Add(url))
                urls.Add(url);
        }

        return urls;
    }

    internal void ShowCourseMode(CourseModeRestoration? initialRestoration = null)
    {
        if (IsCourseMode)
            return;

        beatmapBeforeCourseMode = initialRestoration?.Beatmap ?? songSelect.Beatmap.Value;
        selectedCourse.Value = initialRestoration?.CourseId is string courseId
            ? catalog.Courses.FirstOrDefault(course => course.Id == courseId)
            : null;

        State.Value = Visibility.Visible;
        randomButton?.Enabled.Value = false;
        modsBeforeCourseMode = initialRestoration?.Mods.ToArray()
                               ?? songSelect.Mods.Value.Where(x => x is not null).ToArray();
        userMods = modsBeforeCourseMode.Select(mod => mod.DeepClone()).ToArray();
        lockedMods = [];
        modsAdjustedForCourse = null;
        applyModsForCourse(selectedCourse.Value);
        showCourseMode();
        queueCoursePreview(selectedCourse.Value);
        CourseModeChanged?.Invoke(true);
    }

    internal CourseModeRestoration PrepareForScreenReplacement()
    {
        if (!IsCourseMode)
            throw new InvalidOperationException("Course mode must be active before preparing its replacement.");

        pendingCoursePreviewUpdate?.Cancel();
        pendingCoursePreviewUpdate = null;
        courseHistory.CancelPendingRefresh();

        var restoration = new CourseModeRestoration(
            beatmapBeforeCourseMode ?? songSelect.Beatmap.Value,
            modsBeforeCourseMode,
            selectedCourse.Value?.Id);

        State.Value = Visibility.Hidden;
        beatmapBeforeCourseMode = null;
        modsBeforeCourseMode = [];
        userMods = [];
        lockedMods = [];
        modsAdjustedForCourse = null;
        CourseModeChanged?.Invoke(false);
        return restoration;
    }

    protected override void Update()
    {
        base.Update();

        if (!IsCourseMode)
            return;

        courseHistory.RefreshIfPending();

        // SongSelect may update its own wedge visibility in response to beatmap state while course mode is active.
        originalTitle.Alpha = 0;
        originalDetails.Alpha = 0;
        originalFilter.Alpha = 0;
        originalCarousel.Alpha = 0;
        originalNoResults.Alpha = 0;

        courseHistory.Height = Math.Max(0, wedgesContainer.ChildSize.Y - courseTitle.LayoutSize.Y - 4);
        updateCourseCarouselTopPadding();
    }

    protected override void Dispose(bool isDisposing)
    {
        catalog.Changed -= catalogChanged;
        SearchTerm.ValueChanged -= searchChanged;
        songSelect.Mods.ValueChanged -= onModsChanged;
        CourseCarousel.CourseSelected -= courseSelected;
        CourseCarousel.CourseActivated -= courseActivated;
        CourseCarousel.MatchesChanged -= matchesChanged;
        pendingCoursePreviewUpdate?.Cancel();

        base.Dispose(isDisposing);
    }

    public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (!IsCourseMode)
            return false;

        // The mod overlay is owned by song select in the BMS implementation rather than
        // registered with the game's global overlay manager. Let it consume global actions first.
        if (modSelectAccessor()?.State.Value == Visibility.Visible)
            return false;

        switch (e.Action)
        {
            case GlobalAction.SelectPrevious:
                return CourseCarousel.MoveKeyboardSelection(e, -1);

            case GlobalAction.SelectNext:
                return CourseCarousel.MoveKeyboardSelection(e, 1);

            case GlobalAction.Back:
                songSelect.ReplaceCourseMode(false);
                return true;

            default:
                return false;
        }
    }

    public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
    {
    }

    private void showCourseMode()
    {
        originalTitleWrapper.BypassAutoSizeAxes |= Axes.Y;
        originalDetailsWrapper.BypassAutoSizeAxes |= Axes.Y;
        courseTitleWrapper.BypassAutoSizeAxes &= ~Axes.Y;
        courseHistoryWrapper.BypassAutoSizeAxes &= ~Axes.Y;

        originalTitle.Hide();
        originalDetails.Hide();
        originalFilter.Hide();
        originalNoResults.Hide();

        originalCarouselHostWrapper.InputEnabled = false;
        originalCarouselHostWrapper.Alpha = 0;
        originalCarousel.Hide();

        courseTitle.Show();
        courseHistory.Refresh();
        courseFilter.Show();
        CourseCarousel.Show();
        updateCourseCarouselTopPadding();
        CourseCarousel.RestoreSelection(selectedCourse.Value?.Id);
        if (!courseCarouselPrepared)
            CourseCarousel.Refresh();
        updateNoResultsVisibility();
    }

    private void catalogChanged() => Schedule(() =>
    {
        courseCarouselPrepared = false;
        matchedCourses = -1;
        courseNoResults.Hide();
        CourseCarousel.SetCourses(catalog.Courses);
    });

    private void searchChanged(ValueChangedEvent<string> change)
    {
        matchedCourses = -1;
        courseNoResults.Hide();
        CourseCarousel.Search(change.NewValue);
    }

    private void courseSelected(BmsCourseDefinition? course)
    {
        selectedCourse.Value = course;

        if (IsCourseMode)
        {
            applyModsForCourse(course);
            queueCoursePreview(course);
        }
    }

    private void courseActivated() => StartRequested?.Invoke();

    private void matchesChanged(int count)
    {
        courseCarouselPrepared = true;
        matchedCourses = count;
        updateNoResultsVisibility();
    }

    private void updateNoResultsVisibility()
    {
        if (!IsCourseMode || matchedCourses != 0)
        {
            courseNoResults.Hide();
            return;
        }

        courseNoResults.Message = catalog.Courses.Count == 0
            ? BmsStrings.NoCoursesAvailable
            : BmsStrings.NoCoursesMatchSearch;
        courseNoResults.Show();
    }

    private void updateCourseCarouselTopPadding()
    {
        var top = Math.Max(originalCarouselHostPadding.Top, courseFilter.DrawHeight + 5);

        carouselHost.Padding = originalCarouselHostPadding with { Top = top };
        CourseCarousel.BleedTop = top;
    }

    private void queueCoursePreview(BmsCourseDefinition? course)
    {
        pendingCoursePreviewUpdate?.Cancel();
        pendingCoursePreviewUpdate = Scheduler.AddDelayed(() =>
        {
            pendingCoursePreviewUpdate = null;

            if (IsCourseMode)
                updateCoursePreview(course);
        }, osu.Game.Screens.Select.SongSelect.SELECTION_DEBOUNCE);
    }

    private void updateCoursePreview(BmsCourseDefinition? course)
    {
        var candidates = course == null ? [] : CourseCarousel.GetResolvedBeatmaps(course.Id);

        if (candidates.Count == 0)
        {
            if (beatmapBeforeCourseMode != null)
                songSelect.Beatmap.Value = beatmapBeforeCourseMode;

            return;
        }

        var previewBeatmap = candidates[Random.Shared.Next(candidates.Count)];
        songSelect.Beatmap.Value = beatmaps.GetWorkingBeatmap(previewBeatmap);
    }

    private void presentCourseScore(ScoreInfo score, BmsCourseSession? session)
    {
        if (!IsCourseMode || !songSelect.IsCurrentScreen() || score.BeatmapInfo == null || session == null)
            return;

        songSelect.Beatmap.Value = beatmaps.GetWorkingBeatmap(score.BeatmapInfo);
        songSelect.Push(new BmsCourseResultsScreen(session, recordResult: false));
    }

    /// <summary>
    ///     Adjusts the user's mod selection for the given course. The user's manual mod set
    ///     (<see cref="userMods"/>) is the source of truth while course mode is active: required
    ///     constraint mods are added, and mods forbidden by the course are dropped from the active
    ///     selection (while staying in the manual set so they come back when the course changes).
    /// </summary>
    private void applyModsForCourse(BmsCourseDefinition? course)
    {
        var mods = computeCourseMods(course, out var locked);

        lockedMods = locked;
        setMods(mods);
        modsAdjustedForCourse = course;
        updateModSelectFilter(course);
    }

    private Mod[] computeCourseMods(BmsCourseDefinition? course, out Mod[] locked)
    {
        locked = course == null
            ? []
            : BmsCourseSession.ResolveRequiredMods(userMods, BmsCourseSession.CreateConstraintMods(course.Constraints))
                .Select(mod => mod.DeepClone())
                .ToArray();

        var forbidden = course == null
            ? []
            : BmsCourseSession.ResolveForbiddenModTypes(course.Constraints).ToArray();

        var mods = userMods
            .Where(mod => !forbidden.Any(type => type.IsInstanceOfType(mod)))
            .ToList();

        foreach (var lockedMod in locked)
        {
            if (mods.All(mod => mod.GetType() != lockedMod.GetType()))
                mods.Add(lockedMod);
        }

        return mods.ToArray();
    }

    /// <summary>
    ///     Removes mods forbidden by the selected course from the mod select overlay entirely
    ///     via <see cref="ModSelectOverlay.IsValidMod"/>.
    /// </summary>
    private void updateModSelectFilter(BmsCourseDefinition? course)
    {
        var modSelect = modSelectAccessor();
        if (modSelect == null)
            return;

        var forbidden = course == null
            ? []
            : BmsCourseSession.ResolveForbiddenModTypes(course.Constraints).ToArray();

        modSelect.IsValidMod = forbidden.Length == 0
            ? _ => true
            : mod => !forbidden.Any(type => type.IsInstanceOfType(mod));
    }

    private void setMods(IReadOnlyList<Mod> mods)
    {
        adjustingMods = true;
        try
        {
            songSelect.Mods.Value = mods;
        }
        finally
        {
            adjustingMods = false;
        }
    }

    private void onModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> change)
    {
        if (adjustingMods || !IsCourseMode || modsAdjustedForCourse == null)
            return;

        var forbidden = BmsCourseSession.ResolveForbiddenModTypes(modsAdjustedForCourse.Constraints).ToArray();

        userMods = change.NewValue
            .Where(mod => lockedMods.All(locked => locked.GetType() != mod.GetType()))
            .Select(mod => mod.DeepClone())
            .Concat(userMods.Where(mod => forbidden.Any(type => type.IsInstanceOfType(mod))))
            .ToArray();

        applyModsForCourse(modsAdjustedForCourse);
    }

}

internal sealed record CourseModeRestoration(WorkingBeatmap Beatmap, IReadOnlyList<Mod> Mods, string? CourseId = null);

internal sealed partial class BmsCourseCarouselHost : Container
{
    internal bool InputEnabled { get; set; } = true;

    public override bool HandlePositionalInput => false;

    public override bool HandleNonPositionalInput => false;

    public override bool PropagatePositionalInputSubTree => InputEnabled && base.PropagatePositionalInputSubTree;

    public override bool PropagateNonPositionalInputSubTree => InputEnabled && base.PropagateNonPositionalInputSubTree;

}
