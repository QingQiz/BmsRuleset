using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public class BmsHitWindows(int rank = 2, BmsLayoutVariant layout = BmsLayoutVariant.Bme7K, int column = 1, double? judgementRate = null) : HitWindows
{
    /// <summary>Fallback BAD window (ms) used when <see cref="HitWindows" /> is unavailable.</summary>
    public const double FALLBACK_BAD_WINDOW = 280;

    private readonly double judgementRate = resolveRate(rank, layout, judgementRate);
    private BmsJudgementWindowTable table = createTable(resolveRate(rank, layout, judgementRate), layout, column);

    public override bool IsHitResultAllowed(HitResult result) => result switch
    {
        HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss => true,
        _ => false,
    };

    public override void SetDifficulty(double difficulty)
    {
        table = createTable(judgementRate, layout, column);
    }

    /// <summary>
    /// Framework-facing symmetric window derived from the active beatoraja profile.
    /// <list type="bullet">
    ///     <item>For PGREAT / GREAT / GOOD: returns the tighter side of the asymmetric window.</item>
    ///     <item>For BAD / Meh / Miss: returns the slow side (the conservative bound used by
    ///     framework lifetime and HUD calculations).</item>
    /// </list>
    /// </summary>
    public override double WindowFor(HitResult result) => result switch
    {
        HitResult.Perfect => table.FrameworkWindowFor(HitResult.Perfect),
        HitResult.Great => table.FrameworkWindowFor(HitResult.Great),
        HitResult.Good => table.FrameworkWindowFor(HitResult.Good),
        HitResult.Ok => table.SlowWindowFor(HitResult.Ok),
        HitResult.Meh => table.SlowWindowFor(HitResult.Ok),
        HitResult.Miss => table.SlowWindowFor(HitResult.Ok),
        _ => 0,
    };

    private static BmsJudgementWindowTable createTable(double judgementRate, BmsLayoutVariant layout, int column)
        => BmsJudgementProfileProvider.GetTable(layout, column, judgementRate, tail: false);

    private static double resolveRate(int rank, BmsLayoutVariant layout, double? judgementRate) =>
        judgementRate ?? BmsJudgementProfileProvider.RateForRank(layout, rank);
}
