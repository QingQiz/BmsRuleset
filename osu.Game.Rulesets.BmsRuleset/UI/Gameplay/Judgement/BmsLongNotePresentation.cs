using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

/// <summary>
/// The bounded long-note adapter projects read-only state onto the existing visual implementation.
/// P3b replaces these legacy drawable calls with presentation data consumed by the shared drawable.
/// </summary>
internal static class BmsLongNotePresentation
{
    internal static void Apply(DrawableBmsHitObject drawable, in BmsLongNoteState previous, in BmsLongNoteState state)
    {
        var visual = (IBmsLongNoteDrawable)drawable;
        if (!previous.HeadJudged && state.HeadJudged && !state.TailJudged)
        {
            if (state.HeadResult == HitResult.Meh && state.HasActiveBody)
                visual.KeepBodyAlive(drawable.LongNoteJudgement!.ChargeTailLifetimeEnd());
            else
                visual.PrepareHeadPin();
        }
        if (!previous.TailJudged && state.TailJudged && state.TailResult != HitResult.Meh && !state.LongNoteStarted)
            visual.ClearBody();
        else if (previous.LongNoteStarted && !state.LongNoteStarted && state.TailJudged)
            visual.Retire();
    }
}
