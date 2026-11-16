using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Bms;

public class BmsGaugeCalculator
{
    public BmsGaugeProfile Profile { get; }

    public double Total { get; }

    public int NoteCount { get; }

    private readonly double limitIncrementScale;

    public BmsGaugeCalculator(
        BmsGaugeProfile profile,
        double? total,
        int noteCount,
        BmsGaugeProfileFamily profileFamily = BmsGaugeProfileFamily.SevenKeys,
        int? fullNoteCount = null)
    {
        Profile = profile;
        NoteCount = Math.Max(1, noteCount);

        var fullCount = Math.Max(1, fullNoteCount ?? NoteCount);
        Total = total ?? profile.DefaultTotal?.Invoke(fullCount) ?? CalculateDefaultTotal(fullCount, profileFamily);

        if (fullNoteCount.HasValue)
        {
            // Partial plays retain full-chart survival settings and proportional recovery TOTAL.
            if (profile.Algorithm == BmsGaugeAlgorithm.Total)
                Total = Total * NoteCount / fullCount;
            else
                NoteCount = fullCount;
        }

        var perNoteMaxPercent = Math.Max(Math.Min(0.15, (2 * Total - 320) / NoteCount), 0);
        limitIncrementScale = perNoteMaxPercent / 0.15;
    }

    public static double CalculateDefaultTotal(int noteCount, BmsGaugeProfileFamily profileFamily = BmsGaugeProfileFamily.SevenKeys)
    {
        noteCount = Math.Max(1, noteCount);

        return profileFamily == BmsGaugeProfileFamily.Keyboard
            ? Math.Max(300.0, 7.605 * (noteCount + 100) / (0.01 * noteCount + 6.5))
            : Math.Max(260.0, 7.605 * noteCount / (0.01 * noteCount + 6.5));
    }

    public double GetDeltaFor(ResultId result, double currentHealth)
    {
        var delta = result switch
        {
            var id when id == BmsResults.PGREAT => gain(Profile.PerfectGain),
            var id when id == BmsResults.GREAT => gain(Profile.GreatGain),
            var id when id == BmsResults.GOOD => gain(Profile.GoodGain),
            var id when id == BmsResults.BAD => Profile.BadDelta,
            var id when id == BmsResults.POOR => Profile.PoorDelta,
            var id when id == BmsResults.EMPTY_POOR => Profile.EmptyPoorDelta,
            _ => 0,
        };

        if (delta < 0 && Profile.Algorithm == BmsGaugeAlgorithm.ModifyDamage)
            delta *= Profile.DamageScale?.Invoke(Total, NoteCount) ?? CalculateDamageScale(Total, NoteCount);

        return applyGuts(delta, currentHealth);
    }

    public double ApplyDelta(double currentHealth, double delta)
    {
        if (Profile.IsFailed(currentHealth)) return 0;

        var health = Math.Clamp(currentHealth + delta, Profile.MinHealth, Profile.MaxHealth);
        return Profile.IsFailed(health) ? 0 : health;
    }

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

    internal static double CalculateDamageScale(double total, int noteCount)
    {
        var totalScale = total switch
        {
            >= 240 => 1,
            >= 230 => 1.11,
            >= 210 => 1.25,
            >= 200 => 1.5,
            >= 180 => 1.666,
            >= 160 => 2,
            >= 150 => 2.5,
            >= 130 => 3.333,
            >= 120 => 5,
            _ => 10,
        };

        var noteScale = 1.0;
        var note = 1000;
        var modifier = 0.002;

        while (note > noteCount || note > 1)
        {
            noteScale += modifier * (note - Math.Max(noteCount, note / 2));
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
