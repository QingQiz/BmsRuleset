using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

[Cached]
public partial class BmsColumn : Playfield, IBmsColumn
{
    public const float COLUMN_WIDTH = 42;
    public const float SCRATCH_COLUMN_WIDTH = 50;

    public readonly int Index;

    public int ColumnIndex => Index;

    public bool IsScratch { get; }

    // Owned by the column but parented to a stage-level layer (above the judgement line) by BmsStage,
    // so hit explosions render on top of the stage hitTarget instead of behind it.
    public Container HitExplosionArea { get; } = new() { RelativeSizeAxes = Axes.Y };

    public Drawable KeyArea { get; }

    public Container KeyAreaUnderNotesLayer { get; } = new() { RelativeSizeAxes = Axes.Both };

    /// <summary>
    ///     When <c>true</c>, this column is hidden from layout — zero width, zero
    ///     alpha, and <see cref="updateFromSkin"/> will not restore visual properties.
    /// </summary>
    public bool Hidden
    {
        get;
        set
        {
            if (field == value)
                return;

            field = value;

            if (value)
            {
                Width = 0;
                Alpha = 0;
                Margin = new MarginPadding();
            }
        }
    }

    internal const float HIT_OBJECT_DEPTH = 0;

    internal BmsLayoutVariant LayoutVariant { get; }

    internal float HitTargetPosition => ParentPlayfield.Stage.HitTargetPosition;

    internal double ScrollSpeedMultiplier => ParentPlayfield.ScrollController.ScrollSpeedMultiplier;

    internal double VisualOffset => ParentPlayfield.VisualOffset.Value;

    internal float NoteHeightScale => ParentPlayfield.Stage.NoteHeightScale;

    protected BmsPlayfield ParentPlayfield { get; }

    private const float key_area_under_notes_depth = 1;
    private const float key_area_over_notes_depth = -1;

