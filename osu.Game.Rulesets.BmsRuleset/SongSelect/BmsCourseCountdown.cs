using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.SongSelect;

internal readonly record struct BmsCourseCountdownState(int SecondsRemaining, bool PlayCue)
{
    internal bool HasExpired => SecondsRemaining == 0;
}

internal sealed class BmsCourseCountdown
{
    internal const int DURATION_SECONDS = 99;

    internal static readonly HashSet<int> CUE_SECONDS =
    [
        20, 15, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1,
    ];

    internal bool IsStarted { get; private set; }

    private readonly Func<DateTimeOffset> getUtcNow;
    private readonly HashSet<int> playedCues = [];
    private DateTimeOffset deadline;

    internal BmsCourseCountdown(Func<DateTimeOffset>? getUtcNow = null)
    {
        this.getUtcNow = getUtcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal void Start()
    {
        if (IsStarted)
            return;

        IsStarted = true;
        deadline = getUtcNow().AddSeconds(DURATION_SECONDS);
    }

    internal BmsCourseCountdownState Update()
    {
        if (!IsStarted)
            throw new InvalidOperationException("The BMS course countdown has not started.");

        var secondsRemaining = Math.Max(0, (int)Math.Ceiling((deadline - getUtcNow()).TotalSeconds));
        var playCue = CUE_SECONDS.Contains(secondsRemaining) && playedCues.Add(secondsRemaining);
        return new BmsCourseCountdownState(secondsRemaining, playCue);
    }
}
