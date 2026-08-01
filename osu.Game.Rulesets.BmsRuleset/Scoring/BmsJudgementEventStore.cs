using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public static class BmsJudgementEventStore
{
    private static readonly ConditionalWeakTable<ScoreInfo, Holder> events = new();

    public static void Set(ScoreInfo score, IEnumerable<BmsJudgementEvent> judgementEvents)
    {
        var copy = judgementEvents.ToArray();
        set(score, copy);
    }

    internal static void SetView(ScoreInfo score, IReadOnlyList<BmsJudgementEvent> judgementEvents)
        => set(score, judgementEvents);

    private static void set(ScoreInfo score, IReadOnlyList<BmsJudgementEvent> judgementEvents)
    {
        lock (events)
        {
            if (events.TryGetValue(score, out var existing) && ReferenceEquals(existing.Events, judgementEvents))
                return;

            events.Remove(score);
            events.Add(score, new Holder(judgementEvents));
        }
    }

    public static bool TryGet(ScoreInfo score, out IReadOnlyList<BmsJudgementEvent> judgementEvents)
    {
        lock (events)
        {
            if (events.TryGetValue(score, out var holder))
            {
                judgementEvents = holder.Events;
                return true;
            }
        }

        judgementEvents = [];
        return false;
    }

    public static void Clear(ScoreInfo score)
    {
        lock (events)
            events.Remove(score);
    }

    private sealed class Holder(IReadOnlyList<BmsJudgementEvent> judgementEvents)
    {
        public IReadOnlyList<BmsJudgementEvent> Events { get; } = judgementEvents;
    }
}
