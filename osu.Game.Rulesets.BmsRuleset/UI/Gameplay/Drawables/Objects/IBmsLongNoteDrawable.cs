namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

/// <summary>
/// Presentation changes that must run at the controller's synchronous result boundaries.
/// </summary>
internal interface IBmsLongNoteDrawable
{
    void PrepareHeadPin();
    void KeepBodyAlive(double lifetimeEnd);
    void ClearBody();
    void Retire();
}
