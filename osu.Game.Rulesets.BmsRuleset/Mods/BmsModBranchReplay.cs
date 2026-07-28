using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModBranchReplay : Mod, IApplicableToBeatmapConverter
{
    public override string Name => "BMS Branch Replay";

    public override string Acronym => "BR";

    public override IconUsage? Icon => OsuIcon.ModRepel;

    public override LocalisableString Description => BmsStrings.ModBranchReplay;

    public override ModType Type => ModType.System;

    public override bool UserPlayable => false;

    public override bool ValidForMultiplayer => false;

    public override bool ValidForMultiplayerAsFreeMod => false;

    public Bindable<string> Decisions { get; } = new(string.Empty);

    public void ApplyToBeatmapConverter(IBeatmapConverter beatmapConverter)
    {
        if (beatmapConverter is BmsBeatmapConverter bmsConverter)
            bmsConverter.BranchReplayDecisions = Decisions.Value;
    }
}
