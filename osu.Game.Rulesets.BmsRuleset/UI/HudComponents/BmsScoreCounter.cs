using osu.Game.Screens.Play.HUD;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

public sealed partial class BmsScoreCounter : ArgonScoreCounter
{
    private bool countUpdatePending;

    protected override void TransformCount(long currentValue, long newValue) => countUpdatePending = true;

    public override bool UpdateSubTree()
    {
        // Replay catch-up can change the score thousands of times before one HUD frame.
        // Keep the final target, and flush before transforms advance, including while hidden.
        if (countUpdatePending)
        {
            countUpdatePending = false;
            base.TransformCount(DisplayedCount, Current.Value);
        }

        return base.UpdateSubTree();
    }

    public override void StopRolling()
    {
        countUpdatePending = false;
        base.StopRolling();
    }
}
