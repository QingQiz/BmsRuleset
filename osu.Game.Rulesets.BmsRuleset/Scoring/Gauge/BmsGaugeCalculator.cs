using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public class BmsGaugeCalculator
{
    public BmsGaugeProfile Profile { get; }

    public double Total { get; }

    public int NoteCount { get; }

    private readonly double limitIncrementScale;

    public BmsGaugeCalculator(
        BmsGaugeProfile profile,
        double total,
        int noteCount,
        BmsGaugeProfileFamily profileFamily = BmsGaugeProfileFamily.SevenKeys)
    {
        Profile = profile;
        NoteCount = Math.Max(1, noteCount);

        Total = total > 0
            ? total
            : CalculateDefaultTotal(NoteCount, profileFamily);

        var perNoteMaxPercent = Math.Max(Math.Min(0.15, (2 * Total - 320) / noteCount), 0);
        limitIncrementScale = perNoteMaxPercent / 0.15;
    }

    public static double CalculateDefaultTotal(int noteCount, BmsGaugeProfileFamily profileFamily = BmsGaugeProfileFamily.SevenKeys)
    {
        noteCount = Math.Max(1, noteCount);

        return profileFamily == BmsGaugeProfileFamily.Keyboard
            ? Math.Max(300.0, 7.605 * (noteCount + 100) / (0.01 * noteCount + 6.5))
            : Math.Max(260.0, 7.605 * noteCount / (0.01 * noteCount + 6.5));
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

        if (delta < 0 && Profile.Algorithm == BmsGaugeAlgorithm.ModifyDamage)
            delta *= calculateDamageScale();

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
            BmsGaugeAlgorithm.ModifyDamage => value,
            BmsGaugeAlgorithm.Fixed => value,
            _ => throw new ArgumentOutOfRangeException(nameof(Profile.Algorithm), Profile.Algorithm, null),
        };
    }

    private double calculateDamageScale()
    {
        var totalScale = Total switch
        {
            >= 240 => 1,
            >= 230 => 1.11,
            >= 210 => 1.25,
            >= 200 => 1.5,
            >= 180 => 1.666,
            >= 160 => 2,
            >= 150 => 2.5,
            >= 130 => 33.33,
            >= 120 => 5,
            _ => 10,
        };

        var noteScale = 1.0;
        var note = 1000;
        var modifier = 0.002;

        while (note > NoteCount || note > 1)
        {
            noteScale += modifier * (note - Math.Max(NoteCount, note / 2));
            note /= 2;
            modifier *= 2;
        }

        return Math.Max(totalScale, noteScale);
    }

    private double applyGuts(double delta, double currentHealth)
    {
        if (delta >= 0)
            return delta;

        foreach (var rule in Profile.GutsRules)
        {
            if (currentHealth < rule.HealthThreshold)
                return delta * rule.DamageMultiplier;
        }

        return delta;
    }
}
