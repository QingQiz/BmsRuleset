using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal partial class BmsCourseStageScoreDisplay : CompositeDrawable
{
    private readonly BeatmapInfo beatmap;
    private readonly BmsLampDisplay lamp;
    private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

    private readonly UpdateableRank rank;
    private IDisposable? scoreSubscription;

    internal bool HasRank => rank.Rank != null;

    internal BmsCourseStageScoreDisplay(BeatmapInfo beatmap, BmsLampDisplay lamp)
    {
        this.beatmap = beatmap;
        this.lamp = lamp;
        AutoSizeAxes = Axes.Both;

        InternalChild = rank = new UpdateableRank(animate: false)
        {
            Size = new Vector2(40, 20),
            Alpha = 0,
        };
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
        selectedMods.BindValueChanged(_ => updateSubscription());
        localUser.BindValueChanged(_ => updateSubscription(), true);
    }

    private void updateSubscription()
    {
        scoreSubscription?.Dispose();
        updateScores([]);

        scoreSubscription = realm.RegisterForNotifications(
            r => r.All<ScoreInfo>().Where(score => score.BeatmapHash == beatmap.Hash && !score.DeletePending),
            localScoresChanged);
    }

    private void localScoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        // Linked beatmap updates do not change the result and can produce notification-only refreshes.
        if (changes?.HasCollectionChanges() == false)
            return;

        var localScores = sender
                          .Where(score => score.UserID == localUser.Value.Id || score.UserID <= 1)
                          .Where(score => ruleset.Value.Equals(score.Ruleset))
                          .ToArray();

        updateScores(localScores);
    }

    private void updateScores(IReadOnlyList<ScoreInfo> scores)
    {
        var highestScore = scores.MaxBy(score => (score.TotalScore, -score.Date.UtcDateTime.Ticks));
        rank.Rank = highestScore?.Rank;
        rank.Alpha = highestScore != null ? 1 : 0;

        lamp.Lamp = BmsLampCalculator.Calculate(BmsLampScoreSelector.SelectBest(scores, selectedMods.Value));
    }

    protected override void Dispose(bool isDisposing)
    {
        scoreSubscription?.Dispose();
        base.Dispose(isDisposing);
    }
}
