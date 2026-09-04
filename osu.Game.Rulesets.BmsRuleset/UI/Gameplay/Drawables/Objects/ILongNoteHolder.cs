using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

/// <summary>
///     Exposes long-note-specific capabilities
/// </summary>
public interface ILongNoteHolder
{
    bool IsHoldingLongNote { get; }

    bool IsAutomaticallyHeld { get; set; }

    bool TryRelease(double releaseOffset, BmsJudgementWindowTable tailTable);

    void UpdateBodyGeometry(float headY, float endY);
}
