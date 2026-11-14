namespace RhythmEngine.Core;

/// <summary>A profile-owned result identity. Equality uses the name, never its grade or score.</summary>
/// <remarks>Declare reusable IDs in the owning profile or rule; strings enter only at explicit definition or import boundaries.</remarks>
public sealed record ResultId
{
    public string Value { get; }

    public ResultId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}
