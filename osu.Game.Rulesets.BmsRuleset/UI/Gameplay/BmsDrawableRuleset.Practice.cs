using System;
using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice.Audio;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public partial class BmsDrawableRuleset
{
    private static BmsGameplayAudioController createGameplayAudioController(BmsBeatmap beatmap, IReadOnlyList<Mod> mods)
        => new(beatmap, mods, beatmap.PracticeEndTime.HasValue ? new BmsPracticeSamplePlayback(beatmap, mods) : null);

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
