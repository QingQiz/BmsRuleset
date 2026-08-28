using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Screens;
using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal static class BmsSongSelectEntryPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.BmsRuleset.SongSelectEntry";

    private static readonly ConditionalWeakTable<OsuScreen, object> tracked_song_selects = new();

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
            var postfix = AccessTools.Method(typeof(BmsSongSelectEntryPatcher), nameof(loadSongSelectPostfix));

            if (target == null || prefix == null || postfix == null)
            {
                BmsLogger.Log("BMS song-select entry patch cannot be installed because MainMenu.loadSongSelect is unavailable.");
                return;
            }

            new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
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

        var songSelect = new BmsSoloSongSelect();
        __instance.Push(songSelect);
        TrackRulesetChanges(songSelect);
        return false;
    }

    private static void loadSongSelectPostfix(MainMenu __instance)
    {
        if (__instance.GetChildScreen() is OsuScreen songSelect && isManagedSongSelect(songSelect))
            TrackRulesetChanges(songSelect);
    }
    // ReSharper restore InconsistentNaming

    internal static void TrackRulesetChanges(OsuScreen songSelect)
    {
        if (tracked_song_selects.TryGetValue(songSelect, out _))
            return;

        tracked_song_selects.Add(songSelect, new object());
        songSelect.Ruleset.BindValueChanged(change => replaceSongSelectIfRequired(songSelect, change.NewValue));
    }

    private static void replaceSongSelectIfRequired(OsuScreen songSelect, RulesetInfo ruleset)
    {
        if (!songSelect.IsCurrentScreen())
            return;

        if (!isManagedSongSelect(songSelect))
            return;

        var useBmsSongSelect = ShouldReplace(ruleset);

        if (useBmsSongSelect == songSelect is BmsSoloSongSelect)
            return;

        var parent = songSelect.GetParentScreen();

        if (parent == null)
            return;

        songSelect.Exit();

        if (!parent.IsCurrentScreen())
            return;

        OsuScreen replacement = useBmsSongSelect ? new BmsSoloSongSelect() : new SoloSongSelect();
        parent.Push(replacement);
        TrackRulesetChanges(replacement);
    }

    private static bool isManagedSongSelect(OsuScreen screen) =>
        screen is BmsSoloSongSelect || screen.GetType() == typeof(SoloSongSelect);
}
