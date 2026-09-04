using osu.Framework.Graphics.Containers;

namespace osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;

internal partial class InputBlockedContainer : Container
{
    public override bool PropagatePositionalInputSubTree => false;
}