    private BmsColumnKeySound? keySound;
    private readonly BmsHitExplosionPool normalHitExplosionPool;
    private readonly BmsHitExplosionPool longNoteHitExplosionPool;
    private readonly List<(DrawableBmsHitObject Drawable, BmsJudgementCandidate Candidate)> pressCandidates = [];
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsColumn(int index, BmsPlayfield playfield)
    {
        ParentPlayfield = playfield;
        Index = index;
        LayoutVariant = playfield.LayoutVariant;
        IsScratch = BmsLayout.IsScratchColumn(index, LayoutVariant);

        RelativeSizeAxes = Axes.Y;
        Width = defaultColumnWidth(index, LayoutVariant);
        HitObjectContainer.Depth = HIT_OBJECT_DEPTH;

        normalHitExplosionPool = new BmsHitExplosionPool(
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, LayoutVariant, Index), 2);
        longNoteHitExplosionPool = new BmsHitExplosionPool(
            // Head, first hold pulse, and tail can overlap within the explosion fade lifetime.
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, LayoutVariant, Index, true), 3);

        InternalChildren =
        [
            normalHitExplosionPool,
            longNoteHitExplosionPool,
            KeyAreaUnderNotesLayer,
        ];

        KeyArea = new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, LayoutVariant, index))
        {
            RelativeSizeAxes = Axes.Y,
            CentreComponent = false,
        };
    }

    #region Disposal

    protected override void Dispose(bool isDisposing)
    {
        NewResult -= onColumnNewResult;
        base.Dispose(isDisposing);

        if (skin.IsNotNull())
            skin.SourceChanged -= updateFromSkin;
    }

    #endregion

    public static BmsColumn Create(int index, BmsPlayfield playfield)
    {
        var providerType = BmsColumnFactory.GetColumnProviderType(index);
        var genericType = typeof(BmsColumnGeneric<>).MakeGenericType(providerType);
        return (BmsColumn)Activator.CreateInstance(genericType, index, playfield)!;
    }

    internal static float DepthForKeyArea(bool keysUnderNotes) =>
        keysUnderNotes ? key_area_under_notes_depth : key_area_over_notes_depth;

    protected override HitObjectContainer CreateHitObjectContainer()
        => new BmsColumnHitObjectContainer(
            ParentPlayfield.ScrollController,
            () => HitTargetPosition,
            () => ParentPlayfield.IsResumeRewinding,
            () => ParentPlayfield.ResumeRewindEndTime);

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject)
        => new BmsHitObjectLifetimeEntry(
            hitObject,
            ParentPlayfield.ScrollController,
            () => ParentPlayfield.VisualOffset.Value);

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // Build the keysound cursor from this column's own hit-object slice so the per-column
        // player only ever scans notes it actually owns. The slice mirrors the playfield's global
        // ordering ( StartTime, then Column ) so cursor reset/seek behaviour stays consistent.
        var columnHitObjects = ParentPlayfield.Beatmap.HitObjects
            .Where(h => h.Column == Index)
            .OrderBy(h => h.StartTime)
            .ThenBy(h => h.Column)
            .ToArray();

        keySound = new BmsColumnKeySound(columnHitObjects, HitObjectContainer);
        AddInternal(keySound);

        NewResult += onColumnNewResult;
    }

    protected override void Update()
    {
        // Each column owns its result stack, so it must suppress the framework's rewind reversion too.
        if (!ParentPlayfield.IsResumeRewinding)
            base.Update();
    }

    private static float defaultColumnWidth(int index, BmsLayoutVariant layoutVariant) =>
        BmsLayout.IsScratchColumn(index, layoutVariant) ? SCRATCH_COLUMN_WIDTH : COLUMN_WIDTH;

    [BackgroundDependencyLoader]
    private void load()
    {
        skin.SourceChanged += updateFromSkin;
        updateFromSkin();
    }

    private void updateFromSkin()
    {
        if (Hidden)
            return;

        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, LayoutVariant, Index);
        Width = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnWidth, lookup))?.Value
                ?? defaultColumnWidth(Index, LayoutVariant);

        // For 2P/scratch-on-right, ColumnSpacing indices must be remapped to follow
        // visual column order [keys…, scratch] rather than BMS index order.
        int? spacingLeftCol;
        int? spacingRightCol;

        if (BmsLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int colIdx)
        {
            var totalCols = BmsLayout.GetTotalColumns(LayoutVariant);
            (spacingLeftCol, spacingRightCol) = BmsLayout.RemapColum2PGapIdx(colIdx, totalCols);
        }
        else
        {
            spacingLeftCol = lookup.ColumnIndex;
            spacingRightCol = lookup.ColumnIndex;
        }

        var spacingLookupLeft = spacingLeftCol != null
            ? new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, LayoutVariant, spacingLeftCol.Value)
            : null;

        var spacingLookupRight = spacingRightCol != null
            ? new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, LayoutVariant, spacingRightCol.Value)
            : null;

        Margin = new MarginPadding
        {
            Left = spacingLookupLeft != null
                ? skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.LeftColumnSpacing, spacingLookupLeft))?.Value ?? 0
                : 0,
            Right = spacingLookupRight != null
                ? skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.RightColumnSpacing, spacingLookupRight))?.Value ?? 0
                : 0,
        };

    }

    #region Hit explosions / landmine

    /// <summary>
    /// Spawns a hit explosion (hit light) in this column. Used for note hits (via this column's
    /// own NewResult), LN head/tail endpoints, and the repeating hit light fired throughout an
    /// LN hold. Moved here from BmsPlayfield so the column owns its lane's hit-light visuals.
    /// </summary>
    public void TriggerHitExplosion(bool isLongNote)
    {
        var pool = isLongNote ? longNoteHitExplosionPool : normalHitExplosionPool;
        HitExplosionArea.Add(pool.Get(explosion => explosion.ApplyPositionOffset(ParentPlayfield.Stage.HitTargetPositionOffset)));
    }

    private sealed partial class BmsHitExplosionPool(BmsSkinComponentLookup lookup, int initialSize)
        : DrawablePool<BmsHitExplosion>(initialSize)
    {
        protected override BmsHitExplosion CreateNewDrawable() => new(lookup);
    }

    /// <summary>
    /// Triggers a note-hit / LN-tail definition key. All columns and automation route through the
    /// shared key Track, so retriggering the definition has the same truncation behaviour.
    /// </summary>
    public void PlaySample(ushort? sampleKey, int volume) => keySound?.PlaySample(sampleKey, volume);

    /// <summary>
    /// Plays the landmine explosion sample (#WAV00) when a landmine in this column detonates.
    /// </summary>
    public void DetonateLandmine(BmsHitObject hitObject)
    {
        if (!hitObject.Beatmap.SampleDefinitions.ContainsKey(0))
            return;

        keySound?.PlayLandmineSound(hitObject.SampleVolume);
    }

    private void onColumnNewResult(DrawableHitObject drawable, JudgementResult result)
    {
        if (drawable is not DrawableBmsHitObject bmsHitObject)
            return;

        // Landmines detonate via their own path (DrawableBmsLandmine + DetonateLandmine); no hit light.
        if (bmsHitObject.HitObject is BmsLandmine)
            return;

        // BMS POOR is represented by framework Meh, which IsHit() considers successful even though
        // it must not produce the hit feedback reserved for BAD and better judgements.
        if (ShouldTriggerHitExplosion(result.Type))
            TriggerHitExplosion(bmsHitObject.HitObject is BmsLongNote);
    }

    internal static bool ShouldTriggerHitExplosion(HitResult result) => result != HitResult.Meh && result.IsHit();

    #endregion

    #region Input

    public bool IsPressed { get; private set; }

    public PressOutcome HandlePress(double time)
    {
        IsPressed = true;

        pressCandidates.Clear();
        pressJudgementCandidates.Clear();

        foreach (var alive in HitObjectContainer.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d
                || d.Judged
                || d.HitObject is BmsLandmine)
            {
                continue;
            }

            var candidate = new BmsJudgementCandidate(
                d.HitObject.StartTime,
                d.HitObject.GetEndTime(),
                d.HitObject.Column,
                d.HitObject.EffectiveJudgementRate,
                d.HitObject is BmsLongNote);
            pressCandidates.Add((d, candidate));
            pressJudgementCandidates.Add(candidate);
        }

        var selection = BmsJudgementSelector.SelectPress(LayoutVariant, Index, pressJudgementCandidates, time);

        if (!selection.IsEmptyPoor && selection.Candidate is { } selectedCandidate)
        {
            DrawableBmsHitObject? target = null;
            foreach (var candidate in pressCandidates)
            {
                if (candidate.Candidate.Equals(selectedCandidate))
                {
                    target = candidate.Drawable;
                    break;
                }
            }

            if (target == null)
                return PressOutcome.Empty;

            if (target.TryHit(selection.Result))
            {
                keySound?.PlaySample(target.HitObject.SampleKey, target.HitObject.SampleVolume);
                return PressOutcome.Hit;
            }
        }

        keySound?.PlayKeySound();

        return selection is { IsEmptyPoor: true, Candidate: { } emptyPoorCandidate }
            ? PressOutcome.ForEmptyPoor(emptyPoorCandidate.StartTime, emptyPoorCandidate.Column)
            : PressOutcome.Empty;
    }

    public void HandleRelease(double time)
    {
        if (!IsPressed)
            return;

        IsPressed = false;

        // Release: find the earliest held LN in this column and let it judge the key-up.
        // We must include LNs released before the tail window (a fast release is a drop,
        // scored as POOR) — filtering by the release window here would leave the note
        // frozen at the judgement line until its tail time passed.
        DrawableBmsHitObject? heldNote = null;

        foreach (var alive in HitObjectContainer.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d) continue;
            if (d is not ILongNoteHolder ln || !ln.IsHoldingLongNote)
                continue;

            if (heldNote == null || d.HitObject.GetEndTime() < heldNote.HitObject.GetEndTime())
                heldNote = d;
        }

        if (heldNote is ILongNoteHolder ln2)
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, heldNote.HitObject.EffectiveJudgementRate, tail: true);
            var releaseOffset = time - heldNote.HitObject.GetEndTime();

            if (ln2.TryRelease(releaseOffset, tailTable) && heldNote.HitObject is BmsLongNote ln)
                keySound?.PlaySample(ln.TailSampleKey, ln.TailSampleVolume);
        }
    }

    #endregion

}
