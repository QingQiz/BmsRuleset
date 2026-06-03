using System;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

// TODO empty health. see https://iidx.org/misc/iidx_lr2_beatoraja_diff

/// <summary>
///     BMS-native Normal gauge health processor.
/// </summary>
/// <remarks>
///     <para>
///         <b>Normal gauge</b> rules (LR2 reference implementation):
///         <list type="table">
///             <listheader><term>Judgement</term><description>Gauge delta</description></listheader>
///             <item><term>PGREAT</term><description>+(<c>#TOTAL</c> / N) %</description></item>
///             <item><term>GREAT</term><description>+(<c>#TOTAL</c> / N) %</description></item>
///             <item><term>GOOD</term><description>+(<c>#TOTAL</c> / N × 0.5) %</description></item>
///             <item><term>BAD</term><description>−4 %</description></item>
///             <item><term>POOR / MISS</term><description>−6 %</description></item>
///         </list>
///         Starting gauge: 20 %.
///         Clear condition: ≥ 80 % at song end.
///     </para>
///     <para>
///         When <c>#TOTAL</c> is absent or 0 the default formula
///         <c>max(7.605 × N / (0.01 × N + 6.5), 160)</c> is used (LR2 default).
///     </para>
/// </remarks>
public partial class BmsHealthProcessor : HealthProcessor
{
    private const double bad_delta = -0.04;
    private const double miss_delta = -0.06;
    private const double empty_poor_delta = -0.02;

    private const double initial_health = 0.2;

    private IBeatmap? beatmap;
    private double pgreatGain;
    private bool initialized;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        base.ApplyBeatmap(beatmap);
        this.beatmap = beatmap;
    }

    /// <summary>
    ///     Applies an Empty POOR gauge penalty directly — no note is consumed.
    /// </summary>
    public void RegisterEmptyPoor()
    {
        ensureInitialized();
        Health.Value = Math.Max(0, Health.Value + empty_poor_delta);
    }

    /// <summary>
    ///     BMS Normal gauge fail condition:
    ///     <list type="bullet">
    ///         <item>Fail immediately if health reaches 0.</item>
    ///         <item>Fail at song end if health is below 80 % (Normal gauge clear condition).</item>
    ///     </list>
    /// </summary>
    protected override bool CheckDefaultFailCondition(JudgementResult result)
    {
        if (Health.Value <= 0)
            return true;

        if (MaxHits > 0 && JudgedHits >= MaxHits && Health.Value < 0.8)
            return true;

        return false;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        initialized = false;
        Health.Value = initial_health;
    }

    protected override HitResult GetSimulatedHitResult(Judgement judgement) => judgement is BmsJudgement { IsMine: true }
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        ensureInitialized();

        if (result.HitObject is BmsHitObject { IsMine: true } mine)
        {
            if (result.Type != HitResult.Meh)
                return 0;

            if (mine.LandmineDamagePercent >= max_landmine_damage_percent)
                return -1;

            return -mine.LandmineDamagePercent / 100d;
        }

        return result.Type switch
        {
            HitResult.Perfect => pgreatGain,
            HitResult.Great => pgreatGain,
            HitResult.Good => pgreatGain * 0.5,
            HitResult.Ok => bad_delta,
            HitResult.Meh => miss_delta,
            _ => 0,
        };
    }

    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;

    private void ensureInitialized()
    {
        if (initialized) return;

        initialized = true;
        var noteCount = beatmap?.HitObjects.Count(h => h is not BmsHitObject { IsMine: true }) ?? 0;
        if (noteCount == 0) noteCount = 1;

        double total = 0;
        if (beatmap is BmsBeatmap bmsBeatmap)
            total = bmsBeatmap.Total;

        if (total <= 0)
        {
            total = Math.Max(7.605 * noteCount / (0.01 * noteCount + 6.5), 160.0);
        }

        pgreatGain = total / 100.0 / noteCount;
    }
}
