using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.Malody;

/// <summary>Selects a Malody client rule family. Classic is pinned to 4.3.7; V clients also use <see cref="MalodyVVersion"/>.</summary>
public enum MalodyClient
{
    /// <summary>Classic PC 4.3.7: PC timing windows, a 160 ms head expiry offset and 110 ms of early hold-release lenience.</summary>
    ClassicPc,

    /// <summary>Classic mobile, based on Android 4.3.7: wider mobile windows, a 170 ms head expiry offset and 130 ms of early release lenience.</summary>
    ClassicMobile,

    /// <summary>V Standard uses the wider base windows; VVersion determines the exact boundaries and hold settlement rules.</summary>
    VStandard,

    /// <summary>V Pro uses narrower base windows and shares the selected VVersion's rule implementation with VStandard.</summary>
    VPro,
}

/// <summary>Malody judge difficulty, ordered from lenient to strict, affecting base windows, score multipliers and default health penalties.</summary>
/// <remarks>
/// Listed window adjustments apply to the client's BEST/COOL/GOOD bases; MISS boundaries follow the client-specific rules.
/// V window scaling, conversion and custom calibration are applied through their own settings.
/// </remarks>
public enum MalodyJudge
{
    /// <summary>The most lenient judge: adds 20 ms to base windows, uses a 78% score multiplier and 1x default GOOD/MISS damage.</summary>
    Amateur,

    /// <summary>Adds 10 ms to base windows, uses an 85% score multiplier and 2x default GOOD/MISS damage.</summary>
    Beginner,

    /// <summary>The default judge: unadjusted base windows, a 100% score multiplier and 3x default GOOD/MISS damage.</summary>
    Climber,

    /// <summary>Narrows base windows by 8 ms, uses a 110% score multiplier and 4x default GOOD/MISS damage.</summary>
    Dancer,

    /// <summary>The strictest judge: narrows base windows by 16 ms, uses a 120% score multiplier and 5x default GOOD/MISS damage.</summary>
    Expert,
}

/// <summary>Pins Malody V rules so window boundaries and hold behaviour from different releases are not mixed. Only affects V clients.</summary>
public enum MalodyVVersion
{
    /// <summary>The RC 2.0 model, limited to its verified release and timing options; JudgeAtEnd, window scaling and conversion are unsupported.</summary>
    Rc20,

    /// <summary>The default 6.7.22 rules, with separate tail judgements and native float window boundaries; supports JudgeAtEnd, scaling and conversion.</summary>
    V6722,
}

/// <summary>
/// Optional overrides for host-defined gauges or timing calibration.
/// </summary>
public sealed record MalodyCalibration(HealthConfiguration? Health = null, ImmutableDictionary<MalodyJudge, TimingWindows>? MobileWindows = null);

public sealed record MalodyOptions(
    MalodyClient Client = MalodyClient.ClassicPc,
    MalodyJudge Judge = MalodyJudge.Climber,
    bool JudgeAtEnd = false,
    bool FailOnEmpty = true,
    bool AllowEarlyMiss = true,
    MalodyVVersion VVersion = MalodyVVersion.V6722,
    float WindowScale = 1,
    int WindowConversionPercent = 0);

public static class MalodyProfile
{
    public static TimingWindows PcWindows(MalodyJudge judge)
        => classicWindows(judge, false);

    public static TimingWindows ClassicMobileWindows(MalodyJudge judge)
        => classicWindows(judge, true);

