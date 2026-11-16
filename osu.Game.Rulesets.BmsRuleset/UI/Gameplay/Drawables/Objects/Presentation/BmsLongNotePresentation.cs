using RhythmEngine.Core;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;

internal readonly record struct BmsLongNotePresentationContext(double Time, bool Held, NoteTiming Timing);

internal delegate void BmsLongNoteStateObserver(NoteSnapshot state, in BmsFrameContext frame);

internal delegate BmsLongNotePresentation BmsLongNotePresentationMapping(BmsLongNote note, NoteSnapshot snapshot, in BmsLongNotePresentationContext context);

internal readonly record struct BmsLongNotePresentation(
    bool HeadJudged,
    bool HoldingBody,
    bool KeepAfterHeadResult,
    bool KeepAlive,
    bool Retired,
    bool ClearBodyOnRetire,
    double LifetimeEnd,
    bool HoldLightActive)
{
    internal bool AnimateBody { get; init; }

    internal bool ReleasedStyle { get; init; }

    internal bool PrepareHeadPin { get; init; }

    internal bool RestoreHeadPin { get; init; }

    internal bool HeadReached { get; init; }

    internal bool KeepVisibleAfterHit { get; init; }

    internal BmsHitLightStyle HitLightStyle => BmsHitLightStyle.Hold;
}
