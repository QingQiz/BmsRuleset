using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModAutoScratch : Mod
{
    public override string Name => "Auto Scratch";

    public override string Acronym => "AS";

    public override LocalisableString Description => "Automatically hits scratch notes.";

    public override ModType Type => ModType.DifficultyReduction;

    public override double ScoreMultiplier => 1;

    [SettingSource("Hide scratch", "Hides the scratch column while auto-scratching")]
    public Bindable<bool> HideScratch { get; } = new();
}
