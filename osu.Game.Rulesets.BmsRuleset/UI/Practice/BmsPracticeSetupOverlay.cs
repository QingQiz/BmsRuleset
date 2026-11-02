using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Practice;

public partial class BmsPracticeSetupOverlay : FocusedOverlayContainer, IKeyBindingHandler<GlobalAction>
{
    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private readonly WorkingBeatmap source;
    private readonly BmsPracticeSettings? initialSettings;
    private readonly BmsPracticeChart chart;
    private readonly TruncatingSpriteText heading;
    private readonly TruncatingSpriteText subtitle;
    private readonly Container panel;
    private readonly Container chartArea;
    private readonly Container settingsPanel;
    private readonly Container settingsPanelContent;
    private readonly FillFlowContainer settingsContent;
    private readonly OsuScrollContainer settingsScroll;
    private readonly FillFlowContainer viewPanel;
    private readonly FillFlowContainer toolbar;
    private readonly FillFlowContainer viewControls;
    private readonly FillFlowContainer fitButtons;
    private readonly Container snapRow;
    private readonly TruncatingSpriteText status;
    private readonly TruncatingSpriteText summary;
    private readonly OsuSpriteText viewDuration;
    private readonly OsuTextBox startInput;
    private readonly OsuTextBox endInput;
    private readonly RoundedButton startBoundaryButton;
    private readonly RoundedButton endBoundaryButton;
    private readonly IconButton locateStartButton;
    private readonly IconButton locateEndButton;
    private readonly OsuCheckbox skipResultsInput;
    private readonly Bindable<int> countInInput = new BindableInt(3) { MinValue = 0, MaxValue = 5 };
    private readonly RoundedButton[] countInButtons;
    private readonly RoundedSliderBar<double> zoomInput;
    private readonly PracticeSnapDropdown snapInput;
    private readonly RoundedButton startButton;
    private readonly FillFlowContainer rangeInputs;
    private readonly Container startTimeRow;
    private readonly Container endTimeRow;
    private readonly FillFlowContainer repeatInputs;
    private readonly FillFlowContainer countInGroup;
    private readonly FillFlowContainer countInChoices;
    private readonly ModSelectOverlay modSelect;
    private readonly bool ownsModSelect;
    private IReadOnlyList<Mod> localMods;
    private CancellationTokenSource? conversionCancellation;
    private BmsBeatmap? fullBeatmap;
    private BmsPracticeSettings settings = new();
    private bool inputsUpdating;
    private bool firstConversion = true;
    private bool inputValid = true;
    private bool loading;
    private bool refreshingMods;
    private bool startInputEdited;
    private bool endInputEdited;
    private BmsPracticeSettingsStore? store;

    public Action<BmsPracticeSession>? StartRequested { get; set; }
    internal bool IsReady => !loading && fullBeatmap != null && inputValid && settings.IsValidFor(fullBeatmap);
    internal BmsPracticeSettings Settings => settings;
    internal IReadOnlyList<Mod> SelectedMods => localMods;
    internal ModSelectOverlay ModSelection => modSelect;
    internal BmsPracticeChart SelectionChart => chart;
    internal RoundedButton StartButton => startButton;
    internal OsuTextBox StartTimeInput => startInput;
    internal OsuTextBox EndTimeInput => endInput;
    internal RoundedButton StartBoundaryButton => startBoundaryButton;
    internal RoundedButton EndBoundaryButton => endBoundaryButton;
    internal IconButton LocateStartButton => locateStartButton;
    internal IconButton LocateEndButton => locateEndButton;
    internal IReadOnlyList<RoundedButton> CountInButtons => countInButtons;
    internal Drawable CountInGroup => countInGroup;
    internal OsuCheckbox SkipResultsInput => skipResultsInput;
    protected override bool BlockNonPositionalInput => true;

