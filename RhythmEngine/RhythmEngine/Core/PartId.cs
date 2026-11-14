namespace RhythmEngine.Core;

/// <summary>A rule-owned part identity shared by emissions, scoring opportunities and snapshots.</summary>
/// <remarks>Declare reusable IDs in the owning profile or rule; strings enter only at explicit definition or import boundaries.</remarks>
public sealed record PartId
{
    public string Value { get; }

    public PartId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}
