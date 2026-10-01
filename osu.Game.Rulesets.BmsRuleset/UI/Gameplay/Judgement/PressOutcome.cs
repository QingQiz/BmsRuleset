namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal enum PressOutcomeKind
{
    Hit,
    EmptyPoor,
    Empty,
}

internal readonly record struct PressOutcome(PressOutcomeKind Kind, double? ExpectedTime = null, int Column = 0)
{
    public static PressOutcome Hit => new(PressOutcomeKind.Hit);

    public static PressOutcome Empty => new(PressOutcomeKind.Empty);

    public static PressOutcome ForEmptyPoor(double expectedTime, int column) => new(PressOutcomeKind.EmptyPoor, expectedTime, column);
}
