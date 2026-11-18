using RhythmEngine.Core;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;

internal readonly record struct BmsLongNotePresentationContext(double Time, bool Held, NoteTiming Timing);

internal delegate void BmsLongNoteStateObserver(NoteSnapshot state, in BmsFrameContext frame);

internal delegate BmsLongNotePresentation BmsLongNotePresentationMapping(BmsLongNote note, NoteSnapshot snapshot, in BmsLongNotePresentationContext context);

internal readonly record struct BmsLongNotePresentation(
    bool HeadJudged,
    bool KeepAfterHeadResult,
    bool KeepAlive,
    bool Retired,
    bool ClearBodyOnRetire,
    double LifetimeEnd)
{
    private HoldFeedback feedback { get; init; }

    internal bool HoldingBody => feedback.HasFlag(HoldFeedback.PinHead);

    internal bool AnimateBody => feedback.HasFlag(HoldFeedback.AnimateBody);

    internal bool HoldLightActive => feedback.HasFlag(HoldFeedback.HoldLight);

    internal bool ReleasedStyle { get; private init; }

    internal bool PrepareHeadPin { get; init; }

    internal bool HeadReached { get; init; }

    internal bool KeepVisibleAfterHit { get; init; }

    internal BmsLongNotePresentation WithHoldFeedback(HoldFeedback effects, bool showReleasedStyle, bool lightInRange = true)
        => this with
        {
            feedback = lightInRange ? effects : effects & ~HoldFeedback.HoldLight,
            ReleasedStyle = showReleasedStyle && !effects.HasFlag(HoldFeedback.BrightBody),
        };
}