    public static TimingWindows VWindows(MalodyJudge judge, bool pro, bool allowEarlyMiss = true,
                                         MalodyVVersion version = MalodyVVersion.V6722, float scale = 1, int conversionPercent = 0)
    {
        if (version == MalodyVVersion.V6722)
            return MalodyVTiming.Create(judge, pro, allowEarlyMiss, scale, conversionPercent).Windows;

        if (version != MalodyVVersion.Rc20 || scale != 1 || conversionPercent != 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        var entries = classicWindows(judge, !pro).Entries;
        return new TimingWindows(entries.Select((window, i) => i < 3
            ? window with { IncludeEarly = false, IncludeLate = false }
            : window with { Early = allowEarlyMiss ? window.Early : 0, Late = -window.Early }));
    }

    private static TimingWindows classicWindows(MalodyJudge judge, bool mobile)
    {
        if (!Enum.IsDefined(judge))
            throw new ArgumentOutOfRangeException(nameof(judge));

        var offset = new[] { 20, 10, 0, -8, -16 }[(int)judge];
        var entries = ProfileSupport.Symmetric(("best", (mobile ? 45 : 36) + offset), ("cool", (mobile ? 85 : 76) + offset),
            ("good", (mobile ? 130 : 110) + offset)).Entries;
        return new TimingWindows([
            .. entries, new TimingWindow("miss", -Time.FromMilliseconds((mobile ? 170 : 160) + offset),
                Time.FromMilliseconds(mobile ? 170 : 160)),
        ]);
    }

    public static CompiledProfile Create(KeyChart source, MalodyOptions? options = null) => Create(source, new MalodyCalibration(), options);

    public static CompiledProfile Create(KeyChart source, MalodyCalibration calibration, MalodyOptions? options = null)
    {
        options ??= new MalodyOptions();
        if (!Enum.IsDefined(options.Client) || !Enum.IsDefined(options.Judge) || !Enum.IsDefined(options.VVersion))
            throw new ArgumentOutOfRangeException(nameof(options));

        var classic = options.Client is MalodyClient.ClassicPc or MalodyClient.ClassicMobile;
        if (classic && (options.JudgeAtEnd || !options.AllowEarlyMiss || options.WindowScale != 1 || options.WindowConversionPercent != 0))
            throw new ArgumentException("These judgement options belong to Malody V.", nameof(options));
        if (!classic && options.VVersion == MalodyVVersion.Rc20 && (options.JudgeAtEnd || options.WindowScale != 1 || options.WindowConversionPercent != 0))
            throw new ArgumentException("The RC 2.0 profile only defines its verified release policy and timing options.", nameof(options));

        var timing = !classic && options.VVersion == MalodyVVersion.V6722
            ? MalodyVTiming.Create(options.Judge, options.Client == MalodyClient.VPro, options.AllowEarlyMiss, options.WindowScale, options.WindowConversionPercent)
            : null;

        var windows = options.Client switch
        {
            MalodyClient.ClassicMobile => calibration.MobileWindows?.GetValueOrDefault(options.Judge) ?? ClassicMobileWindows(options.Judge),
            MalodyClient.VStandard => calibration.MobileWindows?.GetValueOrDefault(options.Judge) ?? timing?.Windows ?? VWindows(options.Judge, false, options.AllowEarlyMiss, options.VVersion),
            MalodyClient.VPro => timing?.Windows ?? VWindows(options.Judge, true, options.AllowEarlyMiss, options.VVersion),
            _ => PcWindows(options.Judge),
        };
        if (timing != null)
            timing = timing with { Windows = windows };
        var multiplier = options.Judge switch
        {
            MalodyJudge.Amateur => 78, MalodyJudge.Beginner => 85, MalodyJudge.Climber => 100,
            MalodyJudge.Dancer => 110, _ => 120,
        };
        var chart = source.Compile(note => createRule(note, windows, options, timing));
        var descriptor = new ProfileDescriptor($"malody-key-{options.Client}-{options.Judge}-{(classic ? "4.3.7" : options.VVersion)}",
            classic ? "malody-4.3.7-pc-d0cf9ecb493f-android-7b6f5650481f" : timing != null ? "malody-v-6.7.22-2f1a4f10a95c" : "malody-v-rc2.0-21c73a27b7e1",
            EvidenceLevel.StaticBinary,
            [
                "https://mzh.moegirl.org.cn/Malody", "https://cbo17ty22x.feishu.cn/wiki/wikcnNkrL0temn9FWmRTUYryv2e",
                "https://cbo17ty22x.feishu.cn/wiki/wikcnrF2fKHoDnC6q8tJKPvx2Ph",
                "https://malody.en.uptodown.com/android/download",
            ],
            [
                "Comparisons pin classic 4.3.7 PC/Android, V RC 2.0 or V 6.7.22 explicitly. Binary-equivalent input uses the clients' integer millisecond song clock.",
                !classic && options.VVersion == MalodyVVersion.Rc20
                    ? "RC 2.0 has static binary comparisons; the independently executed native fixtures cover classic 4.3.7 and V 6.7.22, not this RC."
                    : "Native fixtures cover timing, release/tick state, score arithmetic and Key routing/expiry blocks; V 6.7.22 also covers repeat dispatch. Application scheduling and physical control-count bookkeeping remain outside those fixtures.",
                "V's F toggle is read by Slide's load path and replay recording; P is read by the pause panel. Neither adds a Key judgement policy. Pause and physical control mapping remain host responsibilities.",
            ]);
        var health = calibration.Health ?? createHealth(options);
        return new CompiledProfile(chart, new SessionConfiguration(descriptor.Id, createScore(multiplier, !classic), health, ProfileSupport.Oldest), descriptor);
    }

    private static INoteRule createRule(KeyNote note, TimingWindows windows, MalodyOptions options, MalodyVTiming? timing)
    {
        if (timing != null)
        {
            if (note.Kind is not (KeyNoteKind.Tap or KeyNoteKind.Hold))
                throw ProfileSupport.Unsupported("Malody Key", note.Kind);
            if (note.Kind == KeyNoteKind.Hold && note.EndTime == note.StartTime)
                throw new ArgumentException("Malody holds require a positive duration.", nameof(note));

            return new MalodyVCurrentRule(timing, note.Kind == KeyNoteKind.Hold, options.JudgeAtEnd);
        }

        var deadline = options.Client switch
        {
            MalodyClient.ClassicPc => Time.FromMilliseconds(160),
            MalodyClient.ClassicMobile => Time.FromMilliseconds(170),
            _ => windows.Entries[^1].Late,
        };
        if (note.Kind == KeyNoteKind.Tap)
            return new TapRule(new TapConfiguration(windows, deadline, "miss", 1000));

        if (note.Kind != KeyNoteKind.Hold)
            throw ProfileSupport.Unsupported("Malody Key", note.Kind);
        if (note.EndTime == note.StartTime)
            throw new ArgumentException("Malody holds require a positive duration.", nameof(note));

        if (options.Client is MalodyClient.ClassicPc or MalodyClient.ClassicMobile)
            return new MalodyClassicHoldRule(windows, deadline, Time.FromMilliseconds(options.Client == MalodyClient.ClassicMobile ? 130 : 110));

        return new MalodyVHoldRule(windows);
    }

    private static ScoreConfiguration createScore(int multiplier, bool vScore)
    {
        var values = ProfileSupport.Values(("best", 1000, 1000, ComboChange.Increment), ("cool", 750, 750, ComboChange.Increment),
            ("good", 400, 400, ComboChange.Increment), ("miss", 0, 0, ComboChange.Reset));
        return new ScoreConfiguration(new ScoreState { Bonus = 1 }, (state, context) =>
        {
            if (context.Event.Result == "critical")
                return state with { Counters = state.Counters.SetItem("critical", state.Counters.GetValueOrDefault("critical") + 1) };
            if (context.Event.Result == "hold-tick")
                return state with { Combo = state.Combo + 1, MaximumCombo = Math.Max(state.MaximumCombo, state.Combo + 1) };
            if (context.Event.Result == "hold-drop")
                return state with { Bonus = Math.Max(0, (int)Math.Round(state.Bonus * 100) - 32) / 100.0, Combo = 0 };
            if (context.Opportunity == null)
                return state;

            var change = context.Event.Result switch
            {
                "best" => 1,
                "cool" => -2,
                "good" => -8,
                _ => -32,
            };
            // The native client's bonus is an integer percentage, independent of its float HP field.
            var bonus = Math.Clamp((int)Math.Round(state.Bonus * 100) + change, 0, 100) / 100.0;
            var next = ProfileSupport.Accumulate(state, context, values);
            if (vScore)
            {
                // V multiplies by a single-precision bonus factor before accumulating a double score.
                var raw = state.Counters.GetValueOrDefault("malody-raw-score")
                          + values[context.Event.Result].Score * (multiplier / 100.0) * ((float)(bonus * 100) / 100 + 1);
                return next with { Score = Math.Floor(raw), Bonus = bonus, Counters = state.Counters.SetItem("malody-raw-score", raw) };
            }

            // All built-in multipliers have two decimal places; fixed units preserve fractional credit across hits.
            var units = state.Counters.GetValueOrDefault("malody-score-units")
                        + values[context.Event.Result].Score * (100 + (int)Math.Round(bonus * 100)) * multiplier;
            return next with
            {
                Score = Math.Floor(units / 10000),
                Bonus = bonus,
                Counters = state.Counters.SetItem("malody-score-units", units),
            };
        }, plan => plan.Opportunities.Length * 2000.0 * multiplier / 100);
    }

    private static HealthConfiguration createHealth(MalodyOptions options)
    {
        var judgeScale = (int)options.Judge + 1;
        return HealthRules.LatchedGauge(0, 1, 1, context =>
        {
            var delta = context.Event.Result switch
            {
                "best" => 0.012f,
                "cool" => 0.004f,
                "good" => -0.02f * judgeScale,
                "miss" => -0.05f * judgeScale,
                "hold-drop" or "hold-tick" or "critical" => 0,
                _ => throw new InvalidOperationException($"Undefined Malody health result {context.Event.Result}."),
            };
            return (float)context.Health.Value + delta - context.Health.Value;
        }, value => options.FailOnEmpty && value <= 0, value => !options.FailOnEmpty || value > 0);
    }
}
