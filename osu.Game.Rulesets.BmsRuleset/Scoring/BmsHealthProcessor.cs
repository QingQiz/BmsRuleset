using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

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

    /// <summary>
    /// Whether HP ever dropped to 0 during this play.
    /// to determine gauge-failed rank even when NF mod prevents mid-song failure.
    /// </summary>
    public bool HasEverFailed { get; private set; }

    private const double max_landmine_damage_percent = (36 * 36 - 1) / 2d;

    private BmsGaugeCalculator? calculator;

    public BmsGaugeType GaugeType { get; private set; } = BmsGaugeType.Normal;

    public BmsGaugeProfile GaugeProfile { get; private set; } = BmsGaugeProfileFactory.Create(BmsGaugeType.Normal);

    public Bindable<BmsGaugeDisplayProfile> DisplayProfile { get; } =
        new(BmsGaugeProfileFactory.Create(BmsGaugeType.Normal).Display);

    private IBeatmap? beatmap;
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
        Health.Value = calculator!.ApplyDelta(Health.Value, calculator.GetDeltaFor(HitResult.Miss, Health.Value));
        markEverFailedIfEmpty();
    }

    public void SetGaugeType(BmsGaugeType gaugeType)
    {
        GaugeType = gaugeType;
        GaugeProfile = BmsGaugeProfileFactory.Create(gaugeType);
        DisplayProfile.Value = GaugeProfile.Display;
        Health.MaxValue = GaugeProfile.MaxHealth;
        Health.Value = GaugeProfile.InitialHealth;
        initialized = false;
    }

    public bool HasPassedAtEnd()
    {
        if (HasEverFailed)
            return false;

        return GaugeProfile.ClearThreshold <= 0 || Health.Value >= GaugeProfile.ClearThreshold;
    }

    private void markEverFailedIfEmpty()
    {
        if (Health.Value > 0)
            return;

        HasEverFailed = true;
        TriggerFailure();
    }

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        initialized = false;
        Health.MaxValue = GaugeProfile.MaxHealth;
        Health.Value = GaugeProfile.InitialHealth;
        HasEverFailed = false;
    }

    protected override void ApplyResultInternal(JudgementResult result)
    {
        base.ApplyResultInternal(result);

        if (!HasEverFailed && Health.Value <= 0)
            HasEverFailed = true;
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

        return calculator!.GetDeltaFor(result.Type, Health.Value);
    }

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

        calculator = new BmsGaugeCalculator(GaugeProfile, total, noteCount);
    }
}
