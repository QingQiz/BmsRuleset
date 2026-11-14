using System.Collections.Concurrent;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;

namespace RhythmEngine.Profiles.Bms;

public enum BmsJudgementMode
{
    FiveKey,
    SevenKey,
    Pms,
    Keyboard,
}

[Flags]
public enum BmsJudgementModifiers
{
    None = 0,
    Lr2oraja = 1,
    NoBad = 2,
    NoGood = 4,
    NoGreat = 8,
}

/// <summary>A timing definition shared by execution, previews and framework adapters. All times are microseconds.</summary>
public sealed class BmsTimingDefinition(TimingWindows windows, long passiveDeadline, long? longNoteHeadDeadline = null)
{
    public TimingWindows Windows { get; } = windows;

    public long EarliestHitOffset { get; } = windows.Entries.Where(window => window.Result != BmsResults.EMPTY_POOR).Min(window => window.Early);

    // Removing a hit result does not remove its passive timeout.
    public long PassiveDeadline { get; } = passiveDeadline;

    public long? LongNoteHeadDeadline { get; } = longNoteHeadDeadline;

    public TimingWindow? WindowFor(ResultId result)
    {
        foreach (var window in Windows.Entries)
        {
            if (window.Result == result) return window;
        }

        return null;
    }
}

/// <summary>Immutable BMS policy, independent of any host mod instances or active game.</summary>
public sealed class BmsJudgementRules
{
    private static readonly ConcurrentDictionary<(BmsJudgementMode, BmsJudgementModifiers), BmsJudgementRules> configurations = new();
    private static readonly double[] rank_rates = [0.25, 0.50, 0.75, 1.00, 1.25];
    private static readonly double[] pms_rank_rates = [0.33, 0.50, 0.70, 1.00, 1.33];
    private readonly ConcurrentDictionary<double, BmsTimingProfile> profiles = new();
    private readonly BmsJudgementMode mode;
    private readonly BmsJudgementModifiers modifiers;

    private bool lr2 => modifiers.HasFlag(BmsJudgementModifiers.Lr2oraja);

    private BmsJudgementRules(BmsJudgementMode mode, BmsJudgementModifiers modifiers)
    {
        this.mode = mode;
        this.modifiers = modifiers;
    }

