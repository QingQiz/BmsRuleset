using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

public sealed partial class DrawableBmsNote<TCol> : DrawableBmsHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override BmsSkinComponents SkinComponent => BmsSkinComponents.Note;

    public override bool TryHit()
    {
        if (Judged || HitObject?.HitWindows == null)
            return false;

        var bmsWindows = (BmsHitWindows)HitObject.HitWindows;
        var result = bmsWindows.BmsResultFor(Time.Current - HitObject.StartTime);

        if (result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject.HitWindows == null)
            return;

        if (timeOffset > HitObject.HitWindows.WindowFor(HitResult.Ok))
            ApplyResult(HitResult.Meh);
    }
}
