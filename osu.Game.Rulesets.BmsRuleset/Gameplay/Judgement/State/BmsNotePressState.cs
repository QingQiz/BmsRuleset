using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;

internal sealed class BmsNotePressState
{
    private readonly Dictionary<BmsHitObject, (bool Judged, bool Mistake)> states = [];

    public (bool Judged, bool Mistake) Get(BmsHitObject note) => states.GetValueOrDefault(note);

    public void Record(BmsHitObject note, bool judged, bool mistake = false)
    {
        var previous = Get(note);
        var next = (previous.Judged || judged, previous.Mistake || mistake);
        if (previous == next)
            return;

        states[note] = next;
    }
}
