using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
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
    private const float default_note_height = 14;
    private const float max_long_note_piece_height = 4096;

    private Box noteBox = null!;
    private Box longNoteBody = null!;
    private Box longNoteTail = null!;
    private bool longNoteStarted;
    private BmsPlayfield? playfield;
    private float currentNoteHeight = default_note_height;

    public DrawableBmsHitObject()
        : base(null!)
    {
        Origin = Anchor.TopLeft;
        Size = new Vector2(40, default_note_height);
    }

    public bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.StartTime);

        if (result == HitResult.None)
            return false;

        if (HitObject.IsLongNote)
        {
            longNoteStarted = true;
            return true;
        }

        ApplyResult(result);
        return true;
    }

    public bool TryRelease()
    {
        if (Judged || HitObject?.HitWindows == null || !HitObject.IsLongNote || !longNoteStarted)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.EndTime);

        if (result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    protected override void OnApply()
    {
        base.OnApply();

        Alpha = 1;
        longNoteStarted = false;
        updateNativePieces();
    }

    protected override void Update()
    {
        base.Update();

        // HitObject may not be set yet during early pool lifecycle; bail out.
        if (HitObject == null)
            return;

        // Lazily resolve the playfield on the first valid frame; stage is a shorthand reference.
        playfield ??= Parent?.FindClosestParent<BmsPlayfield>();
        var stage = playfield?.Stage;

        // Clamp the column index to avoid out-of-range access when the layout changes at runtime.
        var column = Math.Clamp(HitObject.Column, 0, playfield?.TotalColumns - 1 ?? 0);
        // Prefer the per-column HitObjectArea as the sizing reference; fall back to Parent if unavailable.
        var columnContainer = stage != null && column < stage.Columns.Length ? stage.Columns[column].HitObjectArea : Parent;
        var parentWidth = columnContainer?.DrawWidth ?? Parent?.DrawWidth ?? 0;
        var parentHeight = columnContainer?.DrawHeight ?? Parent?.DrawHeight ?? 0;

        // Skip positioning until the layout has finished its first valid measure pass.
        if (!float.IsFinite(parentWidth) || !float.IsFinite(parentHeight) || parentWidth <= 0 || parentHeight <= 0)
        {
            UpdateResult(false);
            return;
        }

        // How far in time this note is from the current playback position.
        var timeUntilHit = HitObject.StartTime - Time.Current;
        var endTimeUntilHit = HitObject.EndTime - Time.Current;

        // NaN/Infinity can occur before timing is initialised; bail out safely.
        if (!double.IsFinite(timeUntilHit))
        {
            UpdateResult(false);
            return;
        }

        // timeRange is the visible time window (ms) that maps to the full travel distance on screen.
        var timeRange = playfield?.TimeRange ?? BmsDrawableRuleset.ComputeScrollTime(8);
        // hitTargetPosition is the Y offset (px) of the judgement line from the bottom of the column.
        var hitTargetPosition = stage?.HitTargetPosition ?? BmsStage.HIT_TARGET_POSITION;
        // Convert time offsets to bottom-edge positions relative to the column container.
        var y = parentHeight - hitTargetPosition - (float)(timeUntilHit / timeRange) * travel_distance - currentNoteHeight;
        var tailY = parentHeight - hitTargetPosition - (float)(endTimeUntilHit / timeRange) * travel_distance - currentNoteHeight;
        // Once an LN head has been pressed, clamp visualHeadY to the hit target so it doesn't scroll past it.
        var visualHeadY = HitObject.IsLongNote && longNoteStarted && Time.Current >= HitObject.StartTime
            ? Math.Min(y, parentHeight - hitTargetPosition - currentNoteHeight)
            : y;
        var visualTailY = HitObject.IsLongNote && longNoteStarted && Time.Current >= HitObject.EndTime
            ? Math.Min(tailY, parentHeight - hitTargetPosition - currentNoteHeight)
            : tailY;
        // Map the column-local Y into the parent drawable's coordinate space.
        var objectTop = HitObject.IsLongNote ? Math.Min(visualHeadY, visualTailY) : visualHeadY;
        var headOffset = visualHeadY - objectTop;
        var tailOffset = visualTailY - objectTop;
        var position = columnContainer != null && Parent != null
            ? columnContainer.ToSpaceOfOtherDrawable(new Vector2(0, objectTop), Parent)
            : new Vector2(0, objectTop);
        var scaledParentWidth = parentWidth;

        // Account for any scale transform on the column container when computing the note width.
        if (columnContainer != null && Parent != null)
        {
            var left = columnContainer.ToSpaceOfOtherDrawable(Vector2.Zero, Parent);
            var right = columnContainer.ToSpaceOfOtherDrawable(new Vector2(parentWidth, 0), Parent);
            scaledParentWidth = (right - left).Length;
        }

        // A non-finite mapped position means the coordinate transform is not ready yet.
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            UpdateResult(false);
            return;
        }

        Position = position;
        // Scale note height down proportionally when the column is narrower than the default width.
        var columnScale = parentWidth > 0 ? scaledParentWidth / parentWidth : 1;
        currentNoteHeight = Math.Max(1, default_note_height * Math.Min(1, columnScale));

        // Enforce a 1 px minimum in both dimensions to avoid zero-size drawables.
        var objectHeight = HitObject.IsLongNote
            ? Math.Max(currentNoteHeight, Math.Abs(visualTailY - visualHeadY) + currentNoteHeight)
            : currentNoteHeight;

        Size = new Vector2(Math.Max(1, scaledParentWidth), objectHeight);
        // Reposition the LN body and tail pieces to match the updated head/tail Y values.
        updateLongNotePieces(headOffset, tailOffset);

        // The execution of the Update function is frame-by-frame.
        // Therefore, when the execution finds that the current time is
        // greater than the trigger time of the hitobject,
        // it means it is being triggered in the current or next frame.
        // At this point, the mine is processed:
        // if it is held down, trigger the mine; otherwise, let it expire immediately.
        if (HitObject.IsMine && !Judged && Time.Current >= HitObject.StartTime)
        {
            if (playfield?.IsColumnPressedForLandmine(HitObject.Column) == true)
            {
                playfield.DetonateLandmine(HitObject);
                ApplyResult(HitResult.Meh);
            }
            else
                Expire(); // column was not held — mine passes silently

            return;
        }

        // Mark LN as started so visualHeadY clamping kicks in from the next frame.
        if (playfield?.IsAutoplay == true && HitObject.IsLongNote && Time.Current >= HitObject.StartTime)
            longNoteStarted = true;

        // Autoplay: hide and auto-judge the note at its hit time (tail time for LNs, start time for normals).
        if (playfield?.IsAutoplay == true && !Judged && Time.Current >= (HitObject.IsLongNote ? HitObject.EndTime : HitObject.StartTime))
        {
            Alpha = 0;
            ApplyMaxResult();
            return;
        }

        // Passive miss check: called every frame so CheckForResult can apply a miss once the hit window closes.
        UpdateResult(false);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null || HitObject.IsMine)
            return;

        var missWindow = HitObject.HitWindows.WindowFor(HitResult.Ok); // BAD is the passive-miss boundary

        if (HitObject.IsLongNote)
        {
            // LN head never pressed: passive POOR once the head BAD window is exhausted.
            if (!longNoteStarted && Time.Current > HitObject.StartTime + missWindow)
            {
                ApplyResult(HitResult.Meh);
                return;
            }

            // LN held but player never released before the tail BAD window expired:
            // this is a "drop" — scores POOR (Meh) in BMS.
            if (longNoteStarted && Time.Current > HitObject.EndTime + missWindow)
            {
                ApplyResult(HitResult.Meh);
                // ReSharper disable once RedundantJumpStatement
                return;
            }

            // For an in-progress LN the framework-supplied timeOffset is relative to
            // StartTime; do not apply the generic miss check below until the tail window.
            return;
        }

        // Normal note: passive POOR (Meh) once the BAD window is passed with no keypress.
        if (timeOffset > missWindow)
            ApplyResult(HitResult.Meh);
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
                LifetimeEnd = Math.Max(HitObject.EndTime, HitStateUpdateTime) + 100;
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
        AddRangeInternal([
            longNoteBody = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Alpha = 0,
            },
            longNoteTail = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = default_note_height,
                Alpha = 0,
            },
            noteBox = new Box
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Height = default_note_height,
            },
        ]);
    }

    private void updateLongNotePieces(float headOffset, float tailOffset)
    {
        if (!HitObject.IsLongNote)
        {
            noteBox.Y = 0;
            noteBox.Height = currentNoteHeight;
            longNoteBody.Alpha = 0;
            longNoteTail.Alpha = 0;
            return;
        }

        noteBox.Y = headOffset;
        noteBox.Height = currentNoteHeight;

        // Body spans centre-to-centre between the head and tail caps.
        var headCentre = headOffset + currentNoteHeight / 2;
        var tailCentre = tailOffset + currentNoteHeight / 2;
        var bodyTop = Math.Min(headCentre, tailCentre);
        var bodyBottom = Math.Max(headCentre, tailCentre);

        // Clamp to within one screen-length of the head so that super-long BMS LNs
        // (where the tail is thousands of pixels away) don't produce enormous geometry.
        var visibleTop = Math.Max(bodyTop, headOffset - max_long_note_piece_height);
        var visibleBottom = Math.Min(bodyBottom, headOffset + max_long_note_piece_height);
        var bodyHeight = Math.Max(1, visibleBottom - visibleTop);

        longNoteBody.Y = visibleTop;
        longNoteBody.Height = bodyHeight;
        longNoteBody.Alpha = 0.65f;
        longNoteTail.Y = tailOffset;
        longNoteTail.Height = currentNoteHeight;
        longNoteTail.Alpha = 1;
    }

    private void updateNativePieces()
    {
        if (HitObject == null)
            return;

        var colour = HitObject.IsMine ? Color4.OrangeRed : HitObject.IsLongNote ? Color4.Cyan : Color4.White;
        noteBox.Colour = colour;
        longNoteBody.Colour = Color4.Cyan;
        longNoteTail.Colour = Color4.Cyan;
        longNoteBody.Alpha = HitObject.IsLongNote ? 0.65f : 0;
        longNoteTail.Alpha = HitObject.IsLongNote ? 1 : 0;
    }
}
