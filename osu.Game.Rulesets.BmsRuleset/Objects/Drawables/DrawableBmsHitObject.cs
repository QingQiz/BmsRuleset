using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public abstract partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{

    protected abstract BmsSkinComponents SkinComponent { get; }

    protected virtual bool SkipFurtherUpdates => false;

    protected Container NoteContainer = null!;

    [Resolved(CanBeNull = true)]
    protected BmsPlayfield? Playfield { get; private set; }

    [Resolved(CanBeNull = true)]
    protected IBmsScoring? Scoring { get; private set; }

    [Resolved(CanBeNull = true)]
    protected BmsColumn? ParentColumn { get; private set; }

    protected int Column { get; init; } = -1;

    protected DrawableBmsHitObject()
        : base(null!)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;
    }

    public virtual bool TryHit(HitResult result)
    {
        if (Judged || result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    protected virtual void ResetKindState()
    {
    }

    protected virtual bool UpdateKindState() => false;

    protected virtual void AddKindDrawablesBeforeNote()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();
        Alpha = 1;
        ResetKindState();
    }

    protected override void Update()
    {
        base.Update();
        if (HitObject == null) return;
        if (Judged || SkipFurtherUpdates) return;
        if (UpdateKindState()) return;

        UpdateResult(false);
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);
        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = Time.Current;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                LifetimeEnd = Time.Current + 300;
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    protected override void LoadSamples()
    {
    }
}

public abstract partial class DrawableBmsHitObject<TCol> : DrawableBmsHitObject
    where TCol : struct, IColumnProvider
{
    private BmsCachedSkinnableDrawable? cachedSkinnableDrawable;

    protected DrawableBmsHitObject()
    {
        Column = default(TCol).Value;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        AddKindDrawablesBeforeNote();

        NoteContainer = new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            RelativeSizeAxes = Axes.X,
        };

        cachedSkinnableDrawable = new BmsCachedSkinnableDrawable(
            new BmsSkinComponentLookup(SkinComponent, Playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K, Column))
        {
            ComponentAnchor = Anchor.BottomCentre,
        };

        NoteContainer.Add(cachedSkinnableDrawable);
        AddInternal(NoteContainer);
    }
}
