using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsLandmine<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Mine;
    private BmsLandmineState state => ParentColumn!.Judgements.GetLandmineState((BmsLandmine)HitObject);
    private readonly BmsLandmineStateObserver stateObserver;

    protected override bool SkipFurtherUpdates => state.HandledTime.HasValue && Time.Current >= HitObject.StartTime;
    internal override bool RequiresColumnFrameUpdate => false;

    public DrawableBmsLandmine() => stateObserver = applyPresentation;

    protected override void OnApply()
    {
        base.OnApply();
        ParentColumn!.Judgements.ObserveLandmineState(this, stateObserver);
        ParentColumn.Judgements.RefreshNoteRepresentation(this);
        restorePresentation();
    }

    internal override void RestoreRewoundState() => restorePresentation();

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
