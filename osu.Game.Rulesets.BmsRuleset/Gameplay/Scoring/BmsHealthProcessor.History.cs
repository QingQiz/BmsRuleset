using System.Collections;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;

public partial class BmsHealthProcessor
{
    private sealed class GaugeHistoryBuffer : IReadOnlyList<BmsGaugeHistoryEvent>
    {
        private readonly List<StoredEvent> events = [];
        private readonly List<StoredState> states = [];

        public int Count => events.Count;

        public BmsGaugeHistoryEvent this[int index]
        {
            get
            {
                var entry = events[index];
                var snapshots = new BmsGaugeStateSnapshot[entry.StateCount];
                for (var i = 0; i < snapshots.Length; i++)
                {
                    var state = states[entry.StateOffset + i];
                    snapshots[i] = new BmsGaugeStateSnapshot(state.GaugeType, state.Health, state.Failed);
                }

                return new BmsGaugeHistoryEvent(entry.Time, entry.ActiveGaugeType, snapshots);
            }
        }

        public void Record(List<GaugeState> currentStates, double time, BmsGaugeType activeGaugeType)
        {
            // Keep exact applied values without creating snapshot objects on the judgement path.
            // Expanding them on read moves that allocation to graph rendering and score export.
            var offset = states.Count;
            for (var i = 0; i < currentStates.Count; i++)
            {
                var state = currentStates[i];
                states.Add(new StoredState(state.CurrentHp, state.GaugeType, state.IsHpFailed));
            }

            events.Add(new StoredEvent(time, offset, currentStates.Count, activeGaugeType));
        }

        public void Clear()
        {
            events.Clear();
            states.Clear();
        }

        public IEnumerator<BmsGaugeHistoryEvent> GetEnumerator()
        {
            for (var i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private readonly record struct StoredEvent(
            double Time,
            int StateOffset,
            int StateCount,
            BmsGaugeType ActiveGaugeType);

        private readonly record struct StoredState(double Health, BmsGaugeType GaugeType, bool Failed);
    }
}