    public static BmsJudgementRules For(BmsJudgementMode mode, BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
        // LR2oraja has one timing family for all layouts.
        => configurations.GetOrAdd((modifiers.HasFlag(BmsJudgementModifiers.Lr2oraja) ? BmsJudgementMode.SevenKey : mode, modifiers),
            static key => new BmsJudgementRules(key.Item1, key.Item2));

    public long SearchWindow => lr2 ? 1_000_000 : 500_000;

    public BmsSelection? SelectionOverride => lr2 ? BmsSelection.Combo : null;

    public bool NonConsumingBad => !lr2 && mode == BmsJudgementMode.Pms;

    public long ReleaseGrace => NonConsumingBad ? 200_000 : 0;

    public bool ImmediateBadHead => lr2;

    public bool EmptyPoorBreaksCombo => !lr2 && mode is BmsJudgementMode.FiveKey or BmsJudgementMode.Pms;

    public double RateForRank(int rank) => lr2
        ? rank switch { 0 => .25, 1 => .5, 3 => 1, _ => .75 }
        : (mode == BmsJudgementMode.Pms ? pms_rank_rates : rank_rates)[Math.Clamp(rank, 0, 4)];

    public double RateForExRank(double exRank) => exRank / 100 * RateForRank(2);

    public double ResolveRate(int rank, double? exRank) => exRank is { } value ? RateForExRank(value) : RateForRank(rank);

    public BmsTimingProfile GetProfile(double rate)
    {
        if (!double.IsFinite(rate) || rate < 0) throw new ArgumentOutOfRangeException(nameof(rate));

        return profiles.TryGetValue(rate, out var profile) ? profile : profiles.GetOrAdd(rate, createProfile);
    }

    private BmsTimingProfile createProfile(double rate)
    {
        if (lr2)
        {
            var head = lr2Windows(rate, false);
            var tail = lr2Windows(rate, true);
            return new BmsTimingProfile(this, head, head, tail, tail);
        }

        return mode switch
        {
            BmsJudgementMode.FiveKey => new BmsTimingProfile(this,
                table(false, (-20, 20), (-50, 50), (-100, 100), (-150, 150), (-150, 500)),
                table(false, (-30, 30), (-60, 60), (-110, 110), (-160, 160), (-160, 500)),
                table(true, (-120, 120), (-150, 150), (-200, 200), (-250, 250)),
                table(true, (-130, 130), (-160, 160), (-110, 110), (-260, 260))),
            BmsJudgementMode.SevenKey => new BmsTimingProfile(this,
                table(false, (-20, 20), (-60, 60), (-150, 150), (-280, 220), (-150, 500)),
                table(false, (-30, 30), (-70, 70), (-160, 160), (-290, 230), (-160, 500)),
                table(true, (-120, 120), (-160, 160), (-200, 200), (-280, 220)),
                table(true, (-130, 130), (-170, 170), (-210, 210), (-290, 230))),
            BmsJudgementMode.Pms => new BmsTimingProfile(this,
                table(false, (-20, 20), (-50, 50), (-117, 117), (-183, 183), (-175, 500)),
                table(false, (-20, 20), (-50, 50), (-117, 117), (-183, 183), (-175, 500)),
                table(true, (-120, 120), (-150, 150), (-217, 217), (-283, 283)),
                table(true, (-120, 120), (-150, 150), (-217, 217), (-283, 283))),
            BmsJudgementMode.Keyboard => new BmsTimingProfile(this,
                table(false, (-30, 30), (-90, 90), (-200, 200), (-320, 240), (-200, 650)),
                table(false, (-30, 30), (-90, 90), (-200, 200), (-320, 240), (-200, 650)),
                table(true, (-160, 25), (-200, 75), (-260, 140), (-320, 240)),
                table(true, (-160, 25), (-200, 75), (-260, 140), (-320, 240))),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        BmsTimingDefinition table(bool tail, params (double Slow, double Fast)[] rows)
        {
            ResultId[] results = [BmsResults.PGREAT, BmsResults.GREAT, BmsResults.GOOD, BmsResults.BAD, BmsResults.EMPTY_POOR];
            var entries = new TimingWindow[rows.Length];

            for (var i = 0; i < rows.Length; i++)
            {
                var fixedWindow = i == 4 || (mode == BmsJudgementMode.Pms && i is 0 or 3);
                // beatoraja truncates in integral microseconds after multiplying by RANK.
                var early = (long)Math.Truncate(-rows[i].Fast * 1000 * (fixedWindow ? 1 : rate));
                var late = (long)Math.Truncate(-rows[i].Slow * 1000 * (fixedWindow ? 1 : rate));

                if (mode == BmsJudgementMode.Pms && i is 1 or 2)
                {
                    early = Math.Clamp(early, (long)(-rows[3].Fast * 1000), (long)(-rows[0].Fast * 1000));
                    late = Math.Clamp(late, (long)(-rows[0].Slow * 1000), (long)(-rows[3].Slow * 1000));
                }

                entries[i] = new TimingWindow(results[i], early, late);
            }

            // Preserve nested windows, including the unusual five-key long-scratch GOOD row.
            for (var i = 0; i < 3; i++)
            {
                var early = Math.Max(entries[i].Early, entries[3].Early);
                var late = Math.Min(entries[i].Late, entries[3].Late);

                if (i > 0)
                {
                    early = Math.Min(early, entries[i - 1].Early);
                    late = Math.Max(late, entries[i - 1].Late);
                }

                entries[i] = entries[i] with { Early = early, Late = late };
            }

            if (!tail && entries[4].Early <= -SearchWindow) entries[4] = entries[4] with { Early = -SearchWindow, IncludeEarly = false };
            return constrain(entries, entries[3].Late);
        }
    }

    private BmsTimingDefinition lr2Windows(double rate, bool tail)
    {
        var perfect = interpolate(rate, [0, 8, 15, 18, 21]);
        var great = interpolate(rate, [0, 24, 30, 40, 60]);
        var good = interpolate(rate, [0, 40, 60, 100, 120]);
        if (tail) perfect = great = good;
        var rows = new List<TimingWindow>
        {
            new(BmsResults.PGREAT, -perfect, perfect), new(BmsResults.GREAT, -great, great),
            new(BmsResults.GOOD, -good, good), new(BmsResults.BAD, -200_000, 200_000),
        };
        if (!tail) rows.Add(new TimingWindow(BmsResults.EMPTY_POOR, -SearchWindow, 0, IncludeEarly: false));
        return constrain(rows, 200_000, tail ? null : good);
    }

    private BmsTimingDefinition constrain(IEnumerable<TimingWindow> windows, long deadline, long? longNoteDeadline = null)
        => new(new TimingWindows(windows.Where(window =>
            !(modifiers.HasFlag(BmsJudgementModifiers.NoBad) && window.Result == BmsResults.BAD)
            && !((modifiers & (BmsJudgementModifiers.NoGood | BmsJudgementModifiers.NoGreat)) != 0 && window.Result == BmsResults.GOOD)
            && !(modifiers.HasFlag(BmsJudgementModifiers.NoGreat) && window.Result == BmsResults.GREAT))), deadline, longNoteDeadline);

    private static long interpolate(double rate, ReadOnlySpan<double> values)
    {
        if (rate >= 1) return (long)Math.Min(200_000, Math.Truncate(values[4] * rate * 1000));

        var index = (int)(rate * 4);
        return (long)Math.Truncate((values[index] + (values[index + 1] - values[index]) * (rate * 4 - index)) * 1000);
    }
}

public sealed class BmsTimingProfile(
    BmsJudgementRules rules,
    BmsTimingDefinition normal,
    BmsTimingDefinition scratch,
    BmsTimingDefinition longNoteTail,
    BmsTimingDefinition longScratchTail)
{
    private readonly ConcurrentDictionary<(KeyNoteKind, bool), INoteRule> noteRules = new();

    public BmsTimingDefinition GetTiming(bool directional, bool tail)
        => tail ? directional ? longScratchTail : longNoteTail : directional ? scratch : normal;

    public INoteRule CreateRule(KeyNote note)
    {
        var head = GetTiming(note.Directional, false);
        var deadline = note.Kind == KeyNoteKind.Hold ? head.LongNoteHeadDeadline ?? head.PassiveDeadline : head.PassiveDeadline;

        // Chart-specific short LN durations must not accumulate in the shared profile cache.
        if (note.Kind == KeyNoteKind.Hold && head.LongNoteHeadDeadline != null && note.EndTime - note.StartTime < deadline)
            return createRule(note.EndTime - note.StartTime);

        return noteRules.GetOrAdd((note.Kind, note.Directional), _ => createRule(deadline));

        INoteRule createRule(long headDeadline) => BmsProfiles.CreateRule(note, head.Windows,
            GetTiming(note.Directional, true).Windows, new BmsRuleOptions(ImmediateBadHead: rules.ImmediateBadHead,
                NonConsumingBad: rules.NonConsumingBad, HeadDeadline: headDeadline, TailDeadline: head.PassiveDeadline,
                ReleaseGrace: rules.ReleaseGrace, MineOrder: TimerOrder.AfterInput, PassiveOrder: TimerOrder.AfterInput));
    }
}
