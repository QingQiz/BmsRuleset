using RhythmEngine.Core;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;

/// <summary>The framework's symmetric view of an already compiled engine rule.</summary>
internal sealed class BmsHitWindows(TimingWindows windows, long deadline, GameplayProfile profile) : HitWindows
{
    public override bool IsHitResultAllowed(HitResult result)
        => result is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss;

    public override void SetDifficulty(double difficulty)
    {
    }

    public override double WindowFor(HitResult result) => profile.FrameworkWindowFor(windows, deadline, result);
}
