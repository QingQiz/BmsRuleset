using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;

public class BmsHitWindows(
    BmsHitObject? source,
    int rank = 2,
    BmsLayoutVariant layout = BmsLayoutVariant.Bme7K,
    int column = 1,
    double? judgementRate = null) : HitWindows
{
    // HitWindows validates WindowFor() in its constructor, so this must run before the base constructor.
    private readonly BmsTimingDefinition? table = source == null
        ? createTable(rank, layout, column, judgementRate)
        : null;

    public BmsHitWindows(int rank = 2, BmsLayoutVariant layout = BmsLayoutVariant.Bme7K, int column = 1, double? judgementRate = null)
        : this(null, rank, layout, column, judgementRate)
    {
    }

    public override bool IsHitResultAllowed(HitResult result) => result switch
    {
        HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss => true,
        _ => false,
    };

    public override void SetDifficulty(double difficulty)
    {
    }

    /// <summary>
    /// Framework-facing symmetric window derived from the active beatoraja profile.
    /// <list type="bullet">
    ///     <item>For PGREAT / GREAT / GOOD: returns the tighter side of the asymmetric window.</item>
    ///     <item>For BAD / Meh / Miss: returns the slow side (the conservative bound used by
    ///     framework lifetime and HUD calculations).</item>
    /// </list>
    /// </summary>
    public override double WindowFor(HitResult result)
    {
        var current = source != null ? BmsEngineRules.GetTiming(source, false) : table!;
        return result switch
        {
            HitResult.Perfect => current.FrameworkWindowFor(HitResult.Perfect),
            HitResult.Great => current.FrameworkWindowFor(HitResult.Great),
            HitResult.Good => current.FrameworkWindowFor(HitResult.Good),
            HitResult.Ok or HitResult.Meh or HitResult.Miss => current.PassiveDeadline / 1000.0,
            _ => 0,
        };
    }

    private static BmsTimingDefinition createTable(int rank, BmsLayoutVariant layout, int column, double? judgementRate)
        => judgementRate is { } rate
            ? BmsEngineRules.GetTiming(layout, column, rate, false)
            : BmsEngineRules.GetTiming(layout, column, rank, false);
}
