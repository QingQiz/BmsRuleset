using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsCourseSongSelectController : CompositeDrawable, IKeyBindingHandler<GlobalAction>
{
    internal readonly Bindable<Visibility> State = new();

    internal bool IsCourseMode => State.Value == Visibility.Visible;

    internal Bindable<string> SearchTerm { get; } = new(string.Empty);

    internal BmsCourseCarousel CourseCarousel { get; }

    internal event Action<bool>? CourseModeChanged;

    private readonly BmsCourseCatalog catalog;
    private readonly BeatmapTitleWedge originalTitle;
    private readonly BeatmapDetailsArea originalDetails;
    private readonly FilterControl originalFilter;
    private readonly BeatmapCarousel originalCarousel;
    private readonly NoResultsPlaceholder originalNoResults;
    private readonly Drawable originalTitleWrapper;
    private readonly Drawable originalDetailsWrapper;
    private readonly FillFlowContainer wedgesContainer;

    private readonly Bindable<BmsCourseDefinition?> selectedCourse = new();
    private readonly BmsCourseTitleWedge courseTitle;
    private readonly BmsCourseDetailsArea courseDetails;
    private readonly BmsCourseFilterControl courseFilter;
    private readonly BmsCourseNoResultsPlaceholder courseNoResults;
    private readonly Drawable courseTitleWrapper;
    private readonly Drawable courseDetailsWrapper;

    private FooterButtonRandom? randomButton;
    private bool randomButtonEnabledBeforeCourseMode;
    private Visibility originalTitleState;
    private Visibility originalDetailsState;
    private Visibility originalFilterState;
    private Visibility originalNoResultsState;
    private float originalCarouselAlpha = 1;
    private int matchedCourses = -1;

    internal BmsCourseSongSelectController(
        BmsCourseCatalog catalog,
        FillFlowContainer wedgesContainer,
        BeatmapTitleWedge originalTitle,
        BeatmapDetailsArea originalDetails,
        FilterControl originalFilter,
        BeatmapCarousel originalCarousel,
        NoResultsPlaceholder originalNoResults,
        float topPadding)
    {
        this.catalog = catalog;
        this.wedgesContainer = wedgesContainer;
        this.originalTitle = originalTitle;
        this.originalDetails = originalDetails;
        this.originalFilter = originalFilter;
        this.originalCarousel = originalCarousel;
        this.originalNoResults = originalNoResults;

        originalTitleWrapper = originalTitle.Parent!;
        originalDetailsWrapper = originalDetails.Parent!;

        AlwaysPresent = true;
        Size = new osuTK.Vector2(1);
        Alpha = 0;

        courseTitle = new BmsCourseTitleWedge(selectedCourse)
        {
            TopPadding = topPadding,
        };
        courseDetails = new BmsCourseDetailsArea(selectedCourse);
        courseFilter = new BmsCourseFilterControl(SearchTerm);
        CourseCarousel = new BmsCourseCarousel
        {
            RelativeSizeAxes = Axes.Both,
            BleedTop = FilterControl.HEIGHT_FROM_SCREEN_TOP + 5,
            BleedBottom = Screens.Footer.ScreenFooter.HEIGHT + 5,
        };
        courseNoResults = new BmsCourseNoResultsPlaceholder();

        courseTitleWrapper = new Graphics.Containers.ShearAligningWrapper(courseTitle)
        {
            BypassAutoSizeAxes = Axes.Y,
        };
        courseDetailsWrapper = new Graphics.Containers.ShearAligningWrapper(courseDetails)
        {
            BypassAutoSizeAxes = Axes.Y,
        };

        wedgesContainer.AddRange([courseTitleWrapper, courseDetailsWrapper]);
        ((Container)originalCarousel.Parent!).AddRange([CourseCarousel, courseNoResults]);
        ((Container)originalFilter.Parent!).Add(courseFilter);

        CourseCarousel.CourseSelected += courseSelected;
        CourseCarousel.MatchesChanged += matchesChanged;
        SearchTerm.BindValueChanged(searchChanged);
        catalog.Changed += catalogChanged;
        CourseCarousel.SetCourses(catalog.Courses);
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

        State.Value = Visibility.Visible;
        if (randomButton != null)
            randomButton.Enabled.Value = false;
        applyModeVisibility();
        CourseModeChanged?.Invoke(true);
    }

    internal void HideCourseMode()
    {
        if (!IsCourseMode)
            return;

        State.Value = Visibility.Hidden;
        if (randomButton != null)
            randomButton.Enabled.Value = randomButtonEnabledBeforeCourseMode;
        applyModeVisibility();
        CourseModeChanged?.Invoke(false);
    }

    protected override void Update()
    {
        base.Update();

        if (!IsCourseMode)
            return;

        // SongSelect may update its own wedge visibility in response to beatmap state while course mode is active.
        originalTitle.Alpha = 0;
        originalDetails.Alpha = 0;
        originalFilter.Alpha = 0;
        originalCarousel.Alpha = 0;
        originalNoResults.Alpha = 0;

        courseDetails.Height = Math.Max(0, wedgesContainer.ChildSize.Y - courseTitle.LayoutSize.Y - 4);
    }

    protected override void Dispose(bool isDisposing)
    {
        catalog.Changed -= catalogChanged;
        SearchTerm.ValueChanged -= searchChanged;
        CourseCarousel.CourseSelected -= courseSelected;
        CourseCarousel.MatchesChanged -= matchesChanged;

        if (IsCourseMode && randomButton != null)
            randomButton.Enabled.Value = randomButtonEnabledBeforeCourseMode;

        base.Dispose(isDisposing);
    }

    public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (!IsCourseMode || e.Action != GlobalAction.Back)
            return false;

        HideCourseMode();
        return true;
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
            courseDetailsWrapper.BypassAutoSizeAxes &= ~Axes.Y;

            originalTitle.Hide();
            originalDetails.Hide();
            originalFilter.Hide();
            originalCarousel.Hide();
            originalNoResults.Hide();

            courseTitle.Show();
            courseDetails.Show();
            courseFilter.Show();
            CourseCarousel.Show();
            CourseCarousel.Refresh();
            updateNoResultsVisibility();
        }
        else
        {
            originalTitleWrapper.BypassAutoSizeAxes &= ~Axes.Y;
            originalDetailsWrapper.BypassAutoSizeAxes &= ~Axes.Y;
            courseTitleWrapper.BypassAutoSizeAxes |= Axes.Y;
            courseDetailsWrapper.BypassAutoSizeAxes |= Axes.Y;

            restoreVisibility(originalTitle, originalTitleState);
            restoreVisibility(originalDetails, originalDetailsState);
            restoreVisibility(originalFilter, originalFilterState);
            originalCarousel.FadeTo(originalCarouselAlpha, 200, Easing.OutQuint);
            restoreVisibility(originalNoResults, originalNoResultsState);

            courseTitle.Hide();
            courseDetails.Hide();
            courseFilter.Hide();
            CourseCarousel.Hide();
            courseNoResults.Hide();
        }
    }

    private void catalogChanged() => Schedule(() =>
    {
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

    private void courseSelected(BmsCourseDefinition? course) => selectedCourse.Value = course;

    private void matchesChanged(int count)
    {
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

    private static void restoreVisibility(VisibilityContainer container, Visibility state)
    {
        if (state == Visibility.Visible)
            container.Show();
        else
            container.Hide();
    }
}
