using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public static class BmsJudgementProfileProvider
{
    private static readonly double[] rank_rates = [0.25, 0.50, 0.75, 1.00, 1.25];

    public static BmsJudgementWindowTable GetTable(BmsLayoutVariant layout, int column, int rank, bool tail)
    {
        var profile = createProfile(layout, Math.Clamp(rank, 0, 4));
        var scratch = BmsLayout.IsScratchColumn(column, layout);

        return tail
            ? scratch
                ? profile.LongScratchTail
                : profile.LongNoteTail
            : scratch
                ? profile.Scratch
                : profile.Normal;
    }

    private static BmsJudgementProfile createProfile(BmsLayoutVariant layout, int rank)
        => layout switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => fiveKeys(rank),
            // ReSharper disable once RedundantSwitchExpressionArms
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => sevenKeys(rank),
            _ => sevenKeys(rank),
        };

    private static BmsJudgementProfile fiveKeys(int rank) => new(
        head(rank, (-20, 20), (-50, 50), (-100, 100), (-150, 150), (-150, 500)),
        head(rank, (-30, 30), (-60, 60), (-110, 110), (-160, 160), (-160, 500)),
        tail(rank, (-120, 120), (-150, 150), (-200, 200), (-250, 250)),
        tail(rank, (-130, 130), (-160, 160), (-110, 110), (-260, 260)));

    private static BmsJudgementProfile sevenKeys(int rank) => new(
        head(rank, (-20, 20), (-60, 60), (-150, 150), (-280, 220), (-150, 500)),
        head(rank, (-30, 30), (-70, 70), (-160, 160), (-290, 230), (-160, 500)),
        tail(rank, (-120, 120), (-160, 160), (-200, 200), (-280, 220)),
        tail(rank, (-130, 130), (-170, 170), (-210, 210), (-290, 230)));

    private static BmsJudgementWindowTable head(
        int rank,
        (double late, double early) perfect,
        (double late, double early) great,
        (double late, double early) good,
        (double late, double early) bad,
        (double late, double early) miss)
    {
        var rate = rank_rates[rank];
        return new BmsJudgementWindowTable([
            scaled(HitResult.Perfect, perfect, rate),
            scaled(HitResult.Great, great, rate),
            scaled(HitResult.Good, good, rate),
            fixedWindow(HitResult.Ok, bad),
            fixedWindow(HitResult.Miss, miss),
        ]);
    }

    private static BmsJudgementWindowTable tail(
        int rank,
        (double late, double early) perfect,
        (double late, double early) great,
        (double late, double early) good,
        (double late, double early) bad)
    {
        var rate = rank_rates[rank];
        return new BmsJudgementWindowTable([
            scaled(HitResult.Perfect, perfect, rate),
            scaled(HitResult.Great, great, rate),
            scaled(HitResult.Good, good, rate),
            fixedWindow(HitResult.Ok, bad),
        ]);
    }

    private static BmsJudgementWindow scaled(HitResult result, (double late, double early) row, double rate)
        => new(result, row.late * rate, row.early * rate);

    private static BmsJudgementWindow fixedWindow(HitResult result, (double late, double early) row)
        => new(result, row.late, row.early);
}
