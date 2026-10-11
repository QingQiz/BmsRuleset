using System.Collections;
using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

internal sealed class BmsScheduledOrder : IReadOnlyList<BmsScheduledObject>
{
    private readonly List<BmsScheduledObject> items = [];
    private readonly List<BmsScheduledObject> snapshot = [];
    private bool dirty;

    public int Count => items.Count;

    public BmsScheduledObject this[int index] => items[index];

    internal void Add(BmsScheduledObject item)
    {
        items.Add(item);
        dirty = true;
    }

    internal void Clear()
    {
        items.Clear();
        dirty = true;
    }

    internal List<BmsScheduledObject> GetSnapshot()
    {
        if (dirty) refreshSnapshot();
        return snapshot;
    }

    private void refreshSnapshot()
    {
        // Result callbacks may rebuild the source while a cursor still needs its original successors.
        snapshot.Clear();
        snapshot.AddRange(items);
        dirty = false;
    }

    public IEnumerator<BmsScheduledObject> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
