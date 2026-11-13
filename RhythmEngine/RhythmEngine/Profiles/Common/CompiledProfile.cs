using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Common;

/// <summary>Identifies the evidence behind a profile, distinguishing original implementations from community compatibility models.</summary>
/// <remarks>Enum order is not a confidence ranking. ProfileDescriptor sources and limitations define the verified scope.</remarks>
public enum EvidenceLevel
{
    /// <summary>Based on the target project's public source at a pinned revision; this alone does not establish full client parity.</summary>
    PublicSource,

    /// <summary>Based on community reimplementations or compatibility references, without claiming equivalence to the original client.</summary>
    CommunityCompatibility,

    /// <summary>Based on analysis of a pinned client binary. The profile documents whether native instruction comparisons also exist and what they cover.</summary>
    StaticBinary,
}

public sealed record ProfileDescriptor(
    string Id,
    string Revision,
    EvidenceLevel Evidence,
    ImmutableArray<string> Sources,
    ImmutableArray<string> Limitations);

public sealed record CompiledProfile(Chart Chart, SessionConfiguration Configuration, ProfileDescriptor Descriptor)
{
    public JudgementSession CreateSession() => new(Chart, Configuration);
}
