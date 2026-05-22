using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

/// <summary>
///     Placeholder drawable for native BMS notes.
/// </summary>
/// <remarks>
///     This intentionally favours clarity over final gameplay fidelity. The object is rendered in a
///     lane determined by <see cref="BmsHitObject.Column" /> and scrolls by projected osu! time.
/// </remarks>
public sealed partial class DrawableBmsHitObject : DrawableHitObject<BmsHitObject>
{
    private const float travel_distance = 560;

    private Container noteContainer = null!;
    private SkinnableDrawable? note;
    private int skinnedColumn = -1;

    public DrawableBmsHitObject()
        : base(null!)
    {
        Origin = Anchor.BottomLeft;
        Size = new Vector2(40, DefaultBmsNotePiece.NOTE_HEIGHT);
    }

    public bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null)
            return false;

        var result = HitObject.HitWindows.ResultFor(Time.Current - HitObject.StartTime);

        if (result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public void PlayKeySound() => base.PlaySamples();

    public override void PlaySamples()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();

        Alpha = 1;
        updateNotePiece();

        if (note != null)
            note.Colour = HitObject.IsLongNote ? Color4.Cyan : Color4.White;
    }

    protected override void Update()
    {
        base.Update();

        if (HitObject == null)
            return;

        var playfield = Parent?.FindClosestParent<BmsPlayfield>();
        var stage = playfield?.Stage;
        var column = Math.Clamp(HitObject.Column, 0, playfield?.TotalColumns - 1 ?? 0);
        var columnContainer = stage != null && column < stage.Columns.Length ? stage.Columns[column].HitObjectArea : Parent;
        var parentWidth = columnContainer?.DrawWidth ?? Parent?.DrawWidth ?? 0;
        var parentHeight = columnContainer?.DrawHeight ?? Parent?.DrawHeight ?? 0;

        if (!float.IsFinite(parentWidth) || !float.IsFinite(parentHeight) || parentWidth <= 0 || parentHeight <= 0)
        {
            UpdateResult(false);
            return;
        }

        var timeUntilHit = HitObject.StartTime - Time.Current;

        if (!double.IsFinite(timeUntilHit))
        {
            UpdateResult(false);
            return;
        }

        var timeRange = playfield?.TimeRange ?? BmsDrawableRuleset.ComputeScrollTime(8);
        var hitTargetPosition = stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION;
        var y = parentHeight - hitTargetPosition - (float)(timeUntilHit / timeRange) * travel_distance;
        var position = columnContainer != null && Parent != null
            ? Parent.ToLocalSpace(columnContainer.ToScreenSpace(new Vector2(0, y)))
            : new Vector2(0, y);

        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            UpdateResult(false);
            return;
        }

        Position = position;
        Size = new Vector2(Math.Max(1, parentWidth), Math.Max(DefaultBmsNotePiece.NOTE_HEIGHT, note?.DrawHeight ?? 0));

        if (playfield?.IsAutoplay == true && !Judged && Time.Current >= HitObject.StartTime)
        {
            Alpha = 0;
            ApplyMaxResult();
            return;
        }

        UpdateResult(false);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null)
            return;

        if (timeOffset > HitObject.HitWindows.WindowFor(HitResult.Miss))
            ApplyResult(HitResult.Miss);
    }

    protected override void UpdateInitialTransforms()
    {
        base.UpdateInitialTransforms();
        this.FadeInFromZero(100);
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);

        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = HitStateUpdateTime + 100;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    protected override void LoadSamples()
    {
        if (!string.IsNullOrEmpty(HitObject.SamplePath))
            Samples.Samples = [new BmsSampleInfo(HitObject.SamplePath)];
        else
            base.LoadSamples();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        AddInternal(noteContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
        });
    }

    private void updateNotePiece()
    {
        if (HitObject == null || skinnedColumn == HitObject.Column)
            return;

        skinnedColumn = HitObject.Column;

        var playfield = Parent?.FindClosestParent<BmsPlayfield>();
        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.Note, playfield?.LayoutVariant ?? BmsLayoutVariant.Bme7K, HitObject.Column);

        noteContainer.Clear();
        noteContainer.Add(note = new SkinnableDrawable(lookup, _ => new DefaultBmsNotePiece())
        {
            Anchor = Anchor.BottomLeft,
            Origin = Anchor.BottomLeft,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            CentreComponent = false,
        });
    }
}
