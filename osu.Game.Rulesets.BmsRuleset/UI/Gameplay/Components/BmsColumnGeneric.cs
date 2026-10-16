using osu.Framework.Allocation;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

public sealed partial class BmsColumnGeneric<TCol>(int index, BmsPlayfield playfield)
    : BmsColumn(index, playfield)
    where TCol : struct, IColumnProvider
{

    [BackgroundDependencyLoader]
    private void load()
    {
        RegisterPool<BmsNote, DrawableBmsNote<TCol>>(InitialPoolSizes.Notes, int.MaxValue);
        RegisterPool<BmsLongNote, DrawableBmsLongNote<TCol>>(InitialPoolSizes.LongNotes, int.MaxValue);
        RegisterPool<BmsLandmine, DrawableBmsLandmine<TCol>>(InitialPoolSizes.Mines, int.MaxValue);
    }
}
