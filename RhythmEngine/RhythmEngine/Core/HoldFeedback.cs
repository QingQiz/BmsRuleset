namespace RhythmEngine.Core;

[Flags]
public enum HoldFeedback
{
    None = 0,
    BrightBody = 1,
    PinHead = 2,
    AnimateBody = 4,
    HoldLight = 8,
    All = BrightBody | PinHead | AnimateBody | HoldLight,
}

/// <summary>Independent feedback channels before, during and after a hold interruption.</summary>
public readonly record struct HoldFeedbackPolicy(HoldFeedback Held, HoldFeedback Released, HoldFeedback Recovered)
{
    public HoldFeedback Resolve(bool held, bool interrupted) => !held ? Released : interrupted ? Recovered : Held;
}
