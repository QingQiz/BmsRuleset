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
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

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

    internal bool PreserveLongNoteHistory => ParentPlayfield.PreserveLongNoteHistory;

    protected BmsPlayfield ParentPlayfield { get; }

    internal float GetLongNoteHeadYAtStartTime(BmsLongNote note)
    {
        var scroll = ParentPlayfield.ScrollController;
        var displayTime = note.StartTime + VisualOffset * scroll.PlaybackRate;
        var displayPosition = scroll.ConstantScrollActive
            ? displayTime
            : scroll.TimingMap?.GetScrollPositionAtTime(displayTime) ?? displayTime;
        var headPosition = scroll.GetVisualScrollPosition(note.StartTime, note.ScrollPositionAtStartTime);
        return -(HitTargetPosition + (float)((headPosition - displayPosition)
                                            * scroll.ScrollCoordinateScale(HitObjectContainer.DrawHeight, HitTargetPosition)));
    }

    private BmsColumnKeySound? keySound;
    private readonly BmsNotePressHistory pressHistory = new();
    private readonly BmsHitObject[] columnNotes;

    internal bool HasPmsMistake(BmsHitObject note) => BmsJudgementProfileProvider.IsPms(LayoutVariant) && pressHistory.Get(note).Mistake;

    private readonly bool hasLongNotes;
    private readonly bool canBatchUpdates;
    private readonly BmsHitExplosionPool normalHitExplosionPool;
    private readonly BmsHitExplosionPool longNoteHitExplosionPool;
    private readonly List<(DrawableBmsHitObject Drawable, BmsJudgementCandidate Candidate)> pressCandidates = [];
    private readonly List<BmsJudgementCandidate> pressJudgementCandidates = [];

    internal BmsHitObjectPoolPlan.ColumnSizes InitialPoolSizes => ParentPlayfield.InitialPoolSizes[Index];

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsColumn(int index, BmsPlayfield playfield)
    {
        ParentPlayfield = playfield;
        Index = index;
        LayoutVariant = playfield.LayoutVariant;
        IsScratch = BmsLayout.IsScratchColumn(index, LayoutVariant);
        columnNotes = playfield.Beatmap.HitObjects.Where(h => h.Column == index && h is not BmsLandmine and not BmsInvisibleNote).OrderBy(h => h.StartTime).ToArray();
        hasLongNotes = playfield.Beatmap.HitObjects.Any(h => h.Column == index && h is BmsLongNote);
        canBatchUpdates = playfield.Beatmap.HitObjects.All(h => h.Column != index || h is BmsNote or BmsLongNote);

        RelativeSizeAxes = Axes.Y;
        Width = defaultColumnWidth(index, LayoutVariant);
        HitObjectContainer.Depth = HIT_OBJECT_DEPTH;

        normalHitExplosionPool = new BmsHitExplosionPool(
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, LayoutVariant, Index), playfield.InitialHitExplosionSizes[index]);
        longNoteHitExplosionPool = new BmsHitExplosionPool(
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, LayoutVariant, Index, true), playfield.InitialLongNoteHitExplosionSizes[index]);

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

    protected override HitObjectContainer CreateHitObjectContainer()
        => new BmsColumnHitObjectContainer(
            ParentPlayfield.ScrollController,
            () => HitTargetPosition,
            () => ParentPlayfield.IsResumeRewinding,
            () => ParentPlayfield.ResumeRewindEndTime,
            canBatchUpdates);

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject)
        => new BmsHitObjectLifetimeEntry(
            hitObject,
            ParentPlayfield.ScrollController,
            () => ParentPlayfield.VisualOffset.Value,
            canBatchUpdates ? BmsColumnHitObjectContainer.MAX_DEFERRED_UPDATE_TIME : 0);

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

        var invisibleNotes = ParentPlayfield.Beatmap.InvisibleNotes
            .Where(n => n.Column == Index).OrderBy(n => n.StartTime).ToArray();
        foreach (var note in invisibleNotes)
            Add(note);

        if (invisibleNotes.Length > 0)
            ((BmsColumnHitObjectContainer)HitObjectContainer).RefreshAllEntries();

        keySound = new BmsColumnKeySound(columnHitObjects, HitObjectContainer, invisibleNotes);
        AddInternal(keySound);

        NewResult += onColumnNewResult;
    }

    protected override void Update()
    {
        // Each column owns its result stack, so it must suppress the framework's rewind reversion too.
        if (!ParentPlayfield.IsResumeRewinding)
        {
            if (Time.Elapsed < 0)
                pressHistory.Rewind(Time.Current);
            base.Update();
        }
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
        var explosion = pool.Get();
        explosion.ApplyPositionOffset(ParentPlayfield.Stage.HitTargetPositionOffset);
        HitExplosionArea.Add(explosion);
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

        pressHistory.Record(bmsHitObject.HitObject, Time.Current, true);

        // BMS POOR is represented by framework Meh, which IsHit() considers successful even though
        // it must not produce the hit feedback reserved for BAD and better judgements.
        if (ShouldTriggerHitExplosion(result.Type))
            TriggerHitExplosion(bmsHitObject.HitObject is BmsLongNote);
    }

    internal static bool ShouldTriggerHitExplosion(HitResult result) => result != HitResult.Meh && result.IsHit();

    #endregion

    #region Input

    public bool IsPressed => pressedDirections != 0;

    private int pressedDirections;

    internal int PressedDirections
    {
        get => pressedDirections;
        set => pressedDirections = value;
    }

    internal bool LastPressWasReverseScratch { get; private set; }

    public PressOutcome HandlePress(double time, bool reverseScratch = false)
    {
        LastPressWasReverseScratch = IsScratch && reverseScratch;
        pressedDirections |= LastPressWasReverseScratch ? 2 : 1;

        // Replay input also changes while moving backwards; retain key state without judging it.
        if ((Clock as IGameplayClock)?.IsRewinding == true || Time.Elapsed < 0)
            return PressOutcome.Empty;

        if (hasLongNotes && ((BmsColumnHitObjectContainer)HitObjectContainer).TryRepress(time, LastPressWasReverseScratch) is { } repressed)
        {
            if (repressed is ILongNoteHolder { IsHoldingLongNote: false } && repressed.HitObject is BmsLongNote held)
                keySound?.PlaySample(held.TailSampleKey, held.TailSampleVolume);
            return PressOutcome.Hit;
        }

        // The earliest pending exact-time head wins every judgement algorithm. The ordered
        // live index avoids sorting thousands of future candidates for every replay press.
        if (((BmsColumnHitObjectContainer)HitObjectContainer).FirstPendingHead is { } first
            && first.HitObject.StartTime == time && first.HasPendingHead
            && BmsJudgementProfileProvider.GetTable(LayoutVariant, Index, first.HitObject.EffectiveJudgementRate, false).ResultForOffset(0) == HitResult.Perfect
            && first.TryHit(HitResult.Perfect))
        {
            pressHistory.Record(first.HitObject, time, true);
            keySound?.PlaySample(first.HitObject.SampleKey, first.HitObject.SampleVolume);
            return PressOutcome.Hit;
        }

        pressCandidates.Clear();
        pressJudgementCandidates.Clear();

        foreach (var alive in HitObjectContainer.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d
                || d.Judged
                || d.HitObject is BmsLandmine or BmsInvisibleNote)
            {
                continue;
            }

            var candidate = new BmsJudgementCandidate(
                d.HitObject.StartTime,
                d.HitObject.GetEndTime(),
                d.HitObject.Column,
                d.HitObject.EffectiveJudgementRate,
                d.HitObject is BmsLongNote,
                !d.HasPendingHead,
                pressHistory.Get(d.HitObject).Mistake);
            pressCandidates.Add((d, candidate));
            pressJudgementCandidates.Add(candidate);
        }

        // Consumed notes may already be pooled. Search the chart's fixed empty-POOR interval
        // rather than retaining drawables or scanning the complete judgement history.
        var low = 0;
        var high = columnNotes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (columnNotes[middle].StartTime < time - 175)
                low = middle + 1;
            else
                high = middle;
        }

        for (var i = low; i < columnNotes.Length && columnNotes[i].StartTime <= time + 500; i++)
        {
            var note = columnNotes[i];
            var state = pressHistory.Get(note);
            if (state.Judged)
                pressJudgementCandidates.Add(new BmsJudgementCandidate(note.StartTime, note.GetEndTime(), note.Column,
                    note.EffectiveJudgementRate, note is BmsLongNote, true, state.Mistake));
        }

        var selection = BmsJudgementSelector.SelectPress(LayoutVariant, Index, pressJudgementCandidates, time, ParentPlayfield.JudgementAlgorithm);

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

            if (selection.Result == HitResult.Ok && BmsJudgementProfileProvider.IsPms(LayoutVariant))
            {
                pressHistory.Record(target.HitObject, time, false, true);
                ParentPlayfield.RegisterNonConsumingBad(target.HitObject.StartTime, Index);
                keySound?.PlaySample(target.HitObject.SampleKey, target.HitObject.SampleVolume);
                return PressOutcome.Hit;
            }

            if (target.TryHit(selection.Result))
            {
                pressHistory.Record(target.HitObject, time, true);
                keySound?.PlaySample(target.HitObject.SampleKey, target.HitObject.SampleVolume);
                return PressOutcome.Hit;
            }
        }

        keySound?.PlayKeySound();

        if (selection is { IsEmptyPoor: true, Candidate: { } emptyPoorCandidate })
        {
            foreach (var candidate in pressCandidates)
            {
                if (candidate.Candidate.Equals(emptyPoorCandidate))
                    pressHistory.Record(candidate.Drawable.HitObject, time, false, true);
            }

            return PressOutcome.ForEmptyPoor(emptyPoorCandidate.StartTime, emptyPoorCandidate.Column);
        }

        return PressOutcome.Empty;
    }

    public void HandleRelease(double time, bool reverseScratch = false)
    {
        var direction = IsScratch && reverseScratch ? 2 : 1;
        if ((pressedDirections & direction) == 0)
            return;

        pressedDirections &= ~direction;

        if ((Clock as IGameplayClock)?.IsRewinding == true || Time.Elapsed < 0)
            return;

        if (!hasLongNotes)
            return;

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

            if (ln2.TryRelease(releaseOffset, tailTable, reverseScratch) && heldNote.HitObject is BmsLongNote ln)
                keySound?.PlaySample(ln.TailSampleKey, ln.TailSampleVolume);
        }
    }

    #endregion

}
