using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Statistics;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;

internal partial class BmsStatisticsPanel : StatisticsPanel
{
    private readonly Container content;
    private readonly LoadingSpinner spinner;
    private readonly Container? overview;
    private CancellationTokenSource? loadCancellation;

    [Resolved]
    protected BeatmapManager Beatmaps { get; private set; } = null!;

    internal BmsStatisticsPanel(bool showOverview = true)
    {
        var charts = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                content = new Container { RelativeSizeAxes = Axes.Both },
                spinner = new LoadingSpinner(),
            ],
        };
        InternalChild = new BmsResultColumns(
            showOverview ? overview = new Container { RelativeSizeAxes = Axes.Both } : null,
            charts);
    }

    [BackgroundDependencyLoader]
    private void load() => Score.BindValueChanged(populate, true);

    protected override IEnumerable<StatisticItem> CreateStatisticItems(ScoreInfo newScore, IBeatmap playableBeatmap) =>
        newScore.Ruleset.CreateInstance().CreateStatisticsForScore(newScore, playableBeatmap);

    protected virtual async Task<StatisticItem[]> LoadStatisticItemsAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        var workingBeatmap = Beatmaps.GetWorkingBeatmap(score.BeatmapInfo);
        var playableBeatmap = await Task.Run(() => workingBeatmap.GetPlayableBeatmap(score.Ruleset, score.Mods), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return CreateStatisticItems(score, playableBeatmap).ToArray();
    }

    private void populate(ValueChangedEvent<ScoreInfo?> change)
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        content.Clear();
        overview?.Clear();
        spinner.Hide();

        if (change.NewValue is not { } score)
            return;

        if (overview != null)
            overview.Child = new BmsResultOverview(score);

        spinner.Show();
        var cancellation = loadCancellation = new CancellationTokenSource();
        _ = populateAsync(score, cancellation.Token);
    }

    private async Task populateAsync(ScoreInfo score, CancellationToken cancellationToken)
    {
        try
        {
            var items = await LoadStatisticItemsAsync(score, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Schedule(() =>
            {
                if (IsDisposed || cancellationToken.IsCancellationRequested)
                    return;

                var availableItems = items.Where(item => !item.RequiresHitEvents || score.HitEvents.Count > 0).ToArray();
                Drawable statistics = availableItems.Length > 0
                    ? new BmsResultStatisticsGrid(availableItems)
                    : new FillFlowContainer
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
                    };

                LoadComponentAsync(statistics, loaded =>
                {
                    if (IsDisposed || cancellationToken.IsCancellationRequested)
                        return;

                    content.Child = loaded;
                    spinner.Hide();
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

                spinner.Hide();
                content.Child = new OsuTextFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = BmsStrings.ResultStatisticsUnavailable,
                };
            });
        }
    }

    protected override bool OnClick(ClickEvent e) => false;

    protected override void Dispose(bool isDisposing)
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        Score.ValueChanged -= populate;
        base.Dispose(isDisposing);
    }
}
