using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Allocation;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.BmsRuleset.Editor;

public static class BmsEditorPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.Editor";

    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(EditorLoader), "pushEditor");
            var prefixMethod = AccessTools.Method(typeof(BmsEditorPatcher), nameof(prefix));

            var missingMembers = new[]
            {
                (name: "EditorLoader.pushEditor", member: target),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                Logger.Log(
                    "BMS EditorPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. BMS editor entry will not be disabled.",
                    level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "BMS EditorPatcher: Failed to install Harmony patch. BMS editor entry will not be disabled.");
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool prefix(EditorLoader __instance)
    {
        if (__instance.Beatmap.Value.BeatmapInfo.Ruleset.ShortName != "bms")
            return true;

        PostEditorUnavailableNotification(__instance.Dependencies);
        __instance.ValidForResume = false;
        __instance.Exit();
        return false;
    }

    internal static void PostEditorUnavailableNotification(IReadOnlyDependencyContainer dependencies)
    {
        if (!dependencies.TryGet<INotificationOverlay>(out var notifications))
            return;

        notifications.Post(new SimpleNotification
        {
            Text = "The BMS editor is not supported yet.",
        });
    }
}
