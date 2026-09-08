// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// Navigation and result controls adapted from osu.Game.Screens.Ranking.ResultsScreen.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Audio;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Overlays.Volume;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Course;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result;

[Cached]
internal partial class BmsResultsScreen : ScreenWithBeatmapBackground, IKeyBindingHandler<GlobalAction>
{
    public override bool DisallowExternalBeatmapRulesetChanges => true;

    public override bool? AllowGlobalTrackControl => true;

    protected override OverlayActivation InitialOverlayActivationMode => OverlayActivation.UserTriggered;

    // The global back button lives outside the fixed-scale result content.
    protected override bool InitialBackButtonVisibility => false;

    internal readonly Bindable<ScoreInfo?> SelectedScore = new();
    internal readonly ScoreInfo? Score;

    internal bool AllowRetry { get; init; }

    internal bool AllowWatchingReplay { get; init; } = true;

    internal bool ShouldPlayFlair => player != null && Score?.User.IsBot == false;

    internal Container BottomPanel { get; private set; } = null!;

    protected Container ResultsContent { get; private set; } = null!;

    protected BmsStatisticsPanel StatisticsPanel { get; private set; } = null!;

    [Resolved]
    private Player? player { get; set; }

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    [Cached]
    private readonly OverlayColourProvider colourProvider = new(OverlayColourScheme.Aquamarine);

    private Sample? popInSample;
    private PoolableSkinnableSample? rankApplauseSound;
    private bool skipExitTransition;

    internal BmsResultsScreen(ScoreInfo? score)
    {
        Score = score;
        SelectedScore.Value = score;
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audio)
    {
        popInSample = audio.Samples.Get(@"UI/overlay-pop-in");
        StatisticsPanel = CreateStatisticsPanel();
        StatisticsPanel.RelativeSizeAxes = Axes.Both;
        StatisticsPanel.Score.BindTo(SelectedScore);

        InternalChild = new BmsFixedScaleContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, TwoLayerButton.SIZE_EXTENDED.Y)],
                    Content = new Drawable[][]
                    {
                        [
                            ResultsContent = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                Children = [new GlobalScrollAdjustsVolume(), StatisticsPanel],
                            }
                        ],
                        [
                            BottomPanel = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Alpha = 0,
                            }
                        ],
                    },
                },
            },
        };
    }

    protected virtual BmsStatisticsPanel CreateStatisticsPanel() => new();

    protected override void LoadComplete()
    {
        base.LoadComplete();
        BottomPanel.Children = CreateResultControls();
        if (SelectedScore.Value != null)
            StatisticsPanel.Show();
    }

    protected virtual Drawable[] CreateResultControls()
    {
        var buttons = new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Spacing = new Vector2(5),
            Direction = FillDirection.Horizontal,
        };

        var allowHotkeyRetry = false;
        if (AllowWatchingReplay)
        {
            buttons.Add(new ReplayDownloadButton(SelectedScore.Value)
            {
                Score = { BindTarget = SelectedScore },
                Width = 300,
            });
            allowHotkeyRetry = player is ReplayPlayer;
        }

        if (player != null && AllowRetry)
        {
            buttons.Add(new RetryButton { Width = 300 });
            allowHotkeyRetry = true;
        }

        if (allowHotkeyRetry)
        {
            AddInternal(new HotkeyRetryOverlay
            {
                Action = () =>
                {
                    if (!this.IsCurrentScreen())
                        return;

                    skipExitTransition = true;
                    player?.Restart(true);
                },
            });
        }

        if (Score?.BeatmapInfo is { } beatmap)
        {
            buttons.Add(new CollectionButton(beatmap));
            if (beatmap.BeatmapSet is { OnlineID: > 0 } beatmapSet)
                buttons.Add(new FavouriteButton(beatmapSet));
        }

        return
        [
            new Box { RelativeSizeAxes = Axes.Both, Colour = OsuColour.Gray(0.2f) },
            new Container
            {
                Name = "Result action buttons",
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = BmsCourseResultButton.ExpandedWidth + 10, Vertical = 10 },
                Child = new BmsResultFittedContainer(buttons),
            },
            new BmsCourseResultButton
            {
                Name = "Return to song select button",
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = BmsStrings.ReturnToSongSelect,
                Icon = OsuIcon.LeftCircle,
                BackgroundColour = colours.Pink,
                HoverColour = colours.PinkDark,
                Action = () =>
                {
                    if (this.IsCurrentScreen() && !OnBackButton())
                        this.Exit();
                },
            },
        ];
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        ApplyToBackground(background =>
        {
            background.BlurAmount.Value = 10;
            background.FadeColour(OsuColour.Gray(0.5f), 250);
        });
        BottomPanel.FadeIn(250);
        popInSample?.Play();
        Scheduler.AddDelayed(() => OverlayActivationMode.Value = OverlayActivation.All,
            ShouldPlayFlair ? BmsAccuracyCircle.TOTAL_DURATION + 1000 : 0);
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e))
            return true;

        // Hit events retain gameplay hit objects; release them when the result leaves the stack.
        StatisticsPanel.CancelLoading();
        Score?.HitEvents.Clear();
        if (!skipExitTransition)
            this.FadeOut(100);
        rankApplauseSound?.Stop();
        return false;
    }

    internal void PlayApplause(ScoreRank rank)
    {
        if (!this.IsCurrentScreen())
            return;

        rankApplauseSound?.Dispose();
        var samples = new List<string>();
        if (rank >= ScoreRank.B)
            samples.Add("applause");

        samples.Add(rank switch
        {
            ScoreRank.C => "Results/applause-c",
            ScoreRank.B => "Results/applause-b",
            ScoreRank.A => "Results/applause-a",
            ScoreRank.S or ScoreRank.SH or ScoreRank.X or ScoreRank.XH => "Results/applause-s",
            _ => "Results/applause-d",
        });

        LoadComponentAsync(rankApplauseSound = new PoolableSkinnableSample(new SampleInfo(samples.ToArray())), sample =>
        {
            if (!this.IsCurrentScreen() || sample != rankApplauseSound)
                return;

            AddInternal(sample);
            sample.VolumeTo(0.8);
            sample.Play();
        });
    }

    public override bool OnBackButton() => false;

    public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (e.Repeat || !this.IsCurrentScreen())
            return false;

        switch (e.Action)
        {
            case GlobalAction.QuickExit:
                this.Exit();
                return true;

            case GlobalAction.Select:
                OnSelect();
                return true;
        }

        return false;
    }

    protected virtual void OnSelect()
    {
    }

    public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
    {
    }

    protected override bool OnScroll(ScrollEvent e) => !e.CurrentState.Keyboard.AltPressed || base.OnScroll(e);

    private partial class BmsFixedScaleContainer : DrawSizePreservingFillContainer
    {
        private const float design_scale = 0.8f;

        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        internal BmsFixedScaleContainer()
        {
            // Preserve the layout authored at 0.8x regardless of the parent's animated UI scale.
            TargetDrawSize /= design_scale;
        }

        protected override void Update()
        {
            if (game != null)
                TargetDrawSize = game.ScalingContainerTargetDrawSize / design_scale;

            base.Update();
        }
    }
}
