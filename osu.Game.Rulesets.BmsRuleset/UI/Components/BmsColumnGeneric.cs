using osu.Framework.Allocation;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.UI.Components;

public sealed partial class BmsColumnGeneric<TCol>(int index, BmsPlayfield playfield)
    : BmsColumn(index, playfield)
    where TCol : struct, IColumnProvider
{

    [BackgroundDependencyLoader]
    private void load()
    {
        RegisterPool<BmsNote, DrawableBmsNote<TCol>>(64, int.MaxValue);
        RegisterPool<BmsLongNote, DrawableBmsLongNote<TCol>>(32, int.MaxValue);
        RegisterPool<BmsLandmine, DrawableBmsLandmine<TCol>>(32, int.MaxValue);
    }
}
