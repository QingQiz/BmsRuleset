namespace osu.Game.Rulesets.BmsRuleset.Gameplay.DifficultyTable;

public record ImportOption(string Display, string Url)
{
    public override string ToString() => Display;
}
