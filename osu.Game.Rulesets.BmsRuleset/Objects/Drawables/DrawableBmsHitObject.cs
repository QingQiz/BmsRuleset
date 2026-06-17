using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Shared visual base for a single BMS note. Concrete subclasses own
///     note-kind-specific judgement and state handling.
/// </summary>
public abstract partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{

    #region Properties

    protected BmsPlayfield? Playfield { get; private set; }

    #endregion

    #region Skin

    protected abstract BmsSkinComponents SkinComponent { get; }

    #endregion

    #region Core drawable fields

    protected Container NoteContainer = null!;

    #endregion

    #region Construction

    protected DrawableBmsHitObject()
        : base(null!)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;
    }

    #endregion

    #region Public API

    /// <summary>
    ///     Attempts a key-down judgement on this note.  Returns <c>true</c> if the
    ///     note was in a valid hit window and was consumed.
    /// </summary>
    public virtual bool TryHit() => false;

    public override void PlaySamples()
    {
    }

    #endregion

    #region Kind hooks

    protected virtual bool SkipFurtherUpdates => false;

    protected virtual void ResetKindState()
    {
    }

    /// <summary>
    ///     Runs kind-specific state after common layout has been updated.
    ///     Return <c>true</c> when the common passive result check should be skipped.
    /// </summary>
    protected virtual bool UpdateKindState() => false;

    /// <summary>
    ///     Adds kind-specific drawables before the note container.
    /// </summary>
    protected virtual void AddKindDrawablesBeforeNote()
    {
    }

    #endregion

    #region Loading

    [Resolved(CanBeNull = true)]
    private BmsPlayfield? playfield { get; set; }

    [BackgroundDependencyLoader]
    private void load()
    {
        Playfield = playfield;

        AddKindDrawablesBeforeNote();

        AddInternal(
            NoteContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
            });
    }

    #endregion

    #region Framework overrides

    protected override void OnApply()
    {
        base.OnApply();

        if (NoteContainer.Count == 0 && HitObject != null && playfield != null)
        {
            NoteContainer.Add(new BmsCachedSkinnableDrawable(
                new BmsSkinComponentLookup(SkinComponent, playfield.LayoutVariant, HitObject.Column))
            {
                ComponentAnchor = Anchor.BottomCentre,
            });
        }

        Alpha = 1;
        ResetKindState();
    }

    protected override void Update()
    {
        base.Update();

        if (HitObject == null)
            return;

        if (Judged || SkipFurtherUpdates)
            return;

        if (UpdateKindState())
            return;

        UpdateResult(false);
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);

        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = Math.Max(HitObject.EndTime, HitStateUpdateTime) + 100;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    /// <summary>
    ///     The key sound is processed by the playfield, so the sample is not needed to load here.
    /// </summary>
    protected override void LoadSamples()
    {
    }

    #endregion

}
