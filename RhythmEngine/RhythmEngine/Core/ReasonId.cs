namespace RhythmEngine.Core;

/// <summary>A rule-owned explanation for a state transition, independent of the core scheduler.</summary>
/// <remarks>Declare reusable IDs in the owning profile or rule; strings enter only at explicit definition or import boundaries.</remarks>
public sealed record ReasonId
{
    public string Value { get; }

    public ReasonId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}
