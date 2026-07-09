using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Online.API.Requests;
using osu.Game.Online.Leaderboards;
using osu.Game.Scoring;
using osu.Game.Screens.Play.Leaderboards;
using Realms;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

public static class BmsLocalLeaderboardPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.LocalLeaderboard";

    private static readonly object install_lock = new();

    private static bool disabled;
    private static MethodInfo? currentCriteriaSetter;
    private static FieldInfo? scoresField;
    private static FieldInfo? localScoreSubscriptionField;
    private static FieldInfo? inFlightOnlineRequestField;
    private static PropertyInfo? realmProperty;

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            if (disabled)
                return;

            try
            {
                var target = AccessTools.Method(typeof(LeaderboardManager), nameof(LeaderboardManager.FetchWithCriteria), [typeof(LeaderboardCriteria), typeof(bool)]);
                var prefixMethod = AccessTools.Method(typeof(BmsLocalLeaderboardPatcher), nameof(prefix));

                currentCriteriaSetter = AccessTools.PropertySetter(typeof(LeaderboardManager), nameof(LeaderboardManager.CurrentCriteria));
                scoresField = AccessTools.Field(typeof(LeaderboardManager), "scores");
                localScoreSubscriptionField = AccessTools.Field(typeof(LeaderboardManager), "localScoreSubscription");
                inFlightOnlineRequestField = AccessTools.Field(typeof(LeaderboardManager), "inFlightOnlineRequest");
                realmProperty = AccessTools.Property(typeof(LeaderboardManager), "realm");

                var missingMembers = new[]
                {
                    (name: "LeaderboardManager.FetchWithCriteria", member: (MemberInfo?)target),
                    (name: "BmsLocalLeaderboardPatcher.prefix", member: prefixMethod),
                    (name: "LeaderboardManager.CurrentCriteria.set", member: currentCriteriaSetter),
                    (name: "LeaderboardManager.scores", member: scoresField),
                    (name: "LeaderboardManager.localScoreSubscription", member: localScoreSubscriptionField),
                    (name: "LeaderboardManager.inFlightOnlineRequest", member: inFlightOnlineRequestField),
                    (name: "LeaderboardManager.realm", member: realmProperty),
                }.Where(m => m.member == null).Select(m => m.name).ToArray();

                if (missingMembers.Length > 0)
                {
                    disable("BMS LocalLeaderboardPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers));
                    return;
                }

                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                disable("BMS LocalLeaderboardPatcher: Failed to install Harmony patch. BMS local leaderboards will use osu!'s beatmap ID lookup.", ex);
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool prefix(LeaderboardManager __instance, LeaderboardCriteria newCriteria, bool forceRefresh)
    {
        if (disabled || !isBmsLocalCriteria(newCriteria))
            return true;

        if (!ThreadSafety.IsUpdateThread)
            throw new InvalidOperationException($"{nameof(LeaderboardManager.FetchWithCriteria)} must be called from the update thread.");

        try
        {
            if (!forceRefresh && __instance.CurrentCriteria?.Equals(newCriteria) == true && __instance.Scores.Value?.FailState == null)
                return false;

            setCurrentCriteria(__instance, newCriteria);
            disposeLocalScoreSubscription(__instance);
            cancelInFlightOnlineRequest(__instance);

            getScoresBindable(__instance).Value = null;

            var beatmap = newCriteria.Beatmap!;
            var ruleset = newCriteria.Ruleset!;
            var realm = (RealmAccess)realmProperty!.GetValue(__instance)!;
            var subscription = realm.RegisterForNotifications(r =>
                r.All<ScoreInfo>().Filter($"{nameof(ScoreInfo.BeatmapHash)} == $0"
                                          + $" AND {nameof(ScoreInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $1"
                                          + $" AND {nameof(ScoreInfo.DeletePending)} == false",
                    beatmap.Hash, ruleset.ShortName), (sender, changes) => localScoresChanged(__instance, sender, changes));

            localScoreSubscriptionField!.SetValue(__instance, subscription);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "BMS LocalLeaderboardPatcher: Failed while fetching BMS local leaderboard scores.");
            return true;
        }
    }

    private static void localScoresChanged(LeaderboardManager manager, IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
    {
        if (changes?.HasCollectionChanges() == false)
            return;

        var criteria = manager.CurrentCriteria;

        if (!isBmsLocalCriteria(criteria))
            return;

        var beatmap = criteria!.Beatmap!;
        var ruleset = criteria.Ruleset!;

        var newScores = BmsLocalLeaderboardScoreSelector.SelectScores(
            sender.AsEnumerable(),
            beatmap.Hash,
            ruleset.ShortName,
            criteria.ExactMods,
            criteria.Sorting,
            beatmap);

        getScoresBindable(manager).Value = LeaderboardScores.Success(newScores, scoresRequested: newScores.Length, totalScores: newScores.Length, null);
    }

    private static bool isBmsLocalCriteria(LeaderboardCriteria? criteria) =>
        criteria?.Scope == BeatmapLeaderboardScope.Local
        && criteria.Beatmap != null
        && criteria.Ruleset?.ShortName == "bms";

    private static void setCurrentCriteria(LeaderboardManager manager, LeaderboardCriteria criteria)
        => currentCriteriaSetter!.Invoke(manager, [criteria]);

    private static Bindable<LeaderboardScores?> getScoresBindable(LeaderboardManager manager)
        => (Bindable<LeaderboardScores?>)scoresField!.GetValue(manager)!;

    private static void disposeLocalScoreSubscription(LeaderboardManager manager)
    {
        if (localScoreSubscriptionField!.GetValue(manager) is IDisposable subscription)
            subscription.Dispose();

        localScoreSubscriptionField.SetValue(manager, null);
    }

    private static void cancelInFlightOnlineRequest(LeaderboardManager manager)
    {
        if (inFlightOnlineRequestField!.GetValue(manager) is GetScoresRequest request)
            request.Cancel();

        inFlightOnlineRequestField.SetValue(manager, null);
    }

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            Logger.Log(message, level: LogLevel.Error);
        else
            Logger.Error(exception, message);
    }
}
