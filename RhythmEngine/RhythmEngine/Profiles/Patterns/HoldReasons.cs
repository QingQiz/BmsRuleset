using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Patterns;

/// <summary>Public hold transition reasons, allowing hosts to interpret snapshots without accessing hold state.</summary>
public static class HoldReasons
{
    public static readonly ReasonId RELEASE = new("Release");
    public static readonly ReasonId AUTOMATIC = new("Automatic");
    public static readonly ReasonId TIMEOUT = new("Timeout");
    public static readonly ReasonId DROP = new("Drop");
    public static readonly ReasonId MISSED_HEAD = new("MissedHead");
    public static readonly ReasonId HEAD = new("Head");

    /// <summary>The host ended the interaction; this is distinct from how its tail was judged.</summary>
    public static readonly ReasonId HOST_END = new("End");

    internal static ReasonId FromResolution(HoldResolution resolution) => resolution switch
    {
        HoldResolution.Release => RELEASE,
        HoldResolution.Automatic => AUTOMATIC,
        HoldResolution.Timeout => TIMEOUT,
        HoldResolution.Drop => DROP,
        HoldResolution.MissedHead => MISSED_HEAD,
        HoldResolution.Head => HEAD,
        _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
    };
}
