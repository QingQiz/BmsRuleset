using System;
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
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Result.Course;
using osu.Game.Scoring;
using osu.Game.Screens.Select;

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
    private readonly SoloSongSelect songSelect;
    private readonly BeatmapTitleWedge originalTitle;
    private readonly BeatmapDetailsArea originalDetails;
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
    private readonly BmsCourseTitleWedge courseTitle;
    private readonly BmsCourseHistoryArea courseHistory;
    private readonly BmsCourseFilterControl courseFilter;
    private readonly BmsCourseNoResultsPlaceholder courseNoResults;
    private readonly Drawable courseTitleWrapper;
    private readonly Drawable courseHistoryWrapper;

    private FooterButtonRandom? randomButton;
    private bool randomButtonEnabledBeforeCourseMode;
    private bool courseCarouselPrepared;
    private Visibility originalTitleState;
    private Visibility originalDetailsState;
    private Visibility originalFilterState;
    private Visibility originalNoResultsState;
    private float originalCarouselAlpha = 1;
    private int matchedCourses = -1;
    private Sample? confirmSelectionSample;
    private WorkingBeatmap? beatmapBeforeCourseMode;
    private ScheduledDelegate? pendingCoursePreviewUpdate;

    [Resolved]
    private BeatmapManager beatmaps { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private INotificationOverlay? notifications { get; set; }

    internal BmsCourseSongSelectController(
        BmsCourseCatalog catalog,
        SoloSongSelect songSelect,
        FillFlowContainer wedgesContainer,
        BeatmapTitleWedge originalTitle,
        BeatmapDetailsArea originalDetails,
        FilterControl originalFilter,
        BeatmapCarousel originalCarousel,
        NoResultsPlaceholder originalNoResults,
        float topPadding)
    {
        this.catalog = catalog;
        this.songSelect = songSelect;
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

        courseTitle = new BmsCourseTitleWedge(selectedCourse)
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
        CourseCarousel.SetCourses(catalog.Courses);
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audio, GameHost host, RealmAccess realm)
    {
        confirmSelectionSample = audio.Samples.Get(@"SongSelect/confirm-selection");
        BmsRulesetRuntime.EnsureDifficultyTableStore(host, realm);
    }

    internal void AttachRandomButton(FooterButtonRandom? button)
    {
        randomButton = button;

        if (randomButton == null || !IsCourseMode)
            return;

        randomButtonEnabledBeforeCourseMode = randomButton.Enabled.Value;
        randomButton.Enabled.Value = false;
    }

    internal void ToggleMode()
    {
        if (IsCourseMode)
            HideCourseMode();
        else
            ShowCourseMode();
    }

    internal void StartCourse(SoloSongSelect songSelect)
    {
        var course = SelectedCourse;

        if (!IsCourseMode || course == null || !songSelect.IsCurrentScreen())
            return;

        var gaugeType = BmsCourseSession.ResolveCourseGaugeType(songSelect.Mods.Value);

        var resolvedStages = course.Stages.Select(resolveStage).ToArray();

        if (resolvedStages.Any(stage => stage == null))
        {
            notifications?.Post(new SimpleNotification { Text = BmsStrings.CourseCannotStartMissingStages });
            return;
        }

        var mods = BmsCourseSession.CreateCourseMods(songSelect.Mods.Value, gaugeType);
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

    internal void ShowCourseMode()
    {
        if (IsCourseMode)
            return;

        originalTitleState = originalTitle.State.Value;
        originalDetailsState = originalDetails.State.Value;
        originalFilterState = originalFilter.State.Value;
        originalNoResultsState = originalNoResults.State.Value;
        originalCarouselAlpha = originalCarousel.Alpha;
        randomButtonEnabledBeforeCourseMode = randomButton?.Enabled.Value ?? false;
        beatmapBeforeCourseMode = songSelect.Beatmap.Value;

        State.Value = Visibility.Visible;
        randomButton?.Enabled.Value = false;
        applyModeVisibility();
        queueCoursePreview(selectedCourse.Value);
        CourseModeChanged?.Invoke(true);
    }

    internal void HideCourseMode()
    {
        if (!IsCourseMode)
            return;

        pendingCoursePreviewUpdate?.Cancel();
        pendingCoursePreviewUpdate = null;
        courseHistory.CancelPendingRefresh();
        State.Value = Visibility.Hidden;
        restoreBeatmapBeforeCourseMode();
        randomButton?.Enabled.Value = randomButtonEnabledBeforeCourseMode;
        applyModeVisibility();
        CourseModeChanged?.Invoke(false);
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
        CourseCarousel.CourseSelected -= courseSelected;
        CourseCarousel.CourseActivated -= courseActivated;
        CourseCarousel.MatchesChanged -= matchesChanged;
        pendingCoursePreviewUpdate?.Cancel();

        if (IsCourseMode && randomButton != null)
            randomButton.Enabled.Value = randomButtonEnabledBeforeCourseMode;

        if (IsCourseMode)
            carouselHost.Padding = originalCarouselHostPadding;

        if (IsCourseMode)
            restoreBeatmapBeforeCourseMode();

        base.Dispose(isDisposing);
    }

    public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (!IsCourseMode)
            return false;

        switch (e.Action)
        {
            case GlobalAction.SelectPrevious:
                return CourseCarousel.MoveKeyboardSelection(e, -1);

            case GlobalAction.SelectNext:
                return CourseCarousel.MoveKeyboardSelection(e, 1);

            case GlobalAction.Back:
                HideCourseMode();
                return true;

            default:
                return false;
        }
    }

    public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
    {
    }

    private void applyModeVisibility()
    {
        if (IsCourseMode)
        {
            originalTitleWrapper.BypassAutoSizeAxes |= Axes.Y;
            originalDetailsWrapper.BypassAutoSizeAxes |= Axes.Y;
            courseTitleWrapper.BypassAutoSizeAxes &= ~Axes.Y;
            courseHistoryWrapper.BypassAutoSizeAxes &= ~Axes.Y;

            originalTitle.Hide();
            originalDetails.Hide();
            originalFilter.Hide();
            originalNoResults.Hide();

            originalCarouselHostWrapper.AlwaysPresent = false;
            originalCarouselHostWrapper.InputEnabled = false;
            originalCarouselHostWrapper.Alpha = 0;
            originalCarousel.Hide();

            courseTitle.Show();
            courseHistory.Refresh();
            courseFilter.Show();
            CourseCarousel.Show();
            updateCourseCarouselTopPadding();
            if (!courseCarouselPrepared)
                CourseCarousel.Refresh();
            updateNoResultsVisibility();
        }
        else
        {
            originalTitleWrapper.BypassAutoSizeAxes &= ~Axes.Y;
            originalDetailsWrapper.BypassAutoSizeAxes &= ~Axes.Y;
            courseTitleWrapper.BypassAutoSizeAxes |= Axes.Y;
            courseHistoryWrapper.BypassAutoSizeAxes |= Axes.Y;

            restoreVisibility(originalTitle, originalTitleState);
            restoreVisibility(originalDetails, originalDetailsState);
            restoreVisibility(originalFilter, originalFilterState);
            originalCarouselHostWrapper.AlwaysPresent = true;
            originalCarouselHostWrapper.InputEnabled = true;
            originalCarouselHostWrapper.FadeTo(1, 200, Easing.OutQuint);
            originalCarousel.Show();
            originalCarousel.FadeTo(originalCarouselAlpha, 200, Easing.OutQuint);
            restoreVisibility(originalNoResults, originalNoResultsState);
            carouselHost.Padding = originalCarouselHostPadding;

            courseTitle.Hide();
            courseHistory.Hide();
            courseFilter.Hide();
            CourseCarousel.Hide();
            courseNoResults.Hide();
        }
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
            queueCoursePreview(course);
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

    private void restoreBeatmapBeforeCourseMode()
    {
        if (beatmapBeforeCourseMode == null)
            return;

        songSelect.Beatmap.Value = beatmapBeforeCourseMode;
        beatmapBeforeCourseMode = null;
    }

    private static void restoreVisibility(VisibilityContainer container, Visibility state)
    {
        if (state == Visibility.Visible)
        {
            // SongSelect may have already called Show() while returning from gameplay, after which course mode keeps the drawable transparent.
            // Re-entering the visible state is required to restart both its position and alpha transforms.
            if (container.State.Value == Visibility.Visible)
                container.Hide();

            container.Show();
        }
        else
            container.Hide();
    }
}

internal sealed partial class BmsCourseCarouselHost : Container
{
    internal bool InputEnabled { get; set; } = true;

    public override bool HandlePositionalInput => false;

    public override bool HandleNonPositionalInput => false;

    public override bool PropagatePositionalInputSubTree => InputEnabled && base.PropagatePositionalInputSubTree;

    public override bool PropagateNonPositionalInputSubTree => InputEnabled && base.PropagateNonPositionalInputSubTree;

    protected override void Update()
    {
        base.Update();

        // Keep the reveal animation alive, then let the normal drawable lifecycle resume.
        if (InputEnabled && AlwaysPresent && Alpha > 0)
            AlwaysPresent = false;
    }
}
