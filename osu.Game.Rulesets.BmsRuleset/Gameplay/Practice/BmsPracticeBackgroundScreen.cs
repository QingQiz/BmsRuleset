using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Screens.Backgrounds;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

internal partial class BmsPracticeBackgroundScreen(WorkingBeatmap beatmap) : BackgroundScreenBeatmap(beatmap)
{
    private DimmableBackground dimmable = null!;

    protected override DimmableBackground CreateFadeContainer() => dimmable = new DimmableBackground { RelativeSizeAxes = Axes.Both };

    internal void BindVisualBreakTime(IBindable<bool> source)
    {
        // Player's native break state includes completed attempts and preroll, which would brighten every repeat.
        var breakTime = (Bindable<bool>)dimmable.IsBreakTime;
        breakTime.UnbindBindings();
        ((IBindable<bool>)breakTime).BindTo(source);
    }
}
