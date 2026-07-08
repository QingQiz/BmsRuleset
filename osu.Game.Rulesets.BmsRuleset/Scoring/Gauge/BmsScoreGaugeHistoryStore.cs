using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;

public static class BmsScoreGaugeHistoryStore
{
    private static readonly ConditionalWeakTable<ScoreInfo, Holder> histories = new();

    public static void Set(ScoreInfo score, IEnumerable<BmsGaugeHistoryEvent> history)
    {
        var copy = history
            .Select(e => e with { States = e.States.ToArray() })
            .ToArray();

        lock (histories)
        {
            histories.Remove(score);
            histories.Add(score, new Holder(copy));
        }
    }

    public static bool TryGet(ScoreInfo score, out IReadOnlyList<BmsGaugeHistoryEvent> history)
    {
        lock (histories)
        {
            if (histories.TryGetValue(score, out var holder))
            {
                history = holder.History;
                return true;
            }
        }

        history = [];
        return false;
    }

    public static void Clear(ScoreInfo score)
    {
        lock (histories)
            histories.Remove(score);
    }

    private sealed class Holder(IReadOnlyList<BmsGaugeHistoryEvent> history)
    {
        public IReadOnlyList<BmsGaugeHistoryEvent> History { get; } = history;
    }
}
