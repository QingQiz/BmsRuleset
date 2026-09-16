using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsLandmine<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Mine;

    // The column owns detonation; a culled mine has no passive POOR check to preserve.
    protected override bool UsesPassiveResultCheck => false;

    protected override bool SkipFurtherUpdates => mineHandled && Time.Current >= HitObject.StartTime;

    private bool mineHandled;

    internal override bool RequiresColumnFrameUpdate => !SkipFurtherUpdates;

    protected override void ResetKindState() => mineHandled = false;

    protected override bool UpdateKindState()
    {
        if (mineHandled && Time.Current < HitObject.StartTime)
        {
            // A backwards seek can revisit a handled mine before its drawable returns to the pool.
            mineHandled = false;
            Alpha = 1;
        }

        if (Judged || mineHandled || Time.Current < HitObject.StartTime)
            return false;

        mineHandled = true;

        if (ParentColumn?.IsPressed == true && Time.Current < HitObject.StartTime + BmsHitObjectLifetimeEntry.MINE_PAST_LIFETIME)
        {
            ParentColumn?.DetonateLandmine(HitObject);
            ApplyResult(HitResult.Meh);
        }
        else
        {
            Alpha = 0;
        }

        return true;
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
    }
}