    public BmsPracticeSetupOverlay(WorkingBeatmap source, IReadOnlyList<Mod> initialMods, BmsPracticeSettings? initialSettings = null,
                                  ModSelectOverlay? modSelectOverlay = null)
    {
        this.source = source;
        this.initialSettings = initialSettings;
        RelativeSizeAxes = Axes.Both;
        Depth = -20;
        localMods = BmsPracticeSession.PrepareMods(initialMods);

        panel = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Y,
            Height = 0.94f,
            Masking = true,
            CornerRadius = 10,
        };
        AddInternal(new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.84f) });
        AddInternal(panel);
        panel.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background6 });
        panel.Add(viewPanel = new FillFlowContainer
        {
            Name = "Practice chart controls",
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, section_spacing),
            Depth = -1,
        });
        viewPanel.Add(new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children =
            [
                heading = new TruncatingSpriteText
                {
                    Text = BmsStrings.PracticeSelectRange,
                    Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                    Colour = colourProvider.Content1,
                },
                subtitle = new TruncatingSpriteText
                {
                    Text = BmsStrings.PracticeChartTitle(source.BeatmapInfo.Metadata.Title, source.BeatmapInfo.DifficultyName),
                    Font = OsuFont.GetFont(size: 16),
                    Colour = colourProvider.Light2,
                },
            ],
        });

        chart = new BmsPracticeChart { RelativeSizeAxes = Axes.Y, Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre };
        toolbar = new FillFlowContainer
        {
            Depth = -1,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(control_spacing),
        };
        viewPanel.Add(toolbar);
        toolbar.Add(viewControls = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
            Children =
            [
                viewDuration = text(BmsStrings.PracticeViewDuration(0), 16, true),
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Children =
                    [
                        new IconButton
                        {
                            Icon = FontAwesome.Solid.SearchMinus,
                            Size = new Vector2(36, 40),
                            Action = () => chart.Zoom(1.4),
                            TooltipText = BmsStrings.PracticeZoomOut,
                            IconColour = colourProvider.Content1,
                            IconHoverColour = colourProvider.Highlight1,
                        },
                        zoomInput = new PracticeZoomSlider(chart)
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = 42,
                            Width = 196,
                            Current = new BindableDouble { MinValue = 0, MaxValue = 1 },
                            PlaySamplesOnAdjust = false,
                        },
                        new IconButton
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Icon = FontAwesome.Solid.SearchPlus,
                            Size = new Vector2(36, 40),
                            Action = () => chart.Zoom(1 / 1.4),
                            TooltipText = BmsStrings.PracticeZoomIn,
                            IconColour = colourProvider.Content1,
                            IconHoverColour = colourProvider.Highlight1,
                        },
                    ],
                },
            ],
        });
        toolbar.Add(fitButtons = new FillFlowContainer
        {
            Height = 40,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(10, 0),
            Children =
            [
                button(BmsStrings.PracticeFullChart, () => chart.FitAll(), 108),
                button(BmsStrings.PracticeFitRange, () => chart.FitSelection(), 118),
            ],
        });
        toolbar.Add(snapRow = new Container
        {
            Height = 40,
            Depth = -1,
            Children =
            [
                text(BmsStrings.PracticeSnap, 15).With(t => { t.Anchor = t.Origin = Anchor.CentreLeft; }),
                snapInput = new PracticeSnapDropdown
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.7f,
                    Items = [0, 4, 8, 12, 16, 24, 32, 48, 64],
                    Current = new Bindable<int>(16),
                },
            ],
        });

        // Update the chart after settings have measured their animated height for this frame.
        chartArea = new Container { Name = "Practice chart area", RelativeSizeAxes = Axes.Both, Depth = -0.5f };
        panel.Add(chartArea);
        chartArea.Add(chart);

        settingsPanel = new Container { Name = "Practice settings panel" };
        panel.Add(settingsPanel);
        settingsPanel.Add(new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 8,
            Child = new Box { RelativeSizeAxes = Axes.Both, Colour = colourProvider.Background4 },
        });
        settingsPanel.Add(settingsPanelContent = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(panel_padding) });
        settingsPanelContent.Add(settingsScroll = new PracticeSettingsScroll(updateContentLayout)
        {
            Name = "Practice settings scroll",
            ScrollbarOverlapsContent = true,
            Child = settingsContent = new FillFlowContainer
            {
                Name = "Practice settings fields",
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, section_spacing),
            },
        });
        rangeInputs = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(16) };
        rangeInputs.Add(startTimeRow = new Container
        {
            Height = 84,
            Children =
            [
                startBoundaryButton = button(BmsStrings.PracticeSetStart, () => chart.SelectBoundary(true), 220).With(b => b.Height = 32),
                locateStartButton = locateBoundaryButton(true),
                startInput = timeInput(),
            ],
        });
        rangeInputs.Add(endTimeRow = new Container
        {
            Height = 84,
            Children =
            [
                endBoundaryButton = button(BmsStrings.PracticeSetEnd, () => chart.SelectBoundary(false), 220).With(b => b.Height = 32),
                locateEndButton = locateBoundaryButton(false),
                endInput = timeInput(),
            ],
        });
        settingsContent.Add(rangeInputs);
        repeatInputs = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(16, 0),
        };
        repeatInputs.Add(skipResultsInput = new PracticeResultsCheckbox
        {
            Width = 260,
            RelativeSizeAxes = Axes.None,
            LabelText = BmsStrings.PracticeSkipResults,
        });
        countInButtons = Enumerable.Range(0, 6).Select(value => button(value == 0 ? BmsStrings.PracticeCountInOff : BmsStrings.PracticeCountInSeconds(value),
            () => countInInput.Value = value, 40)).ToArray();
        repeatInputs.Add(countInGroup = new FillFlowContainer
        {
            Name = "Practice count-in settings",
            Masking = true,
            Alpha = 0,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 8),
            Children =
            [
                text(BmsStrings.PracticeCountIn, 16),
                countInChoices = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(4, 0),
                    Children = countInButtons,
                },
            ],
        });
        settingsContent.Add(repeatInputs);
        settingsContent.Add(new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 8),
            Children =
            [
                summary = new TruncatingSpriteText { Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold), Colour = colourProvider.Content1, AllowMultiline = true },
                status = new TruncatingSpriteText { Font = OsuFont.GetFont(size: 14), Text = BmsStrings.PracticeLoading, Colour = colourProvider.Light2, AllowMultiline = true },
            ],
        });
        settingsPanelContent.Add(startButton = button(BmsStrings.PracticeBegin, startPractice, 190).With(b =>
        {
            b.Anchor = b.Origin = Anchor.BottomLeft;
        }));
        startButton.Enabled.Value = false;

        ownsModSelect = modSelectOverlay == null;
        modSelect = modSelectOverlay ?? new ModSelectOverlay { Depth = -30 };
        if (ownsModSelect)
        {
            AddInternal(modSelect);
            modSelect.Beatmap.Value = source;
            modSelect.Ruleset.Value = new BmsRuleset().RulesetInfo;
            ActivateModSelection();
        }
        modSelect.SelectedMods.ValueChanged += selectedModsChanged;

        chart.RangeChanged = (start, end) =>
        {
            settings = settings with { StartTime = start, EndTime = end };
            updateTimeInputs();
            updateSelection();
        };
        chart.ActiveBoundaryChanged = updateBoundaryButtons;
        chart.ViewChanged = () =>
        {
            viewDuration.Text = BmsStrings.PracticeViewDuration(chart.Viewport.Duration);
            var full = chart.Viewport.Maximum - chart.Viewport.Minimum;
            if (full > chart.Viewport.MinimumDuration)
            {
                inputsUpdating = true;
                zoomInput.Current.Value = Math.Log(chart.Viewport.Duration / full) / Math.Log(chart.Viewport.MinimumDuration / full);
                inputsUpdating = false;
            }
        };
        zoomInput.Current.BindValueChanged(e => { if (!inputsUpdating) chart.SetZoom(e.NewValue); });
        snapInput.Current.BindValueChanged(e =>
        {
            chart.SnapDivisor = e.NewValue;
            updateSelection();
        }, true);
        startInput.Current.BindValueChanged(_ => timeInputChanged(true));
        endInput.Current.BindValueChanged(_ => timeInputChanged(false));
        startInput.OnCommit += (_, _) => commitTimeInput(true);
        endInput.OnCommit += (_, _) => commitTimeInput(false);
        skipResultsInput.Current.BindValueChanged(e =>
        {
            updateCountInVisibility(e.NewValue, inputsUpdating ? 0 : 300);
            if (!inputsUpdating)
            {
                settings = settings with { SkipResults = e.NewValue };
                updateSelection();
            }
        }, true);
        countInInput.BindValueChanged(e => { if (!inputsUpdating) { settings = settings with { CountIn = e.NewValue }; updateSelection(); } });
    }

    [BackgroundDependencyLoader]
    private void load(Storage? storage, OsuColour colours)
    {
        if (storage != null) store = new BmsPracticeSettingsStore(storage);
        startButton.BackgroundColour = colours.Pink;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        refreshChart();
    }

    private void refreshChart()
    {
        if (!IsLoaded || IsDisposed) return;
        conversionCancellation?.Cancel();
        conversionCancellation?.Dispose();
        conversionCancellation = new CancellationTokenSource();
        var token = conversionCancellation.Token;
        var mods = localMods;
        loading = true;
        startButton.Enabled.Value = false;
        status.Text = BmsStrings.PracticeLoading;
        _ = Task.Run(() => BmsPracticeSession.GetFullBeatmap(source, mods, token), token).ContinueWith(task =>
        {
            if (token.IsCancellationRequested || IsDisposed) return;
            Schedule(() =>
            {
                if (IsDisposed || token.IsCancellationRequested) return;
                loading = false;
                if (!task.IsCompletedSuccessfully)
                {
                    if (task.Exception != null) Logger.Error(task.Exception, "Failed to prepare practice chart");
                    fullBeatmap = null;
                    status.Text = BmsStrings.PracticeLoadingFailed;
                    startButton.Enabled.Value = false;
                    return;
                }
                fullBeatmap = task.Result;
                if (!localMods.OfType<BmsModBranchReplay>().Any() && BmsPracticeSession.CreateBranchReplay(fullBeatmap) is { } branchReplay)
                    localMods = [.. localMods, branchReplay];
                if (firstConversion)
                {
                    settings = initialSettings is { } initial && initial.IsValidFor(fullBeatmap) ? initial
                        : store?.Load(source.BeatmapInfo.Hash, fullBeatmap) ?? BmsPracticeSettings.CreateDefault(fullBeatmap);
                    inputsUpdating = true;
                    skipResultsInput.Current.Value = settings.SkipResults;
                    countInInput.Value = settings.CountIn;
                    inputsUpdating = false;
                }
                else
                {
                    var bounds = BmsPracticeBeatmap.GetTimeBounds(fullBeatmap);
                    settings = settings with { StartTime = Math.Clamp(settings.StartTime, bounds.StartTime, bounds.EndTime - 0.001), EndTime = Math.Clamp(settings.EndTime, bounds.StartTime + 0.001, bounds.EndTime) };
                }
                chart.SetBeatmap(fullBeatmap, settings, firstConversion);
                if (firstConversion && (settings.StartTime != chart.Viewport.Minimum || settings.EndTime != chart.Viewport.Maximum)) chart.FitSelection();
                firstConversion = false;
                updateTimeInputs();
                updateSelection();
            });
        }, TaskScheduler.Default);
    }

    private void updateTimeInputs()
    {
        inputsUpdating = true;
        startInput.Text = BmsStrings.PracticeInputTime(settings.StartTime).ToString();
        endInput.Text = BmsStrings.PracticeInputTime(settings.EndTime).ToString();
        inputValid = true;
        startInputEdited = endInputEdited = false;
        inputsUpdating = false;
    }

    private void timeInputChanged(bool startChanged)
    {
        if (inputsUpdating) return;
        if (startChanged) startInputEdited = true;
        else endInputEdited = true;
        var validStart = parseTime(startInput.Text, out var start);
        var validEnd = parseTime(endInput.Text, out var end);
        inputValid = validStart && validEnd;
        // The other field may display a rounded musical-grid time; editing one boundary must retain its exact counterpart.
        if (startChanged && validStart) settings = settings with { StartTime = start * 1000 };
        if (!startChanged && validEnd) settings = settings with { EndTime = end * 1000 };
        updateSelection();
    }

    private static bool parseTime(string value, out double seconds)
        => (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) && double.IsFinite(seconds);

    private void commitTimeInput(bool startChanged)
    {
        if (!(startChanged ? startInputEdited : endInputEdited)) return;
        if (!inputValid || fullBeatmap == null) return;
        var bounds = BmsPracticeBeatmap.GetTimeBounds(fullBeatmap);
        var time = startChanged ? settings.StartTime : settings.EndTime;
        if (GetContainingInputManager()?.CurrentState.Keyboard.AltPressed != true) time = chart.SnapTime(time);
        time = Math.Clamp(time, bounds.StartTime, bounds.EndTime);
        settings = startChanged ? settings with { StartTime = time } : settings with { EndTime = time };
        inputsUpdating = true;
        if (startChanged)
        {
            startInput.Text = BmsStrings.PracticeInputTime(time).ToString();
            startInputEdited = false;
        }
        else
        {
            endInput.Text = BmsStrings.PracticeInputTime(time).ToString();
            endInputEdited = false;
        }
        inputsUpdating = false;
        updateSelection();
    }

    private void updateSelection()
    {
        chart.SetSelection(settings);
        updateBoundaryButtons();
        for (var value = 0; value < countInButtons.Length; value++)
            countInButtons[value].BackgroundColour = value == settings.CountIn ? colourProvider.Highlight1 : colourProvider.Background2;
        startButton.Enabled.Value = IsReady;
        var rate = localMods.OfType<ModRateAdjust>().FirstOrDefault()?.SpeedChange.Value ?? 1;
        var bounds = fullBeatmap == null ? default : BmsPracticeBeatmap.GetTimeBounds(fullBeatmap);
        if (loading) status.Text = BmsStrings.PracticeLoading;
        else if (!inputValid || !settings.IsValid || fullBeatmap != null && (settings.StartTime < bounds.StartTime || settings.EndTime > bounds.EndTime)) status.Text = BmsStrings.PracticeInvalidTime;
        else if (!startButton.Enabled.Value) status.Text = BmsStrings.PracticeInvalidRange;
        else status.Text = BmsStrings.PracticeViewHint(snapInput.Current.Value == 0 ? BmsStrings.PracticeSnapMeasure : BmsStrings.PracticeSnapDivision(snapInput.Current.Value));
        if (fullBeatmap != null)
        {
            summary.Text = BmsStrings.PracticeRangeSummary(BmsPracticeBeatmap.SelectedCount(fullBeatmap, settings), Math.Max(0, settings.EndTime - settings.StartTime) / rate, rate);
        }
    }

    private void updateCountInVisibility(bool visible, float duration)
    {
        foreach (var choice in countInButtons)
            choice.Enabled.Value = visible;
        // Keep the spacing inside the clipped group so it folds away with the controls.
        countInGroup.ClearTransforms();
        if (visible)
        {
            countInGroup.Show();
            countInGroup.AutoSizeDuration = duration;
            countInGroup.AutoSizeEasing = Easing.OutQuint;
            countInGroup.AutoSizeAxes = Axes.Y;
        }
        else
        {
            countInGroup.AutoSizeAxes = Axes.None;
            countInGroup.ResizeHeightTo(0, duration, Easing.OutQuint).OnComplete(_ => countInGroup.Hide());
        }
    }

    private void updateBoundaryButtons()
    {
        startBoundaryButton.BackgroundColour = chart.ActiveBoundary == true ? colourProvider.Highlight1 : colourProvider.Background2;
        endBoundaryButton.BackgroundColour = chart.ActiveBoundary == false ? colourProvider.Highlight1 : colourProvider.Background2;
    }

    internal void ActivateModSelection()
    {
        refreshingMods = true;
        modSelect.SelectedMods.Value = localMods.Where(mod => mod.Type != ModType.System).ToArray();
        refreshingMods = false;
    }

    private void selectedModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> e)
    {
        if (refreshingMods || !ownsModSelect && State.Value != Visibility.Visible)
            return;

        var values = e.NewValue.Where(mod => mod.Type != ModType.System).Concat(localMods.OfType<BmsModBranchReplay>());
        localMods = BmsPracticeSession.PrepareMods(values);
        ActivateModSelection();
        refreshChart();
    }

    private void startPractice()
    {
        if (!IsReady) return;
        commitTimeInput(true);
        commitTimeInput(false);
        if (!IsReady) return;
        var session = new BmsPracticeSession(source, settings, localMods);
        Hide();
        StartRequested?.Invoke(session);
    }

    internal bool HandleBack()
    {
        if (modSelect.State.Value == Visibility.Visible)
            modSelect.Hide();
        else
            Hide();
        return true;
    }

    public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (e.Repeat)
            return false;

        switch (e.Action)
        {
            case GlobalAction.Back:
                return HandleBack();
            case GlobalAction.ToggleModSelection:
                modSelect.ToggleVisibility();
                return true;
        }
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
    {
    }

    protected override void PopIn() => this.FadeIn(150);
    protected override void PopOut()
    {
        if (ownsModSelect || IsLoaded)
            modSelect.Hide();
        this.FadeOut(150);
    }

    protected override void Dispose(bool isDisposing)
    {
        modSelect.SelectedMods.ValueChanged -= selectedModsChanged;
        conversionCancellation?.Cancel();
        conversionCancellation?.Dispose();
        base.Dispose(isDisposing);
    }

    private static OsuSpriteText text(LocalisableString value, float size, bool bold = false)
        => new() { Text = value, Font = OsuFont.GetFont(size: size, weight: bold ? FontWeight.Bold : FontWeight.Regular) };

    private partial class PracticeResultsCheckbox : OsuCheckbox, IHasTooltip
    {
        public LocalisableString TooltipText => BmsStrings.PracticeSkipResultsDescription;
    }
    private static OsuTextBox timeInput()
        => new()
        {
            Anchor = Anchor.BottomRight,
            Origin = Anchor.BottomRight,
            RelativeSizeAxes = Axes.X,
            Width = 1,
            Height = 40,
            CommitOnFocusLost = true,
        };
    private IconButton locateBoundaryButton(bool start)
        => new()
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Icon = FontAwesome.Solid.Crosshairs,
            Size = new Vector2(32),
            Action = () => chart.LocateBoundary(start),
            TooltipText = start ? BmsStrings.PracticeLocateStart : BmsStrings.PracticeLocateEnd,
            IconColour = colourProvider.Content1,
            IconHoverColour = colourProvider.Highlight1,
        };
    private RoundedButton button(LocalisableString value, Action action, float width)
        => new() { Text = value, Action = action, Width = width, Height = 40, BackgroundColour = colourProvider.Background2 };

    private partial class PracticeSnapDropdown : OsuDropdown<int>
    {
        protected override LocalisableString GenerateItemText(int item) => item == 0 ? BmsStrings.PracticeSnapMeasure : BmsStrings.PracticeSnapDivision(item);
    }

    private partial class PracticeZoomSlider(BmsPracticeChart chart) : RoundedSliderBar<double>
    {
        protected override LocalisableString GetTooltipText(double value) => BmsStrings.PracticeViewDuration(chart.Viewport.Duration);
    }
}
