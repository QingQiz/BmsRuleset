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
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;

public static class BmsReplayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.Replay";
    private static bool disabled;

    private static PropertyInfo? playerScoreManagerProperty;
    private static PropertyInfo? modelManagerRealmProperty;
    private static FieldInfo? scoreImporterFilesField;
    private static FieldInfo? scoreImporterBeatmapsField;
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
            var getScoreTarget = AccessTools.Method(typeof(ScoreImporter), nameof(ScoreImporter.GetScore), [typeof(ScoreInfo)]);
            var getScorePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(getScorePrefix));
            var getDatabasedScoreTarget = AccessTools.Method(typeof(ScoreManager), "getDatabasedScoreInfo", [typeof(IScoreInfo)]);
            var getDatabasedScorePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(getDatabasedScorePrefix));
            var replayFailIndicatorDisposeTarget = AccessTools.Method(typeof(ReplayFailIndicator), "Dispose", [typeof(bool)]);
            var replayFailIndicatorDisposePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(replayFailIndicatorDisposePrefix));
            var mainMenuLogoArrivingTarget = AccessTools.Method(typeof(MainMenu), "LogoArriving", [typeof(OsuLogo), typeof(bool)]);
            var mainMenuLogoArrivingPrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(mainMenuLogoArrivingPrefix));
            var performFromScreenTarget = AccessTools.Method(typeof(OsuGame), nameof(OsuGame.PerformFromScreen), [typeof(Action<IScreen>), typeof(IEnumerable<Type>)]);
            var performFromScreenPrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(performFromScreenPrefix));
            var spectatorHandleFrameTarget = AccessTools.Method(typeof(SpectatorClient), nameof(SpectatorClient.HandleFrame), [typeof(ReplayFrame)]);
            var spectatorHandleFramePrefixMethod = AccessTools.Method(typeof(BmsReplayPatcher), nameof(spectatorHandleFramePrefix));

            playerScoreManagerProperty = AccessTools.Property(typeof(Player), "scoreManager");
            modelManagerRealmProperty = AccessTools.Property(typeof(ModelManager<ScoreInfo>), "Realm");
            scoreImporterFilesField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Files");
            scoreImporterBeatmapsField = AccessTools.Field(typeof(ScoreImporter), "beatmaps");
            replayFailIndicatorTrackField = AccessTools.Field(typeof(ReplayFailIndicator), "track");
            replayFailIndicatorFailSampleField = AccessTools.Field(typeof(ReplayFailIndicator), "failSample");
            mainMenuLogoProxyField = AccessTools.Field(typeof(MainMenu), "logoProxy");

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "Player.ImportScore", member: importScoreTarget),
                (name: "BmsReplayPatcher.importScorePostfix", member: importScorePostfixMethod),
                (name: "ScoreImporter.GetScore", member: getScoreTarget),
                (name: "BmsReplayPatcher.getScorePrefix", member: getScorePrefixMethod),
                (name: "ScoreManager.getDatabasedScoreInfo", member: getDatabasedScoreTarget),
                (name: "BmsReplayPatcher.getDatabasedScorePrefix", member: getDatabasedScorePrefixMethod),
                (name: "ReplayFailIndicator.Dispose", member: replayFailIndicatorDisposeTarget),
                (name: "BmsReplayPatcher.replayFailIndicatorDisposePrefix", member: replayFailIndicatorDisposePrefixMethod),
                (name: "MainMenu.LogoArriving", member: mainMenuLogoArrivingTarget),
                (name: "BmsReplayPatcher.mainMenuLogoArrivingPrefix", member: mainMenuLogoArrivingPrefixMethod),
                (name: "OsuGame.PerformFromScreen", member: performFromScreenTarget),
                (name: "BmsReplayPatcher.performFromScreenPrefix", member: performFromScreenPrefixMethod),
                (name: "SpectatorClient.HandleFrame", member: spectatorHandleFrameTarget),
                (name: "BmsReplayPatcher.spectatorHandleFramePrefix", member: spectatorHandleFramePrefixMethod),
                (name: "Player.scoreManager", member: playerScoreManagerProperty),
                (name: "ModelManager<ScoreInfo>.Realm", member: modelManagerRealmProperty),
                (name: "RealmArchiveModelImporter<ScoreInfo>.Files", member: scoreImporterFilesField),
                (name: "ScoreImporter.beatmaps", member: scoreImporterBeatmapsField),
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
            harmony.Patch(getScoreTarget, prefix: new HarmonyMethod(getScorePrefixMethod));
            harmony.Patch(getDatabasedScoreTarget, prefix: new HarmonyMethod(getDatabasedScorePrefixMethod));
            harmony.Patch(replayFailIndicatorDisposeTarget, prefix: new HarmonyMethod(replayFailIndicatorDisposePrefixMethod));
            harmony.Patch(mainMenuLogoArrivingTarget, prefix: new HarmonyMethod(mainMenuLogoArrivingPrefixMethod));
            harmony.Patch(performFromScreenTarget, prefix: new HarmonyMethod(performFromScreenPrefixMethod));
            harmony.Patch(spectatorHandleFrameTarget, prefix: new HarmonyMethod(spectatorHandleFramePrefixMethod));
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
    private static bool spectatorHandleFramePrefix(ReplayFrame frame) =>
        // BMS frames cannot be uploaded through the official spectator protocol. Local recording
        // has already saved the frame; skipping here also avoids scheduling a log for every frame.
        frame is not BmsReplayFrame;

    private static void importScorePostfix(Player __instance, Score score, ref Task __result)
    {
        if (!isBmsScore(score.ScoreInfo))
            return;

        __result = importScoreWithReplay(__instance, score, __result);
    }

    private static bool getScorePrefix(ScoreInfo score, ScoreImporter __instance, ref Score __result)
    {
        if (!isBmsScore(score) || score.Files.All(f => f.Filename != BmsReplayArchive.FILENAME))
            return true;

        __result = null!;
        if (scoreImporterFilesField?.GetValue(__instance) is not RealmFileStore files)
            return false;

        try
        {
            if (score.BeatmapInfo == null)
            {
                // ScoreManager re-fetches metadata, so the leaderboard's temporary
                // beatmap link is lost when a chart has been removed and re-imported.
                var hash = score.BeatmapHash;
                if (!string.IsNullOrEmpty(hash) && scoreImporterBeatmapsField?.GetValue(__instance) is Func<BeatmapManager> beatmaps)
                    score.BeatmapInfo = beatmaps().QueryBeatmap(beatmap => beatmap.Hash == hash);

                if (score.BeatmapInfo == null)
                {
                    BmsLogger.Log($"The BMS replay cannot be played because its beatmap is missing (hash: {hash}).", LogLevel.Important);
                    return false;
                }
            }

            __result = BmsReplayArchive.ReadScore(score, files.Store);
        }
        catch (Exception e)
        {
            BmsLogger.Error(e, "BMS replay patch failed to restore replay data from a local score.");
            __result = null!;
        }

        return false;
    }

    private static bool getDatabasedScorePrefix(IScoreInfo originalScoreInfo, ScoreManager __instance, ref ScoreInfo __result)
    {
        if (originalScoreInfo is not ScoreInfo score || !isBmsScore(score) || score.IsManaged)
            return true;

        // BMS archives contain inputs only, so distinct scores can share the same
        // archive hash. Prefer the local identity before the upstream hash lookup.
        var id = score.ID;
        var stored = __instance.Query(candidate => candidate.ID == id);
        if (stored == null)
            return true;

        __result = stored;
        return false;
    }

    internal static Task<Score> LoadScoreAsync(ScoreManager scoreManager, ScoreInfo scoreInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => scoreManager.GetScore(scoreInfo) ?? new Score { ScoreInfo = scoreInfo }, cancellationToken);
    }

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

        if (screens.Contains(typeof(Screens.Select.SongSelect)) && !screens.Contains(typeof(BmsSongSelect)))
            return screens.Append(typeof(BmsSongSelect));

        // Skin-editor gameplay previews must retain the custom solo selection screen as their return target.
        if (screens.Contains(typeof(Screens.Select.SoloSongSelect)) && !screens.Contains(typeof(BmsSoloSongSelect)))
            return screens.Append(typeof(BmsSoloSongSelect));

        return screens;
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
