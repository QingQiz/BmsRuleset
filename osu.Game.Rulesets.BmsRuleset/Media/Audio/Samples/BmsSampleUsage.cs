namespace osu.Game.Rulesets.BmsRuleset.Media.Audio.Samples;

public readonly record struct BmsSampleUsage(
    ushort SampleKey,
    double Time,
    double? CandidateStartTime = null,
    double? CandidateEndTime = null)
{
    public double EarliestTriggerTime => CandidateStartTime ?? Time;

    public double LatestTriggerTime => CandidateEndTime ?? Time;
}
