using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal static class BmsUnavailableTableBeatmapPlayHandler
{
    internal static bool TryHandle(BmsSoloSongSelect songSelect, BeatmapInfo beatmap)
    {
        var resolved = UnavailableTableBeatmapFactory.Resolve(beatmap, BmsRulesetRuntime.DifficultyTableStore);
        if (resolved is not { } unavailable)
            return false;

        var url = UnavailableTableBeatmapFactory.GetDownloadUrl(unavailable.Entry);
        if (url != null)
            songSelect.Dependencies.Get<GameHost>().OpenUrlExternally(url);

        if (songSelect.Dependencies.TryGet<INotificationOverlay>(out var notifications))
            notifications.Post(new SimpleNotification
            {
                Text = url == null
                    ? BmsStrings.DifficultyTableBeatmapNotImported
                    : BmsStrings.DifficultyTableBeatmapDownloadPrompt(unavailable.Entry.Title ?? unavailable.Entry.Md5Hash),
            });

        return true;
    }

}
