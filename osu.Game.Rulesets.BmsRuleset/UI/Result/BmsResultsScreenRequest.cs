using System;
using osu.Game.Screens.Ranking;

namespace osu.Game.Rulesets.BmsRuleset.UI.Result;

// Player.CreateResults requires the native type. This request is replaced before any native UI loads.
internal sealed partial class BmsResultsScreenRequest : ResultsScreen
{
    internal BmsResultsScreen Screen { get; }

    internal BmsResultsScreenRequest(BmsResultsScreen screen)
        : base(screen.Score)
    {
        if (!BmsResultsScreenEntryPatcher.IsInstalled)
            throw new InvalidOperationException("BMS gameplay results require the results screen entry patch.");

        Screen = screen;
    }
}
