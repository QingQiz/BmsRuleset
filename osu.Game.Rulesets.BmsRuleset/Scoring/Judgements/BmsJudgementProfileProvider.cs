using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;

public static class BmsJudgementProfileProvider
{
    private static readonly double[] rank_rates = [0.25, 0.50, 0.75, 1.00, 1.25];
    private static readonly double[] pms_rank_rates = [0.33, 0.50, 0.70, 1.00, 1.33];
    private static readonly ConcurrentDictionary<ProfileKey, BmsJudgementProfile> profiles = new();

    private static IReadOnlyList<IApplicableToJudgementWindow> activeWindowMods = [];

    internal static void SetActiveWindowMods(IReadOnlyList<IApplicableToJudgementWindow> mods)
        => activeWindowMods = mods;

    internal static void ClearActiveWindowMods(IReadOnlyList<IApplicableToJudgementWindow> mods)
    {
        if (ReferenceEquals(activeWindowMods, mods))
            activeWindowMods = [];
    }

    public static BmsJudgementWindowTable GetTable(BmsLayoutVariant layout, int column, int rank, bool tail)
        => getTable(layout, column, RateForRank(layout, rank), tail);

    public static BmsJudgementWindowTable GetTable(BmsLayoutVariant layout, int column, double judgementRate, bool tail)
        => getTable(layout, column, judgementRate, tail);

    public static double RateForRank(int rank) => rank_rates[Math.Clamp(rank, 0, 4)];

    public static double RateForRank(BmsLayoutVariant layout, int rank) => rateForLayoutRank(layout, Math.Clamp(rank, 0, 4));

    public static double RateForExRank(BmsLayoutVariant layout, double exRank)
        // BMSPlayerRule first converts DEFEXRANK to an integer percentage.
        => exRank > 0 ? Math.Truncate(Math.Truncate(exRank) * Math.Round(RateForRank(layout, 2) * 100) / 100) / 100 : RateForRank(layout, 2);

    private static BmsJudgementWindowTable getTable(BmsLayoutVariant layout, int column, double judgementRate, bool tail)
    {
        var profile = profiles.GetOrAdd(new ProfileKey(layout, judgementRate), static key => createProfile(key.Layout, key.Rate));
        var scratch = BmsLayout.IsScratchColumn(column, layout);

        var table = tail
            ? scratch
                ? profile.LongScratchTail
                : profile.LongNoteTail
            : scratch
                ? profile.Scratch
                : profile.Normal;

        foreach (var mod in activeWindowMods)
            table = mod.ApplyToJudgementWindow(table);

        return table;
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

    public static bool IsPms(BmsLayoutVariant layout)
        => layout is BmsLayoutVariant.Pms9K or BmsLayoutVariant.Pms9K2P or BmsLayoutVariant.Pms9KDouble;

    public static bool EmptyPoorBreaksCombo(BmsLayoutVariant layout)
        => IsPms(layout) || layout is BmsLayoutVariant.Bms5K or BmsLayoutVariant.Bms5K2P or BmsLayoutVariant.Bms5KDouble;

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
        (double slow, double fast) miss)
        => createTable(rate, true, perfect, great, good, bad, miss);

    private static BmsJudgementWindowTable pmsTail(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad)
        => createTable(rate, true, perfect, great, good, bad);

    private static BmsJudgementWindowTable head(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad,
        (double slow, double fast) miss)
        => createTable(rate, false, perfect, great, good, bad, miss);

    private static BmsJudgementWindowTable tail(
        double rate,
        (double slow, double fast) perfect,
        (double slow, double fast) great,
        (double slow, double fast) good,
        (double slow, double fast) bad)
        => createTable(rate, false, perfect, great, good, bad);

    private static BmsJudgementWindowTable createTable(double rate, bool pms,
                                                       params (double slow, double fast)[] rows)
    {
        HitResult[] results = [HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Miss];
        var windows = new BmsJudgementWindow[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            var fixedWindow = i == 4 || pms && i is 0 or 3;
            // beatoraja stores integral microseconds, including after rank multiplication.
            var slow = fixedWindow ? rows[i].slow : Math.Truncate(rows[i].slow * 1000 * rate) / 1000;
            var fast = fixedWindow ? rows[i].fast : Math.Truncate(rows[i].fast * 1000 * rate) / 1000;
            if (pms && i is 1 or 2)
            {
                slow = Math.Clamp(slow, rows[3].slow, rows[0].slow);
                fast = Math.Clamp(fast, rows[0].fast, rows[3].fast);
            }

            windows[i] = new BmsJudgementWindow(results[i], slow, fast);
        }

        // Keep nested windows even for the unusual 5K long-scratch GOOD row.
        for (var i = 0; i < 3; i++)
        {
            var slow = Math.Max(windows[i].SlowDTime, windows[3].SlowDTime);
            var fast = Math.Min(windows[i].FastDTime, windows[3].FastDTime);
            if (i > 0)
            {
                slow = Math.Min(slow, windows[i - 1].SlowDTime);
                fast = Math.Max(fast, windows[i - 1].FastDTime);
            }

            windows[i] = new BmsJudgementWindow(results[i], slow, fast);
        }

        return new BmsJudgementWindowTable(windows);
    }
}
