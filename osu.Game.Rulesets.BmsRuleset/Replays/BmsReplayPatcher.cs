using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Audio.Track;
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

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "Player.ImportScore", member: importScoreTarget),
                (name: "BmsReplayPatcher.importScorePostfix", member: importScorePostfixMethod),
                (name: "Score.DeepClone", member: scoreDeepCloneTarget),
                (name: "BmsReplayPatcher.scoreDeepClonePostfix", member: scoreDeepClonePostfixMethod),
                (name: "ScoreImporter.GetScore", member: getScoreTarget),
                (name: "BmsReplayPatcher.getScorePrefix", member: getScorePrefixMethod),
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
            if (playerScoreManagerProperty?.GetValue(player) is ScoreManager scoreManager
                && modelManagerRealmProperty?.GetValue(scoreManager) is RealmAccess realmAccess)
                await AttachReplayAsync(scoreManager, realmAccess, score).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, "BMS replay patch failed to attach replay data to a local score.");
        }
    }

    internal static Task AttachReplayAsync(ScoreManager scoreManager, RealmAccess realmAccess, Score score) => Task.Run(() =>
    {
        using var archive = BmsReplayArchive.Create(score, out var hash);
        using var stream = new MemoryStream(archive.Get(BmsReplayArchive.FILENAME));

        // The detached-score AddFile overload requires the update realm. Use the transaction
        // overload so compression and disk writes cannot stall the transition to results.
        var replayFiles = realmAccess.Write(realm =>
        {
            var managed = realm.Find<ScoreInfo>(score.ScoreInfo.ID)
                          ?? throw new InvalidOperationException("The imported BMS score no longer exists.");
            scoreManager.AddFile(managed, stream, BmsReplayArchive.FILENAME, realm);
            managed.Hash = hash;
            return managed.Files.Detach().ToArray();
        });
        score.ScoreInfo.Files.Clear();
        foreach (var file in replayFiles)
            score.ScoreInfo.Files.Add(file);
        score.ScoreInfo.Hash = hash;
    });

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
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, "BMS replay patch failed to restore score data.");
        }
    }

    internal static async Task RestoreScoreDataAsync(ScoreManager scoreManager, ScoreInfo scoreInfo, CancellationToken cancellationToken)
    {
        if (!isBmsScore(scoreInfo)
            || (scoreInfo.HitEvents.Count > 0 && BmsScoreGaugeHistoryStore.TryGet(scoreInfo, out var history) && history.Count > 0))
            return;

        var data = await Task.Run(() => readScoreData(scoreManager, scoreInfo), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (data != null)
            applyScoreData(scoreInfo, data);
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
        var screens = validScreens?.ToArray() ?? [];

        return screens.Contains(typeof(Screens.Select.SongSelect)) && !screens.Contains(typeof(BmsSongSelect))
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
