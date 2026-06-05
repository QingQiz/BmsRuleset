namespace osu.Game.Rulesets.BmsRuleset.DifficultyTable;

public record ImportOption(string Display, string Url)
{
    public override string ToString() => Display;
}
