using osu.Game.Graphics;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

internal static class BmsHitResultColours
{
    private static readonly OsuColour colours = new();

    public static Color4 ForHitResult(HitResult result) => result switch
    {
        HitResult.Good => colours.Green,
        HitResult.Ok => colours.Yellow,
        HitResult.Meh => colours.Red,
        HitResult.Miss => Color4.Gray,
        _ => colours.ForHitResult(result),
    };

    public static Color4 ForScore(ScoreInfo score, HitResult result) =>
        score.Ruleset.ShortName == "bms" ? ForHitResult(result) : colours.ForHitResult(result);
}
