using osu.Framework.Allocation;
using osuTK;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsLandmine<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    private BmsNoteVisual noteVisual = null!;

    [BackgroundDependencyLoader]
    private void load() => Visuals.Add(noteVisual = new BmsNoteVisual(BmsSkinComponents.Mine, LayoutVariant, Column));

    protected override float VisualHeight => noteVisual.VisualHeight * (ParentColumn?.NoteHeightScale ?? 1);
    protected override void ApplyVisualHeightScale(float scale) => noteVisual.Scale = new Vector2(1, scale);
    private BmsLandmineState state => ParentColumn!.Judgements.GetLandmineState((BmsLandmine)HitObject);
    private readonly BmsLandmineStateObserver stateObserver;

    protected override bool SkipFurtherUpdates => state.HandledTime.HasValue && Time.Current >= HitObject.StartTime;

    public DrawableBmsLandmine() => stateObserver = applyPresentation;

    protected override void OnApply()
    {
        base.OnApply();
        ParentColumn!.JudgementOutput.ObserveLandmineState(this, stateObserver);
        ParentColumn.JudgementOutput.RefreshNoteRepresentation(this);
        restorePresentation();
    }

    internal override void RestoreRewoundVisuals() => restorePresentation();

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);
        BmsNoteVisual.ApplyResultEffects(this, state, Time.Current);
    }

    private void restorePresentation()
    {
        Alpha = state.HandledTime.HasValue ? 0 : 1;
        if (state.Detonated)
            LifetimeEnd = HitObject.StartTime + BmsLandmineJudgementRule.PASSING_LIFETIME;
    }

    private void applyPresentation(in BmsLandmineState snapshot, in BmsFrameContext frame)
    {
        if (snapshot.Detonated)
            // Retain the result through the same traversal's lifetime pass.
            LifetimeEnd = HitObject.StartTime + BmsLandmineJudgementRule.PASSING_LIFETIME;
        else if (snapshot.HandledTime.HasValue)
            Alpha = 0;
    }
}
