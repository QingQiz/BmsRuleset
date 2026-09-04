using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking.Statistics;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public static class BmsReplayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.Replay";
    private static bool disabled;

    private static PropertyInfo? playerScoreManagerProperty;
    private static PropertyInfo? modelManagerRealmProperty;
    private static FieldInfo? scoreImporterFilesField;
    private static FieldInfo? replayFailIndicatorTrackField;
    private static FieldInfo? replayFailIndicatorFailSampleField;
    private static FieldInfo? mainMenuLogoProxyField;
    private static MethodInfo? drawableScheduleMethod;
    private static readonly ConditionalWeakTable<ScoreInfo, RestoreTask> pending_score_restores = new();
    private static readonly ConditionalWeakTable<ScoreInfo, RestoreTask> completed_score_restores = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        if (IsInstalled || disabled)
            return;

        try
        {
            var importScoreTarget = AccessTools.Method(typeof(Player), "ImportScore", [typeof(Score)]);
            var importScorePostfixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(importScorePostfix));
            var scoreDeepCloneTarget = AccessTools.Method(typeof(Score), nameof(Score.DeepClone));
            var scoreDeepClonePostfixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(scoreDeepClonePostfix));
            var getScoreTarget = AccessTools.Method(typeof(ScoreImporter), nameof(ScoreImporter.GetScore), [typeof(ScoreInfo)]);
            var getScorePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(getScorePrefix));
            var statisticsPanelPopulateTarget = AccessTools.Method(typeof(StatisticsPanel), "populateStatistics", [typeof(ValueChangedEvent<ScoreInfo?>)]);
            var statisticsPanelPopulatePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(statisticsPanelPopulatePrefix));
            var replayFailIndicatorDisposeTarget = AccessTools.Method(typeof(ReplayFailIndicator), "Dispose", [typeof(bool)]);
            var replayFailIndicatorDisposePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(replayFailIndicatorDisposePrefix));
            var mainMenuLogoArrivingTarget = AccessTools.Method(typeof(MainMenu), "LogoArriving", [typeof(OsuLogo), typeof(bool)]);
            var mainMenuLogoArrivingPrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(mainMenuLogoArrivingPrefix));
            var performFromScreenTarget = AccessTools.Method(typeof(OsuGame), nameof(OsuGame.PerformFromScreen), [typeof(Action<IScreen>), typeof(IEnumerable<Type>)]);
            var performFromScreenPrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(performFromScreenPrefix));

            playerScoreManagerProperty = AccessTools.Property(typeof(Player), "scoreManager");
            modelManagerRealmProperty = AccessTools.Property(typeof(ModelManager<ScoreInfo>), "Realm");
            scoreImporterFilesField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Files");
            replayFailIndicatorTrackField = AccessTools.Field(typeof(ReplayFailIndicator), "track");
            replayFailIndicatorFailSampleField = AccessTools.Field(typeof(ReplayFailIndicator), "failSample");
            mainMenuLogoProxyField = AccessTools.Field(typeof(MainMenu), "logoProxy");
            drawableScheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]);

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "Player.ImportScore", member: importScoreTarget),
                (name: "BmsReplayPatcher.importScorePostfix", member: importScorePostfixMethod),
                (name: "Score.DeepClone", member: scoreDeepCloneTarget),
                (name: "BmsReplayPatcher.scoreDeepClonePostfix", member: scoreDeepClonePostfixMethod),
                (name: "ScoreImporter.GetScore", member: getScoreTarget),
                (name: "BmsReplayPatcher.getScorePrefix", member: getScorePrefixMethod),
                (name: "StatisticsPanel.populateStatistics", member: statisticsPanelPopulateTarget),
                (name: "BmsReplayPatcher.statisticsPanelPopulatePrefix", member: statisticsPanelPopulatePrefixMethod),
                (name: "ReplayFailIndicator.Dispose", member: replayFailIndicatorDisposeTarget),
                (name: "BmsReplayPatcher.replayFailIndicatorDisposePrefix", member: replayFailIndicatorDisposePrefixMethod),
                (name: "MainMenu.LogoArriving", member: mainMenuLogoArrivingTarget),
                (name: "BmsReplayPatcher.mainMenuLogoArrivingPrefix", member: mainMenuLogoArrivingPrefixMethod),
                (name: "OsuGame.PerformFromScreen", member: performFromScreenTarget),
                (name: "BmsReplayPatcher.performFromScreenPrefix", member: performFromScreenPrefixMethod),
                (name: "Player.scoreManager", member: playerScoreManagerProperty),
                (name: "ModelManager<ScoreInfo>.Realm", member: modelManagerRealmProperty),
                (name: "RealmArchiveModelImporter<ScoreInfo>.Files", member: scoreImporterFilesField),
                (name: "ReplayFailIndicator.track", member: replayFailIndicatorTrackField),
                (name: "ReplayFailIndicator.failSample", member: replayFailIndicatorFailSampleField),
                (name: "MainMenu.logoProxy", member: mainMenuLogoProxyField),
                (name: "Drawable.Schedule", member: drawableScheduleMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                disable("BMS replay patch cannot be installed. Missing: " + string.Join(", ", missingMembers) + ".");
                return;
            }

            var harmony = new Harmony(harmony_id);
            harmony.Patch(importScoreTarget, postfix: new HarmonyMethod(importScorePostfixMethod));
            harmony.Patch(scoreDeepCloneTarget, postfix: new HarmonyMethod(scoreDeepClonePostfixMethod));
            harmony.Patch(getScoreTarget, prefix: new HarmonyMethod(getScorePrefixMethod));
            harmony.Patch(statisticsPanelPopulateTarget, prefix: new HarmonyMethod(statisticsPanelPopulatePrefixMethod));
            harmony.Patch(replayFailIndicatorDisposeTarget, prefix: new HarmonyMethod(replayFailIndicatorDisposePrefixMethod));
            harmony.Patch(mainMenuLogoArrivingTarget, prefix: new HarmonyMethod(mainMenuLogoArrivingPrefixMethod));
            harmony.Patch(performFromScreenTarget, prefix: new HarmonyMethod(performFromScreenPrefixMethod));
            IsInstalled = true;
        }
        catch (Exception e)
        {
            disable("Failed to install the BMS replay patch.", e);
        }
    }

    private static async Task importScoreWithReplay(Player player, Score score, Task originalImport)
    {
        await originalImport.ConfigureAwait(false);

        if (score.Replay.Frames.Count == 0 || score.ScoreInfo.Files.Any(f => f.Filename == BmsReplayArchive.FILENAME))
            return;

        try
        {
            await scheduleOnPlayerUpdateThread(player, () => attachReplayToScore(player, score)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, "BMS replay patch failed to attach replay data to a local score.");
        }
    }

    private static void attachReplayToScore(Player player, Score score)
    {
        if (playerScoreManagerProperty?.GetValue(player) is not ScoreManager scoreManager)
            return;

        using var archive = BmsReplayArchive.Create(score, out var hash);
        using var stream = new MemoryStream(archive.Get(BmsReplayArchive.FILENAME));

        scoreManager.AddFile(score.ScoreInfo, stream, BmsReplayArchive.FILENAME);
        applyHash(scoreManager, score.ScoreInfo, hash);
        score.ScoreInfo.Hash = hash;
    }

    private static void applyHash(ScoreManager scoreManager, ScoreInfo scoreInfo, string hash)
    {
        if (modelManagerRealmProperty?.GetValue(scoreManager) is not RealmAccess realmAccess)
            return;

        realmAccess.Write(realm =>
        {
            var managed = realm.Find<ScoreInfo>(scoreInfo.ID);

            managed?.Hash = hash;
        });
    }

    private static Task scheduleOnPlayerUpdateThread(Player player, Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        drawableScheduleMethod!.Invoke(player,
        [
            () =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception e)
                {
                    completion.SetException(e);
                }
            },
        ]);

        return completion.Task;
    }

    // ReSharper disable InconsistentNaming
    private static void importScorePostfix(Player __instance, Score score, ref Task __result)
    {
        if (!isBmsScore(score.ScoreInfo))
            return;

        __result = importScoreWithReplay(__instance, score, __result);
    }

    private static void scoreDeepClonePostfix(Score __instance, Score __result)
    {
        if (!isBmsScore(__instance.ScoreInfo))
            return;

        // Player clones a completed score before the replay archive is created. These sidecars
        // carry data that ScoreInfo.DeepClone cannot know about, so keep them with that clone.
        if (BmsJudgementEventStore.TryGet(__instance.ScoreInfo, out var judgementEvents))
            BmsJudgementEventStore.Set(__result.ScoreInfo, judgementEvents);

        if (BmsScoreGaugeHistoryStore.TryGet(__instance.ScoreInfo, out var gaugeHistory))
            BmsScoreGaugeHistoryStore.Set(__result.ScoreInfo, gaugeHistory);
    }

    private static bool getScorePrefix(ScoreInfo score, ScoreImporter __instance, ref Score __result)
    {
        if (!isBmsScore(score) || score.Files.All(f => f.Filename != BmsReplayArchive.FILENAME))
            return true;

        if (scoreImporterFilesField?.GetValue(__instance) is not RealmFileStore files)
            return false;

        try
        {
            __result = BmsReplayArchive.ReadScore(score, files.Store);
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, "BMS replay patch failed to restore replay data from a local score.");
            __result = new Score { ScoreInfo = score };
        }

        return false;
    }

    private static void statisticsPanelPopulatePrefix(StatisticsPanel __instance, ValueChangedEvent<ScoreInfo?> score)
    {
        var scoreInfo = score.NewValue;

        if (scoreInfo == null || !isBmsScore(scoreInfo))
            return;

        var hasGaugeHistory = BmsScoreGaugeHistoryStore.TryGet(scoreInfo, out var gaugeHistory) && gaugeHistory.Count > 0;
        if (scoreInfo.HitEvents.Count > 0 && hasGaugeHistory)
            return;

        // CompositeDrawable.Dependencies is populated during InjectDependencies, which the framework
        // runs before the BackgroundDependencyLoader that first fires this callback. We read it here
        // directly rather than capturing it via a separate InjectDependencies patch — Harmony patches
        // on the base Drawable.InjectDependencies don't reliably fire for CompositeDrawable's sealed
        // override, since the override's `base.InjectDependencies()` call is JIT-inlined early.
        var dependencies = __instance.Dependencies;

        if (dependencies == null || !dependencies.TryGet<ScoreManager>(out var scoreManager))
            return;

        // Online leaderboard entries and scores loaded from older databases may not carry the
        // BMS sidecar data in memory. Reading the replay archive here would block the update
        // thread exactly while the results screen is appearing, so perform it in the background
        // and refresh the bindable once the data is available.
        lock (pending_score_restores)
        {
            if (completed_score_restores.TryGetValue(scoreInfo, out _))
                return;

            if (pending_score_restores.TryGetValue(scoreInfo, out _))
                return;

            pending_score_restores.Add(scoreInfo, new RestoreTask());
        }

        var restoreTask = Task.Run(() => readScoreData(scoreManager, scoreInfo));
        _ = restoreTask.ContinueWith(task =>
        {
            lock (pending_score_restores)
            {
                pending_score_restores.Remove(scoreInfo);
                if (task.IsCompletedSuccessfully && task.Result != null)
                    completed_score_restores.GetValue(scoreInfo, static _ => new RestoreTask());
            }

            if (!task.IsCompletedSuccessfully || task.Result == null)
                return;

            drawableScheduleMethod!.Invoke(__instance,
            [
                () =>
                {
                    if (__instance.Score.Value == scoreInfo)
                    {
                        applyScoreData(scoreInfo, task.Result);
                        __instance.Score.TriggerChange();
                    }
                },
            ]);
        }, TaskScheduler.Default);
    }

    internal static void RestoreScoreData(ScoreManager scoreManager, ScoreInfo scoreInfo)
    {
        if (!isBmsScore(scoreInfo))
            return;

        try
        {
            var data = readScoreData(scoreManager, scoreInfo);
            if (data == null)
                return;

            applyScoreData(scoreInfo, data);
            return;
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, "BMS replay patch failed to restore score data.");
        }

    }

    private static RestoredScoreData? readScoreData(ScoreManager scoreManager, ScoreInfo scoreInfo)
    {
        var scoreWithReplay = scoreManager.GetScore(scoreInfo);
        if (scoreWithReplay == null)
            return null;

        var hitEvents = scoreWithReplay.ScoreInfo.HitEvents.Count > 0 ? scoreWithReplay.ScoreInfo.HitEvents : null;
        var judgementEvents = BmsJudgementEventStore.TryGet(scoreWithReplay.ScoreInfo, out var restoredJudgementEvents) && restoredJudgementEvents.Count > 0
            ? restoredJudgementEvents
            : null;
        var gaugeHistory = BmsScoreGaugeHistoryStore.TryGet(scoreWithReplay.ScoreInfo, out var restoredGaugeHistory) && restoredGaugeHistory.Count > 0
            ? restoredGaugeHistory
            : null;

        return hitEvents == null && judgementEvents == null && gaugeHistory == null
            ? null
            : new RestoredScoreData(hitEvents, judgementEvents, gaugeHistory);
    }

    private static void applyScoreData(ScoreInfo scoreInfo, RestoredScoreData data)
    {
        if (data.HitEvents != null)
            scoreInfo.HitEvents = data.HitEvents;
        if (data.JudgementEvents != null)
            BmsJudgementEventStore.Set(scoreInfo, data.JudgementEvents);
        if (data.GaugeHistory != null)
            BmsScoreGaugeHistoryStore.Set(scoreInfo, data.GaugeHistory);
    }

    private sealed record RestoredScoreData(List<HitEvent>? HitEvents, IReadOnlyList<BmsJudgementEvent>? JudgementEvents, IReadOnlyList<BmsGaugeHistoryEvent>? GaugeHistory);

    private sealed class RestoreTask;

    private static void replayFailIndicatorDisposePrefix(ReplayFailIndicator __instance)
    {
        if (__instance.LoadState != LoadState.NotLoaded)
            return;

        replayFailIndicatorFailSampleField?.SetValue(__instance, new SkinnableSound());
        replayFailIndicatorTrackField?.SetValue(__instance, new TrackVirtual(0));
    }

    // Rapid screen changes can leave MainMenu's logo proxy pending when its arriving animation runs again.
    private static void mainMenuLogoArrivingPrefix(MainMenu __instance)
    {
        if (mainMenuLogoProxyField?.GetValue(__instance) is IDisposable proxy)
        {
            proxy.Dispose();
            mainMenuLogoProxyField.SetValue(__instance, null);
        }
    }
    // ReSharper restore InconsistentNaming

    private static void performFromScreenPrefix(ref IEnumerable<Type> validScreens)
    {
        validScreens = AddBmsSongSelect(validScreens);
    }

    internal static IEnumerable<Type> AddBmsSongSelect(IEnumerable<Type>? validScreens)
    {
        var screens = validScreens?.ToArray() ?? Array.Empty<Type>();

        return screens.Contains(typeof(osu.Game.Screens.Select.SongSelect)) && !screens.Contains(typeof(BmsSongSelect))
            ? screens.Append(typeof(BmsSongSelect))
            : screens;
    }

    private static bool isBmsScore(ScoreInfo score) => score.Ruleset.ShortName == Constant.SHORT_NAME;

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            BmsLogger.Log(message, LogLevel.Important);
        else
            BmsLogger.Error(exception, message);
    }
}
