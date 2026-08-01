using System;
using System.Collections.Concurrent;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public static class BmsJudgementProfileProvider
{
    private static readonly double[] rank_rates = [0.25, 0.50, 0.75, 1.00, 1.25];
    private static readonly double[] pms_rank_rates = [0.33, 0.50, 0.70, 1.00, 1.33];
    private static readonly ConcurrentDictionary<ProfileKey, BmsJudgementProfile> profiles = new();

    public static BmsJudgementWindowTable GetTable(BmsLayoutVariant layout, int column, int rank, bool tail)
        => getTable(layout, column, RateForRank(layout, rank), tail);

    public static BmsJudgementWindowTable GetTable(BmsLayoutVariant layout, int column, double judgementRate, bool tail)
        => getTable(layout, column, judgementRate, tail);

    public static double RateForRank(int rank) => rank_rates[Math.Clamp(rank, 0, 4)];

    public static double RateForRank(BmsLayoutVariant layout, int rank) => rateForLayoutRank(layout, Math.Clamp(rank, 0, 4));

    public static double RateForExRank(BmsLayoutVariant layout, double exRank) => RateForRank(layout, 2) * exRank / 100d;

    private static BmsJudgementWindowTable getTable(BmsLayoutVariant layout, int column, double judgementRate, bool tail)
    {
        var profile = profiles.GetOrAdd(new ProfileKey(layout, judgementRate), static key => createProfile(key.Layout, key.Rate));
        var scratch = BmsLayout.IsScratchColumn(column, layout);

        return tail
            ? scratch
                ? profile.LongScratchTail
                : profile.LongNoteTail
            : scratch
                ? profile.Scratch
                : profile.Normal;
    }

    private static double rateForLayoutRank(BmsLayoutVariant layout, int rank)
        => layout switch
        {
            BmsLayoutVariant.Pms9K or BmsLayoutVariant.Pms9K2P or BmsLayoutVariant.Pms9KDouble => pms_rank_rates[rank],
            _ => rank_rates[rank],
        };

    private static BmsJudgementProfile createProfile(BmsLayoutVariant layout, double rate)
        => layout switch
        {
            BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble => fiveKeys(rate),
            // ReSharper disable once RedundantSwitchExpressionArms
            BmsLayoutVariant.Bme7K or BmsLayoutVariant.Bme7K2P or BmsLayoutVariant.Bme7KDouble => sevenKeys(rate),
            BmsLayoutVariant.Pms9K or BmsLayoutVariant.Pms9K2P or BmsLayoutVariant.Pms9KDouble => pms(rate),
            _ => sevenKeys(rate),
        };

    private readonly record struct ProfileKey(BmsLayoutVariant Layout, double Rate);

    private static BmsJudgementProfile fiveKeys(double rate) => new(
        head(rate, (-20, 20), (-50, 50), (-100, 100), (-150, 150), (-150, 500)),
        head(rate, (-30, 30), (-60, 60), (-110, 110), (-160, 160), (-160, 500)),
        tail(rate, (-120, 120), (-150, 150), (-200, 200), (-250, 250)),
        tail(rate, (-130, 130), (-160, 160), (-110, 110), (-260, 260)));

    private static BmsJudgementProfile sevenKeys(double rate) => new(
        head(rate, (-20, 20), (-60, 60), (-150, 150), (-280, 220), (-150, 500)),
        head(rate, (-30, 30), (-70, 70), (-160, 160), (-290, 230), (-160, 500)),
        tail(rate, (-120, 120), (-160, 160), (-200, 200), (-280, 220)),
        tail(rate, (-130, 130), (-170, 170), (-210, 210), (-290, 230)));

    private static BmsJudgementProfile pms(double rate) => new(
        pmsHead(rate, (-20, 20), (-50, 50), (-117, 117), (-183, 183), (-175, 500)),
        pmsHead(rate, (-20, 20), (-50, 50), (-117, 117), (-183, 183), (-175, 500)),
        pmsTail(rate, (-120, 120), (-150, 150), (-217, 217), (-283, 283)),
        pmsTail(rate, (-120, 120), (-150, 150), (-217, 217), (-283, 283)));

    private static BmsJudgementWindowTable pmsHead(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad,
        (double slow, double fast) miss) => new([
        fixedWindow(HitResult.Perfect, perfect),
        scaled(HitResult.Great, great, rate),
        scaled(HitResult.Good, good, rate),
        fixedWindow(HitResult.Ok, bad),
        fixedWindow(HitResult.Miss, miss),
    ]);

    /// <summary>
    /// PMS tail uses rank-scaled perfect/great/good (unlike head where PGREAT is fixed).
    /// </summary>
    private static BmsJudgementWindowTable pmsTail(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad) => new([
        scaled(HitResult.Perfect, perfect, rate),
        scaled(HitResult.Great, great, rate),
        scaled(HitResult.Good, good, rate),
        fixedWindow(HitResult.Ok, bad),
    ]);

    private static BmsJudgementWindowTable head(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad,
        (double slow, double fast) miss) => new([
        scaled(HitResult.Perfect, perfect, rate),
        scaled(HitResult.Great, great, rate),
        scaled(HitResult.Good, good, rate),
        fixedWindow(HitResult.Ok, bad),
        fixedWindow(HitResult.Miss, miss),
    ]);

    private static BmsJudgementWindowTable tail(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad) => new([
        scaled(HitResult.Perfect, perfect, rate),
        scaled(HitResult.Great, great, rate),
        scaled(HitResult.Good, good, rate),
        fixedWindow(HitResult.Ok, bad),
    ]);

    private static BmsJudgementWindow scaled(HitResult result, (double slow, double fast) row, double rate)
        => new(result, row.slow * rate, row.fast * rate);

    private static BmsJudgementWindow fixedWindow(HitResult result, (double slow, double fast) row)
        => new(result, row.slow, row.fast);
}
