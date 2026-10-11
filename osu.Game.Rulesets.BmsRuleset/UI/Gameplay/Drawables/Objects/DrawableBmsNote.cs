using osu.Framework.Allocation;
using osuTK;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Components;
using osu.Game.Rulesets.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    // The framework's reflection fallback misses the inherited protected draw-node factory.
    protected override bool CanBeFlattened => !Masking && !HasProxy && !ForceLocalVertexBatch;

    private BmsNoteVisual noteVisual = null!;

    [BackgroundDependencyLoader]
    private void load() => UseDirectVisual(noteVisual = new BmsNoteVisual(BmsSkinComponents.Note, LayoutVariant, Column));

    protected override float VisualHeight => noteVisual.VisualHeight * (ParentColumn?.NoteHeightScale ?? 1);

    protected override void ApplyVisualHeightScale(float scale) => noteVisual.Scale = new Vector2(1, scale);

    protected override void OnApply()
    {
        base.OnApply();

        ParentColumn?.JudgementBridge.RefreshNoteRepresentation(this);
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state != ArmedState.Hit)
        {
            base.UpdateHitStateTransforms(state);
            BmsNoteVisual.ApplyResultEffects(this, state, Time.Current);
            return;
        }

        Alpha = 0;
        LifetimeEnd = Time.Current;
    }
}
