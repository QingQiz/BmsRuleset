using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.UI;
using osu.Game.Skinning;
using osuTK.Graphics;

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
    public Container HitExplosionArea { get; } = new() { RelativeSizeAxes = Axes.Y, Masking = true };

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

    protected BmsPlayfield ParentPlayfield { get; }

    private readonly BmsLayoutVariant layoutVariant;
    private readonly SkinnableDrawable hitTarget;

    private BmsColumnKeySound? keySound;

    [Resolved]
    private ISkinSource skin { get; set; } = null!;

    public BmsColumn(int index, BmsPlayfield playfield)
    {
        ParentPlayfield = playfield;
        Index = index;
        layoutVariant = playfield.LayoutVariant;
        IsScratch = BmsLayout.IsScratchColumn(index, layoutVariant);

        RelativeSizeAxes = Axes.Y;
        Width = defaultColumnWidth(index, layoutVariant);
        Masking = true;
        BorderThickness = 0;

        InternalChildren =
        [
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, index), _ => new DefaultBmsColumnBackground(index, IsScratch))
            {
                RelativeSizeAxes = Axes.Both,
            },
            new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, layoutVariant, index), _ => new DefaultBmsKeyArea(index, layoutVariant, IsScratch))
            {
                RelativeSizeAxes = Axes.Both,
                CentreComponent = false,
            },
            hitTarget = new SkinnableDrawable(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, layoutVariant, index), _ => new DefaultBmsHitTarget(IsScratch))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.Centre,
                CentreComponent = false,
            },
        ];
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
        => new BmsColumnHitObjectContainer(ParentPlayfield);

    protected override HitObjectLifetimeEntry CreateLifetimeEntry(HitObject hitObject)
        => new BmsHitObjectLifetimeEntry(hitObject, ParentPlayfield);

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

        var lookup = new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, Index);
        Width = skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ColumnWidth, lookup))?.Value
                ?? defaultColumnWidth(Index, layoutVariant);

        // For 2P/scratch-on-right, ColumnSpacing indices must be remapped to follow
        // visual column order [keys…, scratch] rather than BMS index order.
        int? spacingLeftCol;
        int? spacingRightCol;

        if (BmsLayout.Is2P(lookup.LayoutVariant) && lookup.ColumnIndex is int colIdx)
        {
            var totalCols = BmsLayout.GetTotalColumns(layoutVariant);
            (spacingLeftCol, spacingRightCol) = BmsLayout.RemapColum2PGapIdx(colIdx, totalCols);
        }
        else
        {
            spacingLeftCol = lookup.ColumnIndex;
            spacingRightCol = lookup.ColumnIndex;
        }

        var spacingLookupLeft = spacingLeftCol != null
            ? new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, spacingLeftCol.Value)
            : null;

        var spacingLookupRight = spacingRightCol != null
            ? new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, layoutVariant, spacingRightCol.Value)
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

        hitTarget.Y = -(skin.GetConfig<BmsSkinConfigurationLookup, float>(new BmsSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition))?.Value
                        ?? BmsStage.HIT_TARGET_POSITION);
    }

    #region Hit explosions / landmine

    /// <summary>
    /// Number of hit explosions fired by LN hold pulses in this column. Exposed for tests.
    /// </summary>
    public int HoldExplosionCount { get; private set; }

    /// <summary>
    /// Spawns a hit explosion (hit light) in this column. Used for note hits (via this column's
    /// own NewResult), LN head/tail endpoints, and the repeating hit light fired throughout an
    /// LN hold. Moved here from BmsPlayfield so the column owns its lane's hit-light visuals.
    /// </summary>
    public void TriggerHitExplosion(bool isLongNote, bool isHold = false)
    {
        if (isHold)
            HoldExplosionCount++;

        HitExplosionArea.Add(new BmsHitExplosion(new BmsSkinComponentLookup(
            BmsSkinComponents.HitExplosion,
            layoutVariant,
            Index,
            isLongNote)));
    }

    /// <summary>
    /// Plays a note-hit / LN-tail sample in this column's key channel. Routed to the column's own
    /// keysound player so mods (e.g. AutoScratch) hit the same channel a real press would.
    /// </summary>
    public void PlaySample(string samplePath) => keySound?.PlaySample(samplePath);

    /// <summary>
    /// Plays the landmine explosion sample (#WAV00) when a landmine in this column detonates.
    /// </summary>
    public void DetonateLandmine(BmsHitObject hitObject)
    {
        var explosionPath = hitObject.Beatmap.SampleDefinitions.TryGetValue(0, out var p) ? p : string.Empty;
        if (string.IsNullOrEmpty(explosionPath))
            return;

        keySound?.PlayLandmineSound(explosionPath);
    }

    private void onColumnNewResult(DrawableHitObject drawable, JudgementResult result)
    {
        if (drawable is not DrawableBmsHitObject bmsHitObject)
            return;

        // Landmines detonate via their own path (DrawableBmsLandmine + DetonateLandmine); no hit light.
        if (bmsHitObject.HitObject is BmsLandmine)
            return;

        if (result.IsHit)
            TriggerHitExplosion(bmsHitObject.HitObject is BmsLongNote);
    }

    #endregion

    #region Input

    public bool IsPressed { get; private set; }

    public PressOutcome HandlePress(double time)
    {
        IsPressed = true;

        var candidates = new List<(DrawableBmsHitObject Drawable, BmsJudgementCandidate Candidate)>();

        foreach (var alive in HitObjectContainer.AliveEntries.Values)
        {
            if (alive is not DrawableBmsHitObject d
                || d.Judged
                || d.HitObject is BmsLandmine)
            {
                continue;
            }

            candidates.Add((d, new BmsJudgementCandidate(
                d.HitObject.StartTime,
                d.HitObject.GetEndTime(),
                d.HitObject.Column,
                d.HitObject.Beatmap.Rank,
                d.HitObject is BmsLongNote)));
        }

        var selection = BmsJudgementSelector.SelectPress(layoutVariant, Index, candidates.Select(c => c.Candidate), time);

        if (selection.Candidate is { } selectedCandidate)
        {
            var target = candidates.First(c => c.Candidate.Equals(selectedCandidate)).Drawable;
            if (target.TryHit(selection.Result))
            {
                keySound?.PlaySample(target.HitObject.SamplePath);
                return PressOutcome.Hit;
            }
        }

        keySound?.PlayKeySound();

        return selection.IsEmptyPoor ? PressOutcome.EmptyPoor : PressOutcome.Empty;
    }

    public void HandleRelease(double time)
    {
        if (!IsPressed)
            return;

        IsPressed = false;

        // Release: find the earliest held LN in this column and let it judge the key-up.
        // We must include LNs released before the tail window (an early release is a drop,
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
            var tailTable = BmsJudgementProfileProvider.GetTable(layoutVariant, Index, heldNote.HitObject.Beatmap.Rank, tail: true);
            var releaseOffset = time - heldNote.HitObject.GetEndTime();

            if (ln2.TryRelease(releaseOffset, tailTable) && heldNote.HitObject is BmsLongNote ln)
                keySound?.PlaySample(ln.TailSamplePath);
        }
    }

    #endregion

    #region components

    private partial class DefaultBmsColumnBackground(int index, bool isScratch) : CompositeDrawable
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = isScratch
                    ? Color4.DarkSlateBlue.Opacity(0.26f)
                    : index % 2 == 0
                        ? Color4.Black.Opacity(0.28f)
                        : Color4.White.Opacity(0.05f),
            };
        }
    }

    private sealed partial class DefaultBmsHitTarget : CompositeDrawable
    {
        private readonly bool isScratch;

        public DefaultBmsHitTarget(bool isScratch)
        {
            this.isScratch = isScratch;
            Height = isScratch ? 5 : 3;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Color4.White.Opacity(isScratch ? 0.85f : 0.65f),
            };
        }
    }

    /// <inheritdoc cref="CompositeDrawable" />
    /// <summary>
    ///     Default code-drawn key area that shows at the bottom of each column and
    ///     brightens briefly when the bound key is pressed.
    /// </summary>
    private sealed partial class DefaultBmsKeyArea(int columnIndex, BmsLayoutVariant layoutVariant, bool isScratch)
        : CompositeDrawable, IKeyBindingHandler<BmsAction>
    {

        private Box light = null!;

        public bool OnPressed(KeyBindingPressEvent<BmsAction> e)
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(e.Action, layoutVariant) != columnIndex)
                return false;

            light.FadeIn(10);
            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<BmsAction> e)
        {
            if (BmsKeyBindingConfiguration.ActionToColumn(e.Action, layoutVariant) != columnIndex)
                return;

            light.FadeOut(120);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Anchor = Anchor.BottomCentre;
            Origin = Anchor.BottomCentre;

            InternalChildren =
            [
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 60,
                    Colour = isScratch ? Color4.DarkSlateBlue.Opacity(0.55f) : Color4.White.Opacity(0.08f),
                },
                light = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 60,
                    Colour = Color4.White.Opacity(0.45f),
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
            ];
        }
    }

    #endregion

}
