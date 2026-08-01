using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Objects;

public sealed partial class DrawableBmsNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Note;

    // The framework already performs passive result checks; normal notes have no BMS-specific frame state.
    internal override bool RequiresColumnFrameUpdate => false;
    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject == null)
            return;

        var table = BmsJudgementProfileProvider.GetTable(HitObject.Beatmap.LayoutVariant, HitObject.Column, HitObject.EffectiveJudgementRate, tail: false);
        if (table.IsPastPassivePoorOffset(timeOffset))
            ApplyResult(HitResult.Meh);
    }

    protected override void UpdateInitialTransforms() => Alpha = 1;

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state != ArmedState.Hit)
        {
            base.UpdateHitStateTransforms(state);
            return;
        }

        Alpha = 0;
        LifetimeEnd = Time.Current;
    }
}
