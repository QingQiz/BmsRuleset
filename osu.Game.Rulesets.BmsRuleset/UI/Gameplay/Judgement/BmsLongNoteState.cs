using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

internal delegate void BmsLongNoteStateObserver(in BmsLongNoteState state, in BmsFrameContext frame);

internal readonly record struct BmsLongNoteState(
    bool HeadJudged, bool LongNoteStarted, bool TailJudged, HitResult HeadResult, double? HeadTime,
    HitResult TailResult, double? TailTime, double? PendingReleaseTime, bool HasActiveBody = false)
{
    internal bool HasPendingTail => HeadJudged && !TailJudged;
    internal bool IsHolding => LongNoteStarted && !TailJudged;
}
