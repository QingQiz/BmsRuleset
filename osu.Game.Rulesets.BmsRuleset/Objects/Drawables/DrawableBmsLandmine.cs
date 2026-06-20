using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsLandmine<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Mine;

    // ReSharper disable once ConvertToAutoPropertyWithPrivateSetter
    protected override bool SkipFurtherUpdates => mineHandled;

    private bool mineHandled;

    protected override void ResetKindState() => mineHandled = false;

    protected override bool UpdateKindState()
    {
        if (Judged || mineHandled || Time.Current < HitObject.StartTime)
            return false;

        mineHandled = true;

        if (Playfield?.IsColumnPressedForLandmine(HitObject.Column) == true)
        {
            Playfield.DetonateLandmine(HitObject);
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
