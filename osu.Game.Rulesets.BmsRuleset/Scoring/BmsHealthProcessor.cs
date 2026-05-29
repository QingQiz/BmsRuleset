using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
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
///             <item><term>GREAT</term><description>+(<c>#TOTAL</c> / N × 0.5) %</description></item>
///             <item><term>GOOD</term><description>+(<c>#TOTAL</c> / N × 0.2) %</description></item>
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
public partial class BmsHealthProcessor(double drainStartTime) : LegacyDrainingHealthProcessor(drainStartTime)
{
    // Gauge deltas as fractions of 1.0 (100%). see https://iidx.org/misc/iidx_lr2_beatoraja_diff
    private const double bad_delta = -0.04;  // 4%
    private const double miss_delta = -0.06; // 6%
    private const double empty_poor_delta = -0.02;

    // BMS Normal gauge starts at 20%.
    private const double initial_health = 0.2;

    // Per-note gain from PGREAT; computed on first use.
    private double pgreatGain;
    private bool initialized;

    /// <summary>
    ///     Applies an Empty POOR gauge penalty directly — no note is consumed.
    ///     Empty POOR arises when a key is pressed outside every note's Early POOR window,
    ///     so there is no judgement result to route through the normal pipeline.
    /// </summary>
    public void RegisterEmptyPoor()
    {
        ensureInitialized();
        Health.Value = Math.Max(0, Health.Value + empty_poor_delta);
    }

    protected override double ComputeDrainRate()
    {
        // BMS uses discrete hit/miss deltas only — no passive drain at all.
        // Do NOT call base.ComputeDrainRate(): it runs LegacyDrainingHealthProcessor's
        // while(true) convergence loop which diverges for BMS charts because our
        // pgreatGain per-note HP recovery is always less than the framework's
        // hpRecoveryAvailable threshold (computed from DrainRate), causing an infinite loop.
        return 0;
    }

    /// <summary>
    ///     BMS Normal gauge fail condition:
    ///     <list type="bullet">
    ///         <item>Fail immediately if health reaches 0 (same as Hazard semantics, preserves current base behaviour).</item>
    ///         <item>Fail at song end if health is below 80 % (Normal gauge clear condition).</item>
    ///     </list>
    /// </summary>
    protected override bool CheckDefaultFailCondition(JudgementResult result)
    {
        // Immediate fail at zero (same as LegacyDrainingHealthProcessor base).
        if (base.CheckDefaultFailCondition(result))
            return true;

        // Clear condition: after the last note, require ≥ 80 %.
        if (MaxHits > 0 && JudgedHits >= MaxHits && Health.Value < 0.8)
            return true;

        return false;
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        // Reset so gain is recalculated after beatmap assignment.
        initialized = false;
        // BMS Normal gauge starts at 20%, not 100%.
        Health.Value = initial_health;
    }

    protected override IEnumerable<HitObject> EnumerateTopLevelHitObjects() => Beatmap.HitObjects;

    protected override IEnumerable<HitObject> EnumerateNestedHitObjects(HitObject hitObject) => hitObject.NestedHitObjects;

    protected override HitResult GetSimulatedHitResult(Judgement judgement) => judgement is BmsJudgement { IsMine: true }
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    protected override double GetHealthIncreaseFor(HitObject hitObject, HitResult result)
    {
        ensureInitialized();

        if (hitObject is BmsHitObject { IsMine: true } mine)
        {
            if (result != HitResult.Meh)
                return 0;

            if (mine.LandmineDamagePercent >= max_landmine_damage_percent)
                return -1;

            return -mine.LandmineDamagePercent / 100d;
        }

        return result switch
        {
            HitResult.Perfect => pgreatGain,
            HitResult.Great => pgreatGain,      // GREAT
            HitResult.Good => pgreatGain * 0.5, // GOOD
            HitResult.Ok => bad_delta,          // BAD
            HitResult.Meh => miss_delta,        // POOR (passive miss or in-range-early-press)
            _ => 0,
        };
    }

    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;

    private void ensureInitialized()
    {
        if (initialized) return;

        initialized = true;
        var noteCount = Beatmap.HitObjects.Count(h => h is not BmsHitObject { IsMine: true });
        if (noteCount == 0) noteCount = 1; // Avoid division by zero.

        double total = 0;
        if (Beatmap is BmsBeatmap bmsBeatmap)
            total = bmsBeatmap.Total;

        if (total <= 0)
        {
            // LR2 default #TOTAL formula.
            total = Math.Max(7.605 * noteCount / (0.01 * noteCount + 6.5), 160.0);
        }

        // total is expressed as a percentage; convert to fraction then distribute across notes.
        pgreatGain = total / 100.0 / noteCount;
    }
}
