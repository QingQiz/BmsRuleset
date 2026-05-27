using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Audio;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <summary>
///     First native BMS playfield.
/// </summary>
/// <remarks>
///     This is intentionally a simple vertical lane field, not a copied mania stage. It provides the
///     minimum object pooling and layout surface needed to remove the mania dependency. Later phases
///     will split this into BMS columns, BGA layers, key beams, and native scroll timing.
/// </remarks>
public sealed partial class BmsPlayfield : Playfield, IKeyBindingHandler<BmsAction>
{
    public int TotalColumns { get; }

    public BmsLayoutVariant LayoutVariant { get; }

    public BmsStage Stage { get; }

    public bool IsAutoplay { get; }

    public double TimeRange { get; set; } = BmsDrawableRuleset.ComputeScrollTime(8);

    private readonly IReadOnlyList<BmsHitObject> hitObjects;

    private readonly Dictionary<int, int> nextSoundIndexByColumn = new();

    private readonly BmsChartSampleSound keySound = new();

    // Pre-built SkinnableDrawable per HitResult — created once at load, reused on every judgement
    // display by removing from the pool container and adding to JudgementArea, then restoring on
    // the next clear. This avoids a full skin lookup + child construction on every hit.
    private readonly Dictionary<HitResult, SkinnableDrawable> judgementDrawableCache = new();

    // Off-screen container that keeps cached drawables loaded when not shown in JudgementArea.
    private readonly Container judgementDrawablePool;

    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    [Resolved(CanBeNull = true)]
    private BmsHealthProcessor? healthProcessor { get; set; }

    [Resolved(CanBeNull = true)]
    private BmsScoreProcessor? scoreProcessor { get; set; }

    public BmsPlayfield(IReadOnlyList<BmsHitObject> hitObjects, int totalColumns, BmsLayoutVariant layoutVariant = BmsLayoutVariant.Bme7K, bool isAutoplay = false)
    {
        this.hitObjects = hitObjects.OrderBy(h => h.StartTime).ThenBy(h => h.Column).ToArray();
        TotalColumns = Math.Max(1, totalColumns);
        LayoutVariant = layoutVariant;
        IsAutoplay = isAutoplay;

        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        RelativeSizeAxes = Axes.Both;

        InternalChildren =
        [
            Stage = new BmsStage(TotalColumns, LayoutVariant),
            HitObjectContainer,
            keySound,
            judgementDrawablePool = new Container { Alpha = 0, RelativeSizeAxes = Axes.Both },
        ];
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        NewResult -= onNewResult;
        base.Dispose(isDisposing);
    }

    #endregion

    public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
    {
        var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return false;

        playNextKeySound(column.Value);

        // Use the earliest unjudged note in this column that is within a hit window.
        // Picking by StartTime (not by distance) ensures strict sequential ordering:
        // a later note can never be hit before an earlier one in the same column.
        var target = HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .Where(d => !d.Judged &&
                        d.HitObject.Column == column.Value &&
                        d.HitObject.HitWindows is BmsHitWindows w &&
                        w.BmsResultFor(Time.Current - d.HitObject.StartTime) != HitResult.None)
            .MinBy(d => d.HitObject.StartTime);

        if (target?.TryHit() == true)
            return true;

        // No note was consumed. Check whether this press falls inside the Empty POOR zone:
        // a press earlier than −early_poor_window ms before every unjudged note in this
        // column (or when there are no remaining notes at all).
        // Empty POOR: combo break + gauge penalty, but no note is consumed.
        if (!IsAutoplay)
            registerEmptyPoor();

        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
    {
        var column = BmsKeyBindingConfiguration.ActionToColumn(e.Action, LayoutVariant);

        if (column == null || column.Value >= TotalColumns)
            return;

        // Release: find the earliest LN in this column that is held and within the release window.
        HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .Where(d => !d.Judged && d.HitObject.IsLongNote && d.HitObject.Column == column.Value
                        && d.HitObject.HitWindows.ResultFor(Time.Current - d.HitObject.EndTime) != HitResult.None)
            .MinBy(d => d.HitObject.EndTime)
            ?.TryRelease();
    }

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject) => new BmsHitObjectLifetimeEntry(hitObject);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        NewResult += onNewResult;

