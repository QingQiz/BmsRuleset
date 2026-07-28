using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModPaused : Mod, IApplicableMod
{
    public override string Name => "Paused";

    public override string Acronym => "PA";

    public override IconUsage? Icon => OsuIcon.ModClassic;

    public override LocalisableString Description => BmsStrings.ModPaused;

    public override ModType Type => ModType.System;

    public override bool UserPlayable => false;

    public override bool ValidForMultiplayer => false;

    public override bool ValidForMultiplayerAsFreeMod => false;

    public static void ApplyToScore(ScoreInfo score)
    {
        if (score.Pauses.Count == 0 || score.Mods.Any(mod => mod is BmsModPaused))
            return;

        score.Mods = [.. score.Mods, new BmsModPaused()];
    }
}
