using System;
using System.Reflection;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsCourseStageResultsScreen : ResultsScreen
{
    private static readonly FieldInfo bottom_panel_field = typeof(ResultsScreen).GetField("bottomPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly Action nextStage;
    private readonly Action abandonCourse;
    private readonly BmsCourseCountdown countdown = new();

    private OsuSpriteText countdownText = null!;
    private Sample? countdownSample;
    private bool actionCompleted;
    private bool allowExit;

    [Resolved(canBeNull: true)]
    private IDialogOverlay? dialogOverlay { get; set; }

    [Resolved]
    private OsuColour colours { get; set; } = null!;

    internal BmsCourseStageResultsScreen(ScoreInfo score, Action nextStage, Action abandonCourse)
        : base(cloneScore(score))
    {
        this.nextStage = nextStage;
        this.abandonCourse = abandonCourse;
        BackButtonVisibility.Value = false;
        AllowWatchingReplay = false;
        AllowRetry = false;
    }

    private static ScoreInfo cloneScore(ScoreInfo score)
        => BmsScoreGaugeHistoryStore.Clone(score);

    [BackgroundDependencyLoader]
    private void load(AudioManager audio)
    {
        countdownSample = audio.Samples.Get(@"Multiplayer/countdown-warn");
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        var bottomPanel = (Container)bottom_panel_field.GetValue(this)!;

        bottomPanel.Name = "Course stage result controls";
        bottomPanel.Children =
        [
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colours.Gray3,
                Depth = 1,
            },
            new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(5),
                Depth = 0,
                ColumnDimensions =
                [
                    new Dimension(GridSizeMode.Absolute, 220),
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 260),
                ],
                Content = new[]
                {
                    new Drawable[]
                    {
                        new RoundedButton
                        {
                            Name = "Abandon course button",
                            RelativeSizeAxes = Axes.Both,
                            Size = osuTK.Vector2.One,
                            Text = BmsStrings.AbandonCourse,
                            BackgroundColour = colours.Red3,
                            Action = requestAbandon,
                        },
                        countdownText = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = OsuFont.Style.Heading2.With(weight: FontWeight.Bold),
                        },
                        new RoundedButton
                        {
                            Name = "Start next course stage button",
                            RelativeSizeAxes = Axes.Both,
                            Size = osuTK.Vector2.One,
                            Text = BmsStrings.StartNextCourseStage,
                            BackgroundColour = colours.Green3,
                            Action = advance,
                        },
                    },
                },
            },
        ];
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);

        countdown.Start();
    }

    protected override void Update()
    {
        base.Update();

        if (actionCompleted || !countdown.IsStarted)
            return;

        var state = countdown.Update();
        countdownText.Text = BmsStrings.CourseNextStageCountdown(state.SecondsRemaining);

        if (state.PlayCue)
            countdownSample?.Play();

        if (state.HasExpired)
            advance();
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (!allowExit)
        {
            requestAbandon();
            return true;
        }

        return base.OnExiting(e);
    }

    protected override Task<ScoreInfo[]> FetchScores() => Task.FromResult<ScoreInfo[]>([]);

    private void advance() => completeAction(nextStage);

    private void requestAbandon()
    {
        if (actionCompleted || dialogOverlay == null)
            return;

        dialogOverlay.Push(new BmsCourseAbandonDialog(() => completeAction(abandonCourse)));
    }

    private void completeAction(Action action)
    {
        if (actionCompleted)
            return;

        actionCompleted = true;
        dialogOverlay?.CurrentDialog?.Hide();
        action();
        allowExit = true;
        this.Exit();
    }
}

internal partial class BmsCourseAbandonDialog : PopupDialog
{
    internal BmsCourseAbandonDialog(Action confirmed)
    {
        HeaderText = BmsStrings.AbandonCourse;
        BodyText = BmsStrings.AbandonCourseConfirmation;
        Icon = FontAwesome.Solid.ExclamationTriangle;
        Buttons =
        [
            new PopupDialogDangerousButton
            {
                Text = BmsStrings.AbandonCourseConfirm,
                Action = confirmed,
            },
            new PopupDialogCancelButton
            {
                Text = BmsStrings.Cancel,
            },
        ];
    }
}
