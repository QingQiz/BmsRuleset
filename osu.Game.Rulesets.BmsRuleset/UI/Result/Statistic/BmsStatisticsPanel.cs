using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsStatisticsPanel : VisibilityContainer
{
    private const double minimum_chart_visibility_duration = 200;
    private const double content_fade_duration = 450;

    internal readonly Bindable<ScoreInfo?> Score = new();

    protected override bool StartHidden => true;

    private readonly Container content;
    private readonly Container? overview;
    private CancellationTokenSource? loadCancellation;
    private BmsResultStatisticsGrid? statistics;
    private double? chartsVisibleSince;
    private Drawable? unavailableMessage;
    private bool unavailableTransitionStarted;

    [Resolved]
    protected BeatmapManager Beatmaps { get; private set; } = null!;

    [Resolved]
    private ScoreManager scores { get; set; } = null!;

    internal BmsStatisticsPanel(bool showOverview = true)
    {
        content = new Container { Name = "Result statistics content", RelativeSizeAxes = Axes.Both };
        InternalChild = new BmsResultColumns(
            showOverview ? overview = new Container { RelativeSizeAxes = Axes.Both } : null,
            content);
    }

    [BackgroundDependencyLoader]
    private void load() => Score.BindValueChanged(populate, true);

    protected virtual Task RestoreReplayDataAsync(ScoreInfo score, CancellationToken cancellationToken) =>
        BmsReplayPatcher.RestoreScoreDataAsync(scores, score, cancellationToken);

    protected virtual async Task<BmsResultStatisticsData?> LoadStatisticsAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        if (score.HitEvents.Count == 0)
            return null;

        var workingBeatmap = Beatmaps.GetWorkingBeatmap(score.BeatmapInfo);
        return await Task.Run(() =>
        {
            var playableBeatmap = workingBeatmap.GetPlayableBeatmap(score.Ruleset, score.Mods);
            cancellationToken.ThrowIfCancellationRequested();
            return BmsResultStatisticsData.Create(score, playableBeatmap);
        }, cancellationToken).ConfigureAwait(false);
    }

    private void populate(ValueChangedEvent<ScoreInfo?> change)
    {
        CancelLoading();
        content.Clear();
        statistics = null;
        overview?.Clear();

        if (change.NewValue is not { } score)
            return;

        if (overview != null)
            overview.Child = new BmsResultOverview(score);

        content.Child = statistics = new BmsResultStatisticsGrid();
        var cancellation = loadCancellation = new CancellationTokenSource();
        _ = populateAsync(score, cancellation.Token);
    }

    private async Task populateAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        try
        {
            await RestoreReplayDataAsync(score, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var data = await LoadStatisticsAsync(score, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Schedule(() =>
            {
                if (IsDisposed || cancellationToken.IsCancellationRequested)
                    return;

                if (overview?.Child is BmsResultOverview resultOverview)
                    resultOverview.SetScore(score);

                if (data != null)
                    statistics!.SetData(data);
                else
                    showUnavailable(new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 15),
                        Children =
                        [
                            new OsuTextFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                TextAnchor = Anchor.Centre,
                                Text = BmsStrings.ResultStatisticsRequireReplay,
                            },
                            new ReplayDownloadButton(score) { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre },
                        ],
                    }, cancellationToken);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to load BMS result statistics.");
            Schedule(() =>
            {
                if (IsDisposed || cancellationToken.IsCancellationRequested)
                    return;

                showUnavailable(new OsuTextFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    TextAnchor = Anchor.Centre,
                    Text = BmsStrings.ResultStatisticsUnavailable,
                }, cancellationToken);
            });
        }
    }

    private void showUnavailable(Drawable message, CancellationToken cancellationToken)
    {
        LoadComponentAsync(message, loaded =>
        {
            if (IsDisposed || cancellationToken.IsCancellationRequested)
                return;

            unavailableMessage = loaded;
        }, cancellationToken);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        if (statistics is not { IsLoaded: true } currentStatistics || unavailableTransitionStarted)
            return;

        // A cached failure can arrive during the screen's entrance; only count time the charts are visible to the user.
        if (State.Value != Visibility.Visible || DrawColourInfo.Colour.MinAlpha < 0.99f
                                               || currentStatistics.DrawWidth <= 0 || currentStatistics.DrawHeight <= 0)
        {
            chartsVisibleSince = null;
            return;
        }

        chartsVisibleSince ??= Time.Current;
        if (unavailableMessage is not { } message || Time.Current - chartsVisibleSince.Value < minimum_chart_visibility_duration)
            return;

        unavailableTransitionStarted = true;
        var cancellationToken = loadCancellation!.Token;
        currentStatistics.FadeOut(content_fade_duration, Easing.InOutSine).OnComplete(_ =>
        {
            if (IsDisposed || cancellationToken.IsCancellationRequested)
                return;

            unavailableMessage = null;
            content.Child = message;
            message.FadeInFromZero(content_fade_duration, Easing.InOutSine);
        });
    }

    protected override bool OnClick(ClickEvent e) => false;

    protected override void PopIn() => this.FadeIn(350, Easing.OutQuint);

    protected override void PopOut() => this.FadeOut(200, Easing.OutQuint);

    internal void CancelLoading()
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        chartsVisibleSince = null;
        unavailableMessage?.Dispose();
        unavailableMessage = null;
        unavailableTransitionStarted = false;
    }

    protected override void Dispose(bool isDisposing)
    {
        CancelLoading();
        Score.ValueChanged -= populate;
        base.Dispose(isDisposing);
    }
}
