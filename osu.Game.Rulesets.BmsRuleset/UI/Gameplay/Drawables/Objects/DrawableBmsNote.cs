using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Note;

    internal override bool RequiresColumnFrameUpdate => false;

    protected override bool UsesPassiveResultCheck => false;

    private BmsNoteJudgementWindows rule;

    internal override double NextPassiveJudgementTime => rule.NextPassiveJudgementTime(HitObject);

    protected override void OnApply()
    {
        base.OnApply();

        rule = new BmsNoteJudgementWindows(HitObject);
        ParentColumn?.Judgements.RefreshNoteRepresentation(this);
    }

    public override bool TryHit(HitResult result) => ParentColumn!.Judgements.TryPressNote(this, rule, result);

    public override bool UpdateSubTree()
    {
        var updated = base.UpdateSubTree();
        // Dispatch at each object's traversal boundary, including culled objects, so mixed
        // note types keep their result order before the next sibling is visited.
        if (IsLoaded && !UsesCoreScheduling)
            ParentColumn!.Judgements.CheckPassiveNote(this, rule);
        return updated;
    }

    public override void OnKilled()
    {
        base.OnKilled();
        // A large time step can retire a representation before its next subtree visit.
        if (!UsesCoreScheduling) ParentColumn!.Judgements.CheckPassiveNote(this, rule);
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
