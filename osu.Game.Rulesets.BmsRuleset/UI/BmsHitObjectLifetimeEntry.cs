using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.UI;

/// <summary>
///     Custom <see cref="HitObjectLifetimeEntry" /> used by <see cref="BmsPlayfield" />
///     to give BMS hit objects enough pre-time (2.5 s) so they appear at the top of
///     the playfield before reaching the judgement line.
/// </summary>
internal sealed class BmsHitObjectLifetimeEntry(HitObject hitObject) : HitObjectLifetimeEntry(hitObject)
{
    protected override double InitialLifetimeOffset => 10000;
}
