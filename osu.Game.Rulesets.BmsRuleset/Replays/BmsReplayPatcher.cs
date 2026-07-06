using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.BmsRuleset.Replays;

public static class BmsReplayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.Replay";
    private const string ruleset_short_name = "bms";

    private static bool disabled;

    private static PropertyInfo? playerScoreManagerProperty;
    private static PropertyInfo? modelManagerRealmProperty;
    private static FieldInfo? scoreImporterFilesField;
    private static MethodInfo? drawableScheduleMethod;

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

            playerScoreManagerProperty = AccessTools.Property(typeof(Player), "scoreManager");
            modelManagerRealmProperty = AccessTools.Property(typeof(ModelManager<ScoreInfo>), "Realm");
            scoreImporterFilesField = AccessTools.Field(typeof(RealmArchiveModelImporter<ScoreInfo>), "Files");
            drawableScheduleMethod = AccessTools.Method(typeof(Drawable), "Schedule", [typeof(Action)]);

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "Player.ImportScore", member: importScoreTarget),
                (name: "BmsReplayPatcher.importScorePostfix", member: importScorePostfixMethod),
                (name: "ScoreImporter.GetScore", member: getScoreTarget),
                (name: "BmsReplayPatcher.getScorePrefix", member: getScorePrefixMethod),
                (name: "Player.scoreManager", member: playerScoreManagerProperty),
                (name: "ModelManager<ScoreInfo>.Realm", member: modelManagerRealmProperty),
                (name: "RealmArchiveModelImporter<ScoreInfo>.Files", member: scoreImporterFilesField),
                (name: "Drawable.Schedule", member: drawableScheduleMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                disable("BMS replay patch cannot be installed. Missing: " + string.Join(", ", missingMembers) + ".");
                return;
            }

            var harmony = new Harmony(harmony_id);
            harmony.Patch(importScoreTarget, postfix: new HarmonyMethod(importScorePostfixMethod));
            harmony.Patch(getScoreTarget, prefix: new HarmonyMethod(getScorePrefixMethod));
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
            Logger.Error(e, "BMS replay patch failed to attach replay data to a local score.");
        }
    }

    private static void attachReplayToScore(Player player, Score score)
    {
        if (playerScoreManagerProperty?.GetValue(player) is not ScoreManager scoreManager)
            return;

        using var archive = BmsReplayArchive.Create(score);
        using var stream = new MemoryStream(archive.Get(BmsReplayArchive.FILENAME));
        var hash = BmsReplayArchive.ComputeHash(score);

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
            Logger.Error(e, "BMS replay patch failed to restore replay data from a local score.");
            __result = new Score { ScoreInfo = score };
        }

        return false;
    }
    // ReSharper restore InconsistentNaming

    private static bool isBmsScore(ScoreInfo score) => score.Ruleset.ShortName == ruleset_short_name;

    private static void disable(string message, Exception? exception = null)
    {
        disabled = true;

        if (exception == null)
            Logger.Log(message, LoggingTarget.Runtime, LogLevel.Important);
        else
            Logger.Error(exception, message);
    }
}
