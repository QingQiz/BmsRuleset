using System.Collections.Immutable;

namespace RhythmEngine.Profiles;

public enum EvidenceLevel
{
    PublicSource,
    CommunityCompatibility,
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
