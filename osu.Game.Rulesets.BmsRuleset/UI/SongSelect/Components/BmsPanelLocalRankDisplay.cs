// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.UI.Ranking;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

internal partial class BmsPanelLocalRankDisplay : PanelLocalRankDisplay
{
    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();

    private IDisposable? scoreSubscription;
    private CancellationTokenSource? scoreQueryCancellation;
    private int scoreQueryGeneration;

    private readonly UpdateableRank updateable;
    private readonly BmsLampDisplay lampDisplay;

    internal void AttachLamp(Container host)
    {
        if (lampDisplay.Parent == this)
            RemoveInternal(lampDisplay, false);
        else if (lampDisplay.Parent is Container parent)
            parent.Remove(lampDisplay, false);

        lampDisplay.RelativeSizeAxes = Axes.Both;
        lampDisplay.Width = 1;
        lampDisplay.Height = 1;
        host.Add(lampDisplay);
    }

    internal void DetachLamp()
    {
        if (lampDisplay.Parent == this)
            return;

        if (lampDisplay.Parent is Container parent)
            parent.Remove(lampDisplay, false);

        if (lampDisplay.Parent == null)
            AddInternal(lampDisplay);

        lampDisplay.RelativeSizeAxes = Axes.None;
        lampDisplay.Size = new Vector2(40, 20);
    }

    [Resolved(CanBeNull = true)]
    private IBindable<IReadOnlyList<Mod>>? selectedMods { get; set; }

    public BmsPanelLocalRankDisplay(BeatmapInfo? beatmap = null)
        : base(beatmap)
    {
        AutoSizeAxes = Axes.Both;

        InternalChildren =
        [
            updateable = new BmsUpdateableRank(animate: false)
            {
                Size = new Vector2(40, 20),
                Alpha = 0,
            },
            lampDisplay = new BmsLampDisplay(BmsLamp.NoPlay)
            {
                Size = new Vector2(40, 20),
                Alpha = 0,
            },
        ];

    }

    internal void RefreshBmsScores() => updateSubscription();

    [BackgroundDependencyLoader]
    private void load(IAPIProvider api)
    {
        localUser.BindTo(api.LocalUser);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        ruleset.BindValueChanged(_ => updateSubscription());
        localUser.BindValueChanged(_ => updateSubscription(), true);
        selectedMods?.BindValueChanged(_ => updateLamp());
    }

    private void updateSubscription()
    {
        scoreSubscription?.Dispose();
        setRankFromScore(null);

        if (Beatmap == null)
            return;

        var beatmapHash = Beatmap.Hash;
        scoreSubscription = realm.RegisterForNotifications(r => r.All<ScoreInfo>().Where(s => s.BeatmapHash == beatmapHash && !s.DeletePending), localScoresChanged);
    }

    private void localScoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        // This subscription may fire from changes to linked beatmaps, which we don't care about.
        // It's currently not possible for a score to be modified after insertion, so we can safely ignore callbacks with only modifications.
        if (IsDisposed || changes?.HasCollectionChanges() == false)
            return;

        var currentBeatmap = Beatmap;
        var currentUser = localUser.Value;
        var currentRuleset = ruleset?.Value;

        // A score write can notify this panel while its dependencies are being replaced during a screen transition.
        if (currentBeatmap == null || currentUser == null || currentRuleset == null)
            return;

        ScoreInfo? topScore = sender
            // doing these post realm filter is most efficient.
            .Where(s => s.UserID == currentUser.Id || s.UserID <= 1)
            .Where(s => currentRuleset.Equals(s.Ruleset))
            .MaxBy(info => (info.TotalScore, -info.Date.UtcDateTime.Ticks));

        if (selectedMods?.Value is { } currentMods && currentRuleset.ShortName == Constant.SHORT_NAME)
        {
            updateLamp(currentBeatmap, currentUser.Id, currentRuleset, currentMods);
        }
        else
            setRankFromScore(topScore);

        if (selectedMods?.Value is not { } || currentRuleset.ShortName != Constant.SHORT_NAME)
            updateLamp();
    }

    private void setRankFromScore(ScoreInfo? topScore)
    {
        updateable.Rank = topScore?.Rank;
        updateable.Alpha = topScore != null ? 1 : 0;
    }

    private void updateLamp()
    {
        if (IsDisposed)
            return;

        var currentBeatmap = Beatmap;
        var currentUser = localUser.Value;
        var currentRuleset = ruleset?.Value;
        var currentMods = selectedMods?.Value;

        if (currentRuleset == null || currentRuleset.ShortName != Constant.SHORT_NAME || currentBeatmap == null || currentUser == null || currentMods == null)
        {
            lampDisplay.Alpha = 0;
            return;
        }

        updateLamp(currentBeatmap, currentUser.Id, currentRuleset, currentMods);
    }

    private void updateLamp(BeatmapInfo beatmap, int userId, RulesetInfo rulesetInfo, IReadOnlyList<Mod> mods)
    {
        mods = mods.ToArray();
        scoreQueryCancellation?.Cancel();
        var cancellation = scoreQueryCancellation = new CancellationTokenSource();
        var generation = ++scoreQueryGeneration;

        Task.Run(() => getLocalScores(beatmap, userId, rulesetInfo), cancellation.Token).ContinueWith(task => Scheduler.Add(() =>
        {
            if (generation != scoreQueryGeneration || !task.IsCompletedSuccessfully || cancellation.IsCancellationRequested)
                return;

            var selection = select(task.Result, mods);
            updateable.Rank = selection.Rank;
            updateable.Alpha = selection.Rank.HasValue ? 1 : 0;
            lampDisplay.Lamp = BmsLampCalculator.Calculate(selection.Score);
            lampDisplay.Alpha = 1;
        }), TaskScheduler.Default);
    }

    private ScoreInfo[] getLocalScores(BeatmapInfo beatmap, int userId, RulesetInfo rulesetInfo)
    {
        var beatmapHash = beatmap.Hash;

        return realm.Run(r => r.All<ScoreInfo>()
            .Where(s => s.BeatmapHash == beatmapHash && !s.DeletePending)
            .ToArray()
            .Where(s => s.UserID == userId || s.UserID <= 1)
            .Where(s => rulesetInfo.Equals(s.Ruleset))
            .Select(s => s.DeepClone())
            .ToArray());
    }

    private static (ScoreInfo? Score, ScoreRank? Rank) select(IEnumerable<ScoreInfo> scores, IReadOnlyList<Mod> selectedMods)
    {
        var scoreList = scores as ScoreInfo[] ?? scores.ToArray();
        var score = BmsLampScoreSelector.SelectBest(scoreList, selectedMods);
        var rank = scoreList
            .Where(s => BmsLampScoreSelector.MatchesSelectedMods(s, selectedMods))
            .Select(s => (ScoreRank?)s.Rank)
            .DefaultIfEmpty()
            .Max();

        return (score, rank);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        scoreSubscription?.Dispose();
        scoreQueryCancellation?.Cancel();
    }
}
