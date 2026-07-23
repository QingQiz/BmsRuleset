using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects;

/// <summary>
///     Exposes long-note-specific capabilities
/// </summary>
public interface ILongNoteHolder
{
    bool IsHoldingLongNote { get; }

    bool TryRelease(double releaseOffset, BmsJudgementWindowTable tailTable);

    void UpdateBodyGeometry(float headY, float endY);
}
