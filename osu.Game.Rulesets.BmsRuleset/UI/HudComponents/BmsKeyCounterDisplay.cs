using osu.Framework.Bindables;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;

namespace osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

internal sealed partial class BmsKeyCounterDisplay : ArgonKeyCounterDisplay
{
    protected override KeyCounter CreateCounter(InputTrigger trigger) => new BmsKeyCounter(trigger);
}

internal sealed partial class BmsKeyCounter : ArgonKeyCounter
{
    private readonly BindableBool active = new();
    private bool activated;
    private bool releasePending;
    private bool releaseForwardPlayback;
    private double eventTime = double.NaN;

    public BmsKeyCounter(InputTrigger trigger)
        : base(trigger)
    {
        active.BindTarget = IsActive;
    }

    protected override void Activate(bool forwardPlayback = true)
    {
        prepareEvent();
        // At the same HUD time, repeated presses replace identical transforms. Keeping
        // the first also preserves the indicator's immediate position reset.
        if (!activated)
            base.Activate(forwardPlayback);
        else
            active.Value = true;

        activated = true;
        releasePending = false;
    }

    protected override void Deactivate(bool forwardPlayback = true)
    {
        prepareEvent();
        active.Value = false;
        releasePending = true;
        releaseForwardPlayback = forwardPlayback;
    }

    public override bool UpdateSubTree()
    {
        flushRelease();
        return base.UpdateSubTree();
    }

    private void prepareEvent()
    {
        var now = TransformStartTime;
        if (eventTime == now)
            return;

        flushRelease();
        eventTime = now;
    }

    private void flushRelease()
    {
        activated = false;
        if (!releasePending)
            return;

        releasePending = false;
        if (eventTime == TransformStartTime)
            base.Deactivate(releaseForwardPlayback);
        else
        {
            // A hidden HUD can resume later; its animation still starts at input time.
            using (BeginAbsoluteSequence(eventTime))
                base.Deactivate(releaseForwardPlayback);
        }
    }
}
