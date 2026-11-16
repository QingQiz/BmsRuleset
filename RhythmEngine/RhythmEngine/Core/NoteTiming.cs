using System.Collections.Immutable;

namespace RhythmEngine.Core;

/// <summary>
/// Static timing exposed to hosts for presentation and resource preparation. Runtime state and
/// input arbitration can narrow these ranges; they remain authoritative for actual judgements.
/// </summary>
public interface INoteTiming
{
    NoteTiming Timing { get; }
}

public readonly record struct NoteTiming(
    TimingWindows HeadWindows,
    long HeadDeadline,
    ImmutableHashSet<ResultId>? NonResolvingHeadResults = null,
    long TailDeadline = 0,
    long ResolvedHeadDeadline = 0,
    TimingWindows? TailWindows = null)
{
    public long EarliestInputOffset => HeadWindows.Entries.Min(window => window.Early);

    public long EarliestResolvingOffset
    {
        get
        {
            var nonResolving = NonResolvingHeadResults;
            return HeadWindows.Entries
                .Where(window => nonResolving?.Contains(window.Result) != true)
                .Min(window => window.Early);
        }
    }
}
