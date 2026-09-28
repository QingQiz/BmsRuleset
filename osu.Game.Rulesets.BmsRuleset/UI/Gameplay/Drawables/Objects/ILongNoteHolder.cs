using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

/// <summary>
///     Exposes long-note-specific capabilities
/// </summary>
public interface ILongNoteHolder
{
    bool IsHoldingLongNote { get; }

    bool IsAutomaticallyHeld { get; set; }

    bool TryRepress(double currentTime, bool reverseScratch = false) => false;

    bool TryRelease(double releaseOffset, BmsJudgementWindowTable tailTable, bool reverseScratch = false);

    /// <summary>
    ///     Completes an active long-note judgement when gameplay is paused.
    /// </summary>
    bool CompleteAtPause(double currentTime) => false;

    void UpdateBodyGeometry(float headY, float endY);
}
