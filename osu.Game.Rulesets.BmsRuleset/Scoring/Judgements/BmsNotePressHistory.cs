using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

internal sealed class BmsNotePressHistory
{
    private readonly Dictionary<BmsHitObject, (bool Judged, bool Mistake)> states = [];
    private readonly List<(double Time, BmsHitObject Note, (bool Judged, bool Mistake) Previous)> changes = [];

    public (bool Judged, bool Mistake) Get(BmsHitObject note) => states.GetValueOrDefault(note);

    public void Record(BmsHitObject note, double time, bool judged, bool mistake = false)
    {
        var previous = Get(note);
        var next = (previous.Judged || judged, previous.Mistake || mistake);
        if (previous == next)
            return;

        changes.Add((time, note, previous));
        states[note] = next;
    }

    public void Rewind(double time)
    {
        while (changes.Count > 0 && changes[^1].Time > time)
        {
            var change = changes[^1];
            states[change.Note] = change.Previous;
            changes.RemoveAt(changes.Count - 1);
        }
    }
}
