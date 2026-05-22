using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <summary>
///     Minimal native BMS hit windows.
/// </summary>
/// <remarks>
///     These values are deliberately conservative placeholders. They remove the dependency on
///     osu!mania while keeping judgement flow operational until #RANK / #EXRANK parsing can drive
///     BMS-specific timing windows from the chart itself.
/// </remarks>
public class BmsHitWindows : HitWindows
{
    private double perfect;
    private double great;
    private double good;
    private double ok;
    private double meh;
    private double miss;

    public override bool IsHitResultAllowed(HitResult result) => result switch
    {
        HitResult.Miss or HitResult.Meh or HitResult.Ok or HitResult.Good or HitResult.Great or HitResult.Perfect => true,
        _ => false,
    };

    public override void SetDifficulty(double difficulty)
    {
        // OD is kept in the formula only so current osu! difficulty data still has an effect.
        // Native BMS judgement should eventually use #RANK and #EXRANK instead.
        perfect = difficultyRange(difficulty, 22.4, 19.4, 13.9);
        great = difficultyRange(difficulty, 64, 49, 34);
        good = difficultyRange(difficulty, 97, 82, 67);
        ok = difficultyRange(difficulty, 127, 112, 97);
        meh = difficultyRange(difficulty, 151, 136, 121);
        miss = 188;
    }

    public override double WindowFor(HitResult result) => result switch
    {
        HitResult.Perfect => perfect,
        HitResult.Great => great,
        HitResult.Good => good,
        HitResult.Ok => ok,
        HitResult.Meh => meh,
        HitResult.Miss => miss,
        _ => 0,
    };

    private static double difficultyRange(double difficulty, double min, double mid, double max)
    {
        if (difficulty > 5)
            return mid + (max - mid) * (difficulty - 5) / 5;

        if (difficulty < 5)
            return mid - (mid - min) * (5 - difficulty) / 5;

        return mid;
    }
}
