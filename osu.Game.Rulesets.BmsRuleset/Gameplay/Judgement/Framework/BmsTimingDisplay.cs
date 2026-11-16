using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System;
using RhythmEngine.Profiles.Bms;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;

/// <summary>osu! uses millisecond windows and framework result identities.</summary>
internal static class BmsTimingDisplay
{
    internal static double FrameworkWindowFor(this BmsTimingDefinition timing, HitResult result)
        => timing.WindowFor(BmsGameplayProfile.ResultIdFor(result)) is { } window ? Math.Min(Math.Abs(window.Early), Math.Abs(window.Late)) / 1000.0 : 0;

    internal static double FastWindowFor(this BmsTimingDefinition timing, HitResult result)
        => -(timing.WindowFor(BmsGameplayProfile.ResultIdFor(result))?.Early ?? 0) / 1000.0;

    internal static double SlowWindowFor(this BmsTimingDefinition timing, HitResult result)
        => (timing.WindowFor(BmsGameplayProfile.ResultIdFor(result))?.Late ?? 0) / 1000.0;
}
