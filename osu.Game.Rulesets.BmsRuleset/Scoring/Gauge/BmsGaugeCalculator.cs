using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public class BmsGaugeCalculator
{
    public BmsGaugeProfile Profile { get; }

    public double Total { get; }

    public int NoteCount { get; }

    private readonly double limitIncrementScale;

    public BmsGaugeCalculator(BmsGaugeProfile profile, double total, int noteCount)
    {
        Profile = profile;
        NoteCount = Math.Max(1, noteCount);

        Total = total > 0
            ? total
            : CalculateDefaultTotal(NoteCount);

        var perNoteMaxPercent = Math.Max(Math.Min(0.15, (2 * Total - 320) / noteCount), 0);
        limitIncrementScale = perNoteMaxPercent / 0.15;
    }

    public static double CalculateDefaultTotal(int noteCount)
    {
        noteCount = Math.Max(1, noteCount);
        return Math.Max(7.605 * noteCount / (0.01 * noteCount + 6.5), 160.0);
    }

    public double GetDeltaFor(HitResult result, double currentHealth)
    {
        var delta = result switch
        {
            HitResult.Perfect => gain(Profile.PerfectGain),
            HitResult.Great => gain(Profile.GreatGain),
            HitResult.Good => gain(Profile.GoodGain),
            HitResult.Ok => Profile.BadDelta,
            HitResult.Meh => Profile.PoorDelta,
            HitResult.Miss => Profile.EmptyPoorDelta,
            _ => 0,
        };

        return applyGuts(delta, currentHealth);
    }

    public double ApplyDelta(double currentHealth, double delta)
        => Math.Clamp(currentHealth + delta, 0, Profile.MaxHealth);

    private double gain(double value)
    {
        return Profile.Algorithm switch
        {
            BmsGaugeAlgorithm.Total => Total / 100.0 / NoteCount * value,
            BmsGaugeAlgorithm.LimitIncrement => value * limitIncrementScale,
            BmsGaugeAlgorithm.Fixed => value,
            _ => throw new ArgumentOutOfRangeException(nameof(Profile.Algorithm), Profile.Algorithm, null),
        };
    }

    private double applyGuts(double delta, double currentHealth)
    {
        if (delta >= 0)
            return delta;

        foreach (var rule in Profile.GutsRules)
        {
            if (currentHealth <= rule.HealthThreshold)
                return delta * rule.DamageMultiplier;
        }

        return delta;
    }
}
