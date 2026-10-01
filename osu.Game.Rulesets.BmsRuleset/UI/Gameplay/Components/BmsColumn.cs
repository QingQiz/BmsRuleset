using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
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

    internal double? ResumeRewindEndTime => ParentPlayfield.IsResumeRewinding ? ParentPlayfield.ResumeRewindEndTime : null;

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
    internal BmsColumnJudgementContext Judgements { get; set; } = null!;
    internal BmsDrawableJudgementOutput JudgementOutput { get; set; } = null!;

    internal bool CanBatchUpdates => canBatchUpdates;

    private readonly bool canBatchUpdates;
    private readonly BmsHitExplosionPool normalHitExplosionPool;
    private readonly BmsHitExplosionPool longNoteHitExplosionPool;

    internal BmsHitObjectPoolPlan.ColumnSizes InitialPoolSizes => ParentPlayfield.InitialPoolSizes[Index];

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsColumn(int index, BmsPlayfield playfield)
    {
        ParentPlayfield = playfield;
        Index = index;
        LayoutVariant = playfield.LayoutVariant;
        IsScratch = BmsLayout.IsScratchColumn(index, LayoutVariant);
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
        if (JudgementOutput != null)
            NewResult -= JudgementOutput.OnFrameworkNewResult;
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

        keySound = new BmsColumnKeySound(columnHitObjects, Judgements, invisibleNotes);
        AddInternal(keySound);

        NewResult += JudgementOutput.OnFrameworkNewResult;
    }

    protected override void Update()
    {
        // Each column owns its result stack, so it must suppress the framework's rewind reversion too.
        if (!ParentPlayfield.IsResumeRewinding)
        {
            if (Time.Elapsed < 0)
                Judgements.RewindPressHistory(Time.Current);
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
    public void TriggerHitExplosion(BmsHitLightStyle style)
    {
        var pool = style == BmsHitLightStyle.Hold ? longNoteHitExplosionPool : normalHitExplosionPool;
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

    // Framework Meh counts as a hit, but BMS POOR has no successful hit feedback.
    internal static bool ShouldTriggerHitExplosion(HitResult result) => result != HitResult.Meh && result.IsHit();

    #endregion

    #region Input

    public bool IsPressed => Judgements.PressedDirections != 0;

    internal void PlayKeySound() => keySound?.PlayKeySound();

    #endregion

}
