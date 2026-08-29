using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.BmsRuleset.Course;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect.Course;

internal partial class BmsCourseStageScoreDisplay : CompositeDrawable
{
    private readonly BmsLampDisplay lamp;
    private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();
    private BmsGroupedCourseStage? stage;
    private BmsCourseResultStore? resultStore;

    [Resolved]
    private IBindable<RulesetInfo> ruleset { get; set; } = null!;

    [Resolved]
    private RealmAccess realm { get; set; } = null!;

    [Resolved]
    private IBindable<IReadOnlyList<Mod>> selectedMods { get; set; } = null!;

    private readonly UpdateableRank rank;
    private IDisposable? scoreSubscription;

    internal bool HasRank => rank.Rank != null;

    internal BmsGroupedCourseStage? Stage
    {
        set
        {
            if (Equals(stage, value))
                return;

            stage = value;

            if (IsLoaded)
                updateSubscription();
        }
    }

    internal BmsCourseStageScoreDisplay(BmsLampDisplay lamp)
    {
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

        BmsRulesetRuntime.CourseResultsChanged += resultStoreChanged;
        ruleset.BindValueChanged(_ => updateSubscription());
        selectedMods.BindValueChanged(_ => updateSubscription());
        localUser.BindValueChanged(_ => updateSubscription(), true);
    }

    private void updateSubscription()
    {
        scoreSubscription?.Dispose();
        scoreSubscription = null;
        updateScores([]);
        updateResultStore();

        if (stage?.Beatmap == null)
            return;

        var targetStage = stage;
        scoreSubscription = realm.RegisterForNotifications(
            r => r.All<ScoreInfo>().Where(score => score.BeatmapHash == targetStage.Beatmap.Hash && !score.DeletePending),
            (scores, changes) => localScoresChanged(targetStage, scores, changes));
    }

    private void localScoresChanged(BmsGroupedCourseStage targetStage, IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        if (!Equals(stage, targetStage))
            return;

        // Linked beatmap updates do not change the result and can produce notification-only refreshes.
        if (changes?.HasCollectionChanges() == false)
            return;

        var localScores = sender
            .Where(score => score.UserID == localUser.Value.Id || score.UserID <= 1)
            .Where(score => ruleset.Value.Equals(score.Ruleset))
            .ToArray();

        var courseScores = BmsCourseStageScoreSelector.Select(
            resultStore?.GetHistory(targetStage.Course.Id) ?? [],
            targetStage.StageIndex,
            targetStage.Beatmap!.Hash,
            localScores);
        updateScores(courseScores);
    }

    private void updateScores(IReadOnlyList<ScoreInfo> scores)
    {
        var highestScore = scores.MaxBy(score => (score.TotalScore, -score.Date.UtcDateTime.Ticks));
        rank.Rank = highestScore?.Rank;
        rank.Alpha = highestScore != null ? 1 : 0;

        lamp.Lamp = BmsLampCalculator.Calculate(BmsScoreSelector.SelectBest(scores, selectedMods.Value));
    }

    protected override void Dispose(bool isDisposing)
    {
        BmsRulesetRuntime.CourseResultsChanged -= resultStoreChanged;
        if (resultStore != null)
            resultStore.Changed -= courseResultChanged;

        scoreSubscription?.Dispose();
        base.Dispose(isDisposing);
    }

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

    private void courseResultChanged(string courseId)
    {
        if (stage?.Course.Id == courseId)
            Scheduler.Add(updateSubscription);
    }

    private void resultStoreChanged() => Scheduler.Add(updateSubscription);
}
