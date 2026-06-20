namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Exposes long-note-specific capabilities
/// </summary>
public interface ILongNoteHolder
{
    bool IsHoldingLongNote { get; }

    bool TryRelease();

    void UpdateBodyGeometry(float headY, float endY);
}