        // Pre-build one SkinnableDrawable per result type so that showJudgement() never
        // allocates during gameplay.  HitResult.Miss is the empty-POOR display key.
        foreach (var result in BmsRuleset.STATIC_VALID_HIT_RESULTS)
        {
            var drawable = new SkinnableDrawable(
                new SkinComponentLookup<HitResult>(result),
                r => new BmsDefaultJudgementPiece(
                    result == HitResult.Miss
                        ? HitResult.Meh // empty POOR shows "POOR" text
                        : ((SkinComponentLookup<HitResult>)r).Component))
            {
                RelativeSizeAxes = Axes.None,
                AutoSizeAxes = Axes.Both,
            };

            judgementDrawableCache[result] = drawable;
            judgementDrawablePool.Add(drawable);
        }
    }

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        RegisterPool<BmsHitObject, DrawableBmsHitObject>(32, 512);

        if (samplePlaybackDisabler != null)
            samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
    }

    private void onNewResult(DrawableHitObject drawableHitObject, JudgementResult result)
    {
        if (drawableHitObject is not DrawableBmsHitObject bmsHitObject)
            return;

        // Hit explosion only on hits (not misses).
        if (result.IsHit)
        {
            var column = Math.Clamp(bmsHitObject.HitObject.Column, 0, Stage.Columns.Length - 1);
            Stage.Columns[column].HitExplosionArea.Add(new BmsHitExplosion(new BmsSkinComponentLookup(
                BmsSkinComponents.HitExplosion,
                LayoutVariant,
                column,
                bmsHitObject.HitObject.IsLongNote)));
        }

        // Show judgement text for every result type (hits and misses).
        showJudgement(result.Type);
    }

    // ── EMPTY POOR ────────────────────────────────────────────────────────

    /// <summary>
    ///     Fires an Empty POOR: a keypress that found no note to consume.
    ///     Breaks combo and drains gauge by the same amount as a normal POOR,
    ///     but does not register a <see cref="JudgementResult"/> against any note.
    ///     Displays the POOR image (same as a note POOR) in the judgement area.
    /// </summary>
    private void registerEmptyPoor()
    {
        scoreProcessor?.RegisterEmptyPoor();
        healthProcessor?.RegisterEmptyPoor();

        // Show the POOR image. We look up HitResult.Miss, which the skin transformer
        // maps to the same mania-hit0 image as HitResult.Meh (POOR), so both note POORs
        // and empty POORs display identically.
        showJudgement(HitResult.Miss);
    }

    /// <summary>
    ///     Moves the pre-built <see cref="SkinnableDrawable"/> for <paramref name="result"/> from
    ///     the hidden pool container into <see cref="BmsStage.JudgementArea"/> and replays its
    ///     animation.  Any previously shown drawable is returned to the pool container so it stays
    ///     loaded and ready for the next use.
    /// </summary>
    private void showJudgement(HitResult result)
    {
        if (!judgementDrawableCache.TryGetValue(result, out var drawable))
            return;

        // Return the current occupant of JudgementArea to the pool (Clear(false) = remove without dispose).
        foreach (var child in Stage.JudgementArea)
            judgementDrawablePool.Add(child);

        Stage.JudgementArea.Clear(false);

        // Move the cached drawable into the display area and replay its animation.
        judgementDrawablePool.Remove(drawable, false);
        Stage.JudgementArea.Add(drawable);

        if (drawable.Drawable is IAnimatableJudgement animatable)
        {
            drawable.ResetAnimation();
            animatable.PlayAnimation();
        }
    }

    // ── KEY SOUND ─────────────────────────────────────────────────────────

    private void playNextKeySound(int column)
    {
        if (samplePlaybackDisabled.Value)
            return;

        if (findNextSoundHitObject(column) is not { } hitObject || string.IsNullOrEmpty(hitObject.SamplePath))
            return;

        keySound.SampleInfo = new BmsSampleInfo(hitObject.SamplePath);
        keySound.Play();
    }

    private BmsHitObject? findNextSoundHitObject(int column)
    {
        var index = nextSoundIndexByColumn.GetValueOrDefault(column);

        // Skip notes that are definitely past all hit windows. Use the full BAD window (the widest
        // late window) so we never jump over a note that is still judgeable on a late keypress.
        while (index < hitObjects.Count && hitObjects[index].StartTime < Time.Current - BmsHitWindows.BAD_WINDOW)
            index++;

        while (index < hitObjects.Count)
        {
            var hitObject = hitObjects[index];

            if (hitObject.Column != column || hasNoteFinished(hitObject))
            {
                index++;
                continue;
            }

            nextSoundIndexByColumn[column] = index;
            return hitObject;
        }

        nextSoundIndexByColumn[column] = index;
        return null;
    }

    private bool hasNoteFinished(BmsHitObject hitObject)
    {
        var drawable = HitObjectContainer.AliveObjects
            .OfType<DrawableBmsHitObject>()
            .FirstOrDefault(d => ReferenceEquals(d.HitObject, hitObject));

        if (drawable?.Judged == true)
            return true;

        if (Time.Current > hitObject.StartTime && drawable == null)
            return true;

        return Time.Current > hitObject.StartTime + hitObject.HitWindows.WindowFor(HitResult.Ok);
    }

    private sealed class BmsHitObjectLifetimeEntry : HitObjectLifetimeEntry
    {
        public BmsHitObjectLifetimeEntry(HitObject hitObject)
            : base(hitObject)
        {
            // The native BMS renderer currently uses a generous fixed lifetime until BMS-specific
            // scroll timing and LN rendering are implemented.
            LifetimeEnd = hitObject.GetEndTime() + 1000;
        }
    }
}
