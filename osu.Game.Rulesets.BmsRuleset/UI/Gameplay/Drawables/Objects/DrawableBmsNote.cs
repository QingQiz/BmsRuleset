using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Note;

    internal override bool RequiresColumnFrameUpdate => false;

    protected override void OnApply()
    {
        base.OnApply();

        ParentColumn?.Judgements.RefreshNoteRepresentation(this);
    }

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
