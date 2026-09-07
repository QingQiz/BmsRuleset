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
using osuTK;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;

// Own the subscription: the native rank display would also scan scores on the update thread.
internal partial class BmsPanelLocalRankDisplay : CompositeDrawable
{
    private BeatmapInfo? beatmap;

    public BeatmapInfo? Beatmap
    {
        get => beatmap;
        set
        {
            if (beatmap?.Equals(value) == true)
                return;

            beatmap = value;
            if (IsLoaded)
                updateSubscription();
        }
    }

    public bool HasRank => updateable.Rank != null;

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
    {
        this.beatmap = beatmap;
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
        selectedMods?.BindValueChanged(_ => updateScores());
    }

    private void updateSubscription()
    {
        scoreSubscription?.Dispose();
        scoreSubscription = null;
        cancelScoreQuery();
        updateable.Rank = null;
        updateable.Alpha = 0;
        lampDisplay.Alpha = 0;

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

        updateScores();
    }

    private void updateScores()
    {
        cancelScoreQuery();
        if (IsDisposed)
            return;

        var currentBeatmap = Beatmap;
        var currentUser = localUser.Value;
        var currentRuleset = ruleset?.Value;
        var currentMods = selectedMods?.Value;

        if (currentRuleset == null || currentBeatmap == null || currentUser == null)
        {
            lampDisplay.Alpha = 0;
            return;
        }

        var beatmapHash = currentBeatmap.Hash;
        var userId = currentUser.Id;
        var rulesetShortName = currentRuleset.ShortName;
        var mods = currentMods?.Select(mod => mod.DeepClone()).ToArray();
        var cancellation = scoreQueryCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var generation = scoreQueryGeneration;

        Task.Run(() => realm.Run(r =>
        {
            var scores = r.All<ScoreInfo>()
                .Where(s => s.BeatmapHash == beatmapHash && !s.DeletePending)
                .AsEnumerable()
                .Where(s => s.UserID == userId || s.UserID <= 1)
                .Where(s => s.Ruleset.ShortName == rulesetShortName)
                .ToArray();

            if (rulesetShortName == Constant.SHORT_NAME && mods != null)
            {
                var selection = select(scores, mods);
                return (selection.Rank, Lamp: (BmsLamp?)BmsLampCalculator.Calculate(selection.Score));
            }

            return (scores.MaxBy(info => (info.TotalScore, -info.Date.UtcDateTime.Ticks))?.Rank, Lamp: null);
        }), token).ContinueWith(task => Scheduler.Add(() =>
        {
            if (IsDisposed || generation != scoreQueryGeneration || token.IsCancellationRequested)
                return;

            if (!task.IsCompletedSuccessfully)
            {
                if (task.Exception != null)
                    BmsLogger.Error(task.Exception, "Failed to load BMS local rank.");
                return;
            }

            var selection = task.Result;
            updateable.Rank = selection.Rank;
            updateable.Alpha = selection.Rank.HasValue ? 1 : 0;
            lampDisplay.Lamp = selection.Lamp ?? BmsLamp.NoPlay;
            lampDisplay.Alpha = selection.Lamp.HasValue ? 1 : 0;
        }), TaskScheduler.Default);
    }

    private void cancelScoreQuery()
    {
        scoreQueryGeneration++;
        scoreQueryCancellation?.Cancel();
        scoreQueryCancellation?.Dispose();
        scoreQueryCancellation = null;
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
        cancelScoreQuery();
    }
}
