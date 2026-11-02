using System;
using osu.Framework.Bindables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public partial class BmsDrawableRuleset
{
    internal bool UpdatePracticePreparation()
    {
        if (practiceSession == null || gameplayClockContainer?.IsPaused.Value != true
                                    || FrameStableClock is not FrameStabilityContainer stability)
            throw new InvalidOperationException("Practice preparation requires a stopped gameplay clock.");

        // Native frame stability skips its entire subtree while paused. Let that subtree finish
        // loading at the fixed preroll time while input, audio and the source clock remain paused.
        var paused = (Bindable<bool>)stability.IsPaused;
        paused.UnbindFrom(gameplayClockContainer.IsPaused);
        try
        {
            paused.Value = false;
            UpdateSubTree();
        }
        finally
        {
            ((IBindable<bool>)paused).BindTo(gameplayClockContainer.IsPaused);
        }

        return Math.Abs(stability.CurrentTime - gameplayClockContainer.CurrentTime) < 0.001;
    }
}
