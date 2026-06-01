using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModBranchReplay : Mod, IApplicableToBeatmapConverter
{
    public override string Name => "BMS Branch Replay";

    public override string Acronym => "BR";

    public override LocalisableString Description => "Stores BMS random/switch branch decisions for replay playback.";

    public override ModType Type => ModType.System;

    public override double ScoreMultiplier => 1;

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
