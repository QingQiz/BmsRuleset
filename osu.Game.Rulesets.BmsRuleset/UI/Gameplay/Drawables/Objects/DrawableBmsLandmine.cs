using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

public sealed partial class DrawableBmsLandmine<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{

    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Mine;

    // The playfield's passing phase owns detonation so it precedes every HCN body.
    protected override bool UsesPassiveResultCheck => false;

    protected override bool SkipFurtherUpdates => mineHandledTime.HasValue && Time.Current >= HitObject.StartTime;

    private double? mineHandledTime;

    internal override bool RequiresColumnFrameUpdate => false;

    protected override void ResetKindState() => mineHandledTime = (Entry as BmsHitObjectLifetimeEntry)?.LandmineHandledTime;

    internal override void RestoreRewoundState()
    {
        if (mineHandledTime.HasValue && Time.Current < mineHandledTime.Value)
        {
            // Processing can follow StartTime, so detonated and avoided mines must both
            // rewind at the actual frame, matching the host's RawTime result boundary.
            mineHandledTime = null;
            Alpha = 1;
        }
    }

    internal override void UpdateLandmine(bool holding)
    {
        if (Judged || mineHandledTime.HasValue || Time.Current < HitObject.StartTime)
            return;

        mineHandledTime = Time.Current;
        if (Entry is BmsHitObjectLifetimeEntry entry)
            entry.LandmineHandledTime = mineHandledTime;

        if (holding && Time.Current < HitObject.StartTime + BmsHitObjectLifetimeEntry.MINE_PAST_LIFETIME)
        {
            ParentColumn?.DetonateLandmine(HitObject);
            ApplyResult(HitResult.Meh);
            // Passing judgements now precede the column's lifetime pass. Keep the hidden
            // mine until its normal expiry so this same traversal cannot free its result.
            LifetimeEnd = HitObject.StartTime + BmsHitObjectLifetimeEntry.MINE_PAST_LIFETIME;
        }
        else
        {
            Alpha = 0;
        }
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
    }
}
