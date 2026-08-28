using System;
using HarmonyLib;
using osu.Framework.Screens;
using osu.Game.Screens.Menu;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal static class BmsSongSelectEntryPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.SongSelectEntry";

    internal static bool IsInstalled { get; private set; }

    internal static bool ShouldReplace(RulesetInfo? ruleset) => ruleset?.ShortName == Constant.SHORT_NAME;

    internal static void InstallOnce()
    {
        if (IsInstalled)
            return;

        try
        {
            var target = AccessTools.Method(typeof(MainMenu), "loadSongSelect");
            var prefix = AccessTools.Method(typeof(BmsSongSelectEntryPatcher), nameof(loadSongSelectPrefix));

            if (target == null || prefix == null)
            {
                BmsLogger.Log("BMS song-select entry patch cannot be installed because MainMenu.loadSongSelect is unavailable.");
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix));
            IsInstalled = true;
        }
        catch (Exception exception)
        {
            BmsLogger.Error(exception, "Failed to install BMS song-select entry patch.");
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool loadSongSelectPrefix(MainMenu __instance)
    {
        if (!ShouldReplace(__instance.Ruleset.Value))
            return true;

        __instance.Push(new BmsSoloSongSelect());
        return false;
    }
    // ReSharper restore InconsistentNaming
}
