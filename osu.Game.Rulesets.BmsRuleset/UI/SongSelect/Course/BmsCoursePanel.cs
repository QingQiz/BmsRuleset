using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class BmsCourseTablePanel : PanelGroup
{
    internal BmsCourseCarousel? CourseCarousel { private get; set; }

    protected override bool OnClick(ClickEvent e)
    {
        if (Item != null)
            CourseCarousel?.Activate(Item);

        return true;
    }
}

internal partial class BmsCoursePanel : Panel
{
    internal const float HEIGHT = PanelGroup.HEIGHT;

    private const float leaf_panel_active_x_offset = 25;

    internal BmsCourseCarousel? CourseCarousel { private get; set; }

    private OsuSpriteText titleText = null!;
    private UpdateableRank courseRank = null!;
    private BmsLampDisplay courseLamp = null!;
    private SpriteIcon courseIcon = null!;
    private BmsCourseDefinition? currentCourse;
    private BmsCourseResultStore? resultStore;
    private Color4 availableIconColour;
    private CancellationTokenSource? resultCancellation;
    private int resultGeneration;
    private ScheduledDelegate? resultQueryOperation;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

    public BmsCoursePanel()
    {
        PanelXOffset = 20;
    }

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = HEIGHT;
        AccentColour = colourProvider.Highlight1;

        availableIconColour = colourProvider.Background5;
        Icon = courseIcon = new SpriteIcon
        {
            Icon = FontAwesome.Solid.Trophy,
            Size = new Vector2(12),
            Margin = new MarginPadding { Left = 4, Right = 3 },
            Colour = availableIconColour,
        };

        Background = courseLamp = new BmsLampDisplay(BmsLamp.NoPlay)
        {
            RelativeSizeAxes = Axes.Both,
            Size = Vector2.One,
        };

        Content.Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientHorizontal(colourProvider.Background4, colourProvider.Background5),
            },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 14, Right = 30 },
                ColumnDimensions =
                [
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(),
                ],
                Content = new[]
                {
                    new Drawable[]
                    {
                        courseRank = new BmsUpdateableRank(animate: false)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(40, 20),
                            Scale = new Vector2(0.8f),
                            Alpha = 0,
                            Margin = new MarginPadding { Right = 5 },
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Children =
                            [
                                titleText = new OsuSpriteText
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Font = OsuFont.Style.Heading2.With(typeface: Typeface.Torus, weight: FontWeight.Bold, italics: false),
                                },
                            ],
                        },
                    },
                },
            },
        ];
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Panel only recognises osu!'s built-in beatmap types as leaf items, so external course panels must restore the same indentation explicitly.
        Expanded.BindValueChanged(_ => updateLeafPanelOffset());
        Selected.BindValueChanged(_ => updateLeafPanelOffset());
        KeyboardSelected.BindValueChanged(_ => updateLeafPanelOffset());
        BmsRulesetRuntime.CourseResultsChanged += resultStoreChanged;
        mods.BindValueChanged(_ => updateResult());
        updateResultStore();
        updateResult();
        updateLeafPanelOffset(false);
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        var groupedCourse = (BmsGroupedCourse)Item!.Model;
        var course = groupedCourse.Course;
        currentCourse = course;

        updateResultStore();
        titleText.Text = course.Name;
        updateAvailability(groupedCourse.HasMissingStage);
        updateResult();
        updateLeafPanelOffset(false);
    }

    protected override void FreeAfterUse()
    {
        cancelResultQuery();
        currentCourse = null;
        base.FreeAfterUse();
    }

    protected override void Dispose(bool isDisposing)
    {
        cancelResultQuery();
        BmsRulesetRuntime.CourseResultsChanged -= resultStoreChanged;
        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        base.Dispose(isDisposing);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (Item != null)
            CourseCarousel?.Activate(Item);

        return true;
    }

    public override MenuItem[] ContextMenuItems => [];

    private void courseResultChanged(string courseId) => Scheduler.Add(() =>
    {
        if (currentCourse?.Id == courseId)
            updateResult();
    });

    private void resultStoreChanged() => Scheduler.Add(() =>
    {
        updateResultStore();
        updateResult();
    });

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

    private void updateResult()
    {
        cancelResultQuery();
        if (IsDisposed || currentCourse == null)
            return;

        courseLamp.Lamp = BmsLamp.NoPlay;
        courseRank.Rank = null;
        courseRank.Alpha = 0;

        // Pool preparation and restored mod bindings can request the same history in one update.
        resultQueryOperation = Schedule(queryResult);
    }

    private void queryResult()
    {
        if (IsDisposed || resultStore == null || currentCourse == null)
            return;

        var courseId = currentCourse.Id;
        var store = resultStore;
        var selectedMods = mods.Value.Select(mod => mod.DeepClone()).ToArray();
        var generation = resultGeneration;
        var cancellation = resultCancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        // Cached histories still need mod selection, which can be expensive across a whole carousel.
        Task.Run(async () => BmsLampScoreSelector.SelectBestCourse(await store.GetHistoryAsync(courseId, token).ConfigureAwait(false), selectedMods), token)
            .ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    if (task.Exception != null)
                        BmsLogger.Error(task.Exception, "Failed to load the BMS course lamp.");
                    return;
                }

                Schedule(() =>
                {
                    if (IsDisposed || token.IsCancellationRequested || generation != resultGeneration)
                        return;

                    var (lamp, rank) = task.Result;
                    courseLamp.Lamp = lamp;
                    courseRank.Rank = rank;
                    courseRank.Alpha = rank.HasValue ? 1 : 0;
                });
            }, TaskScheduler.Default);
    }

    private void cancelResultQuery()
    {
        resultQueryOperation?.Cancel();
        resultGeneration++;
        resultCancellation?.Cancel();
        resultCancellation?.Dispose();
        resultCancellation = null;
    }

    private void updateAvailability(bool hasMissingStage)
    {
        courseIcon.Icon = hasMissingStage ? FontAwesome.Solid.ExclamationTriangle : FontAwesome.Solid.Trophy;
        courseIcon.Colour = hasMissingStage ? Color4.OrangeRed : availableIconColour;
    }

    private void updateLeafPanelOffset(bool animated = true)
    {
        var x = PanelXOffset + CORNER_RADIUS;

        if (!Expanded.Value && !Selected.Value)
            x += leaf_panel_active_x_offset * 2;

        if (!KeyboardSelected.Value)
            x += leaf_panel_active_x_offset;

        TopLevelContent.MoveToX(x, animated ? DURATION : 0, Easing.OutQuint);
    }
}
