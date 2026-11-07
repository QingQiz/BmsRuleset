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
    /// <summary>Fallback BAD window (ms) used when <see cref="HitWindows" /> is unavailable.</summary>
    public const double FALLBACK_BAD_WINDOW = 280;

    // Actual notes already reference their shared chart data; only standalone windows need configuration storage.
    private readonly WindowConfiguration? configuration = source == null
        ? new WindowConfiguration(layout, column, rank, judgementRate)
        : null;

    // HitWindows validates WindowFor() in its constructor, so this must run before the base constructor.
    private BmsJudgementWindowTable? table = source == null
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
        if (source == null)
            table = createTable(configuration!.Rank, configuration.Layout, configuration.Column, configuration.Rate);
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
        // Gameplay mods are selected after ApplyDefaults; framework consumers must observe the same table as the engine.
        var current = source != null ? BmsJudgementProfileProvider.GetTable(source, false) : table!;
        return result switch
        {
            HitResult.Perfect => current.FrameworkWindowFor(HitResult.Perfect),
            HitResult.Great => current.FrameworkWindowFor(HitResult.Great),
            HitResult.Good => current.FrameworkWindowFor(HitResult.Good),
            HitResult.Ok or HitResult.Meh or HitResult.Miss => current.SlowWindowFor(HitResult.Ok),
            _ => 0,
        };
    }

    private static BmsJudgementWindowTable createTable(int rank, BmsLayoutVariant layout, int column, double? judgementRate)
        => judgementRate is { } rate
            ? BmsJudgementProfileProvider.GetTable(layout, column, rate, false)
            : BmsJudgementProfileProvider.GetTable(layout, column, rank, false);

    private sealed record WindowConfiguration(BmsLayoutVariant Layout, int Column, int Rank, double? Rate);
}
