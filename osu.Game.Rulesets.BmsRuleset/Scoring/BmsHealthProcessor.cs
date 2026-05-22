using System.Collections.Generic;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public partial class BmsHealthProcessor(double drainStartTime) : LegacyDrainingHealthProcessor(drainStartTime)
{
    protected override double ComputeDrainRate()
    {
        base.ComputeDrainRate();
        return 0;
    }

    protected override IEnumerable<HitObject> EnumerateTopLevelHitObjects() => Beatmap.HitObjects;

    protected override IEnumerable<HitObject> EnumerateNestedHitObjects(HitObject hitObject) => hitObject.NestedHitObjects;

    protected override double GetHealthIncreaseFor(HitObject hitObject, HitResult result)
    {
        switch (result)
        {
            case HitResult.Miss:
                // Long-note head/tail objects are not modelled as mania nested objects anymore.
                // Until native BMS LN nesting exists, every top-level miss uses one BMS miss cost.
                return -(Beatmap.Difficulty.DrainRate + 1) * 0.0075;

            case HitResult.Meh:
                return -(Beatmap.Difficulty.DrainRate + 1) * 0.0016;

            case HitResult.Ok:
                return 0;

            case HitResult.Good:
                return HpMultiplierNormal * (0.004 - Beatmap.Difficulty.DrainRate * 0.0004);

            case HitResult.Great:
                return HpMultiplierNormal * (0.005 - Beatmap.Difficulty.DrainRate * 0.0005);

            case HitResult.Perfect:
                return HpMultiplierNormal * (0.0055 - Beatmap.Difficulty.DrainRate * 0.0005);
        }

        return 0;
    }
}
