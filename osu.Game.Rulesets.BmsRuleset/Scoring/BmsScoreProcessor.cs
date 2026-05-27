using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <summary>
///     BMS-native score processor.
/// </summary>
/// <remarks>
///     <para>
///         <b>EX-score</b>: The primary BMS scoring metric is EX-score = PGREAT×2 + GREAT×1.
///         The maximum EX-score for a chart with <i>N</i> notes is 2×N.
///         Accuracy reports <c>EXScore / MaxEXScore</c> (i.e. 1.0 = all PGREATs).
///         Total score is scaled to 0–1 000 000 using the EX-score ratio.
///     </para>
///     <para>
///         <b>Rank mapping</b>: BMS uses gauge-based clear/fail rather than osu!-style letter ranks.
///         We surface the following approximation using EX-score accuracy:
///         <list type="table">
///             <item><term>X (rainbow S)</term><description>100 % (all PGREAT)</description></item>
///             <item><term>S</term><description>≥ 2/3 accuracy (≥ AAA in BMS parlance)</description></item>
///             <item><term>A</term><description>≥ 8/9</description></item>
///             <item><term>B</term><description>≥ 7/9</description></item>
///             <item><term>C</term><description>≥ 6/9</description></item>
///             <item><term>D</term><description>anything below</description></item>
///         </list>
///         These thresholds mirror the traditional BMS DJ LEVEL scale (AAA = 8/9 → 2/3 accuracy).
///     </para>
/// </remarks>
public partial class BmsScoreProcessor() : ScoreProcessor(new BmsRuleset())
{
    // EX-score weights: PGREAT = 2, GREAT = 1, everything else = 0.
    public override int GetBaseScoreForResult(HitResult result) => result switch
    {
        HitResult.Perfect => 2,
        HitResult.Great => 1,
        _ => 0,
    };

    /// <summary>
    ///     Scaled EX-score.  Accuracy = EXScore / MaxEXScore (0–1).
    ///     Total score = accuracy × 1 000 000; bonus portion carries over from base.
    /// </summary>
    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion)
        => 1_000_000 * Accuracy.Value * accuracyProgress + bonusPortion;

    // EX-score has no combo multiplier — every PGREAT is always worth exactly 2.
    protected override double GetComboScoreChange(JudgementResult result) => 0;

    /// <summary>
    ///     BMS BAD (Ok) and POOR (Meh) must break combo, but osu!'s framework considers them
    ///     "hit" results (<c>HitResult.IsHit()</c> = <c>true</c>) so <c>IncreasesCombo()</c>
    ///     fires instead of <c>BreaksCombo()</c> in the sealed <c>ApplyResultInternal</c>.
    ///     We force-reset the combo here after the framework has already stamped it.
    /// </summary>
    /// <remarks>
    ///     Note: the stamped <c>result.ComboAfterJudgement</c> will reflect the pre-reset
    ///     (incremented) value because it is assigned before <c>ApplyScoreChange</c> is called.
    ///     This means revert arithmetic will be incorrect (combo would go negative).
    ///     BMS gameplay does not support rewind, so this is an accepted trade-off.
    /// </remarks>
    protected override void ApplyScoreChange(JudgementResult result)
    {
        if (result.Type is HitResult.Ok or HitResult.Meh)
            Combo.Value = 0;
    }

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
    {
        return accuracy switch
        {
            // All PGREATs → rainbow S (DJ LEVEL MAX / perfect full combo).
            >= 1.0 - 1e-9 when results.GetValueOrDefault(HitResult.Great) == 0 &&
                               results.GetValueOrDefault(HitResult.Good) == 0 &&
                               results.GetValueOrDefault(HitResult.Ok) == 0 &&
                               results.GetValueOrDefault(HitResult.Meh) == 0
                => ScoreRank.X,
            // Traditional BMS DJ LEVEL thresholds expressed as EX-score ratios.
            // AAA = 8/9 of max ≈ 0.889, AA = 7/9 ≈ 0.778, A = 6/9 ≈ 0.667.
            // We expose S/A/B/C/D as approximate equivalents.
            >= 8.0 / 9.0 => ScoreRank.S,
            >= 7.0 / 9.0 => ScoreRank.A,
            >= 6.0 / 9.0 => ScoreRank.B,
            >= 5.0 / 9.0 => ScoreRank.C,
            _ => ScoreRank.D,
        };

        // BMS pass/fail is determined solely by gauge at song end, not by score accuracy.
        // ScoreRank.F is never assigned here; failure is communicated through BmsHealthProcessor.
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap)
        => base.EnumerateHitObjects(beatmap).Order(JudgementOrderComparer.DEFAULT);

    /// <summary>
    ///     Records an Empty POOR: a keypress that found no note to consume.
    ///     Breaks combo and increments the Empty POOR counter stored under
    ///     <see cref="HitResult.Miss"/> in the score statistics so it
    ///     appears in the results-screen statistics and the live HUD judgement counter.
    ///     Empty POORs do not affect EX-score or accuracy.
    /// </summary>
    public void RegisterEmptyPoor()
    {
        Combo.Value = 0;
        ScoreResultCounts[HitResult.Miss] = ScoreResultCounts.GetValueOrDefault(HitResult.Miss) + 1;
    }

    private class JudgementOrderComparer : IComparer<HitObject>
    {
        public static readonly JudgementOrderComparer DEFAULT = new();

        public int Compare(HitObject? x, HitObject? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var result = x.GetEndTime().CompareTo(y.GetEndTime());
            if (result != 0)
                return result;

            // Native BMS should judge objects with identical end times in chart/lane order.
            if (x is BmsHitObject bx && y is BmsHitObject by)
                return bx.Column.CompareTo(by.Column);

            return 0;
        }
    }
}
