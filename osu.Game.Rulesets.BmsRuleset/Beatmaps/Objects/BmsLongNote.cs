using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Objects.Types;

namespace osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

public class BmsLongNote : BmsHitObject, IHasDuration
{

    public double EndTime => StartTime + Duration;

    public double Duration { get; set; }

    internal double VisualScrollPositionAtEndTime { get; set; } = double.NaN;

    /// <summary>
    ///     Precomputed scroll position at <see cref="EndTime"/>.
    ///     Computed once during beatmap loading.
    /// </summary>
    public double ScrollPositionAtEndTime { get; set; }

    public int? TailSampleKey { get; set; }

    public IReadOnlyList<int> AdditionalTailSampleKeys { get; set; } = [];

    internal IEnumerable<BmsSampleEvent> GetTailSamples()
    {
        if (TailSampleKey is { } key)
            yield return new BmsSampleEvent(EndTime, key, TailSampleVolume);
        foreach (var additional in AdditionalTailSampleKeys)
            yield return new BmsSampleEvent(EndTime, additional, TailSampleVolume);
    }

    public int TailSampleVolume { get; set; } = 100;

    /// <summary>
    ///     Creates a synthetic short-note endpoint for CN/HCN tail judgement
    ///     as a separate scoring event from the head judgement.
    /// </summary>
    public BmsHitObject CreateSyntheticEndpoint(double endpointTime)
    {
        var endpoint = new BmsNote();
        CopyTo(endpoint);

        endpoint.StartTime = endpointTime;

        return endpoint;
    }

    protected override void CopyTo(BmsHitObject target)
    {
        base.CopyTo(target);
        if (target is BmsLongNote ln)
        {
            ln.Duration = Duration;
            ln.ScrollPositionAtEndTime = ScrollPositionAtEndTime;
            ln.VisualScrollPositionAtEndTime = VisualScrollPositionAtEndTime;
            ln.TailSampleKey = TailSampleKey;
            ln.AdditionalTailSampleKeys = AdditionalTailSampleKeys;
            ln.TailSampleVolume = TailSampleVolume;
        }
    }
}

public enum BmsLongNoteMode
{
    Undefined = 0,
    LongNote = 1,
    ChargeNote = 2,
    HellChargeNote = 3,
}
