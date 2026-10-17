namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal readonly record struct BmsColumnVisualFrame(
    double ScrollPosition,
    double Scale,
    float HitTarget,
    float Height,
    double TimelineEndTime = double.PositiveInfinity)
{
    internal float YAt(double position) => -(HitTarget + (float)((position - ScrollPosition) * Scale));
}

internal readonly record struct BmsVisualLayout(float OriginY, float MinimumY, float MaximumY);
