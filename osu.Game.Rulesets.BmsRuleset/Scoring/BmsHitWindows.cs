using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

public class BmsHitWindows(int rank = 2) : HitWindows
{

    /// <summary>Fallback BAD window (ms) used when <see cref="HitWindows" /> is unavailable.</summary>
    public const double FALLBACK_BAD_WINDOW = 200;

    // ── LR2 windows (default, symmetric) ────────────────────────────────────
    // Indexed by RANK 0-4: (pgreat, great, good, badEarly, badLate, poorEarly, poorLate, emptyPoorEarly).
    // poorEarly = badEarly (=200) → no POOR hit gap → BmsResultFor never returns
    //   Meh for early presses.  The EP boundary is at -1000ms (emptyPoorEarly=1000)
    //   for E-POOR detection via IsEpoZone.
    private static readonly (double pgreat, double great, double good, double badEarly, double badLate, double poorEarly, double poorLate, double emptyPoorEarly)[] rank_windows_lr2 =
    [
        (8, 24, 40, 200, 200, 200, 200, 1000),   // RANK 0 - Very Hard
        (15, 30, 60, 200, 200, 200, 200, 1000),  // RANK 1 - Hard
        (18, 40, 100, 200, 200, 200, 200, 1000), // RANK 2 - Normal (default)
        (21, 60, 120, 200, 200, 200, 200, 1000), // RANK 3 - Easy
        (21, 60, 200, 200, 200, 200, 200, 1000), // RANK 4 - Very Easy
    ];

    // ── Beatoraja windows (asymmetric) ──────────────────────────────────────
    // Indexed by RANK 0-4: (pgreat, great, good, badEarly, badLate, poorEarly, poorLate, emptyPoorEarly).
    // poorEarly = 500 (POOR hit boundary AND EP boundary — they are the same in
    //   beatoraja).  BAD window scales with JUDGERANK; POOR/MS window is FIXED
    //   at {-150ms, +500ms} for all ranks.
    // Values derived from beatoraja JudgeProperty.java SEVENKEYS base × judgerank rate.
    // ReSharper disable once UnusedMember.Local
    private static readonly (double pgreat, double great, double good, double badEarly, double badLate, double poorEarly, double poorLate, double emptyPoorEarly)[] rank_windows_beatoraja =
    [
        (5, 15, 37.5, 55, 70, 500, 150, 500),     // RANK 0 - Very Hard (25%)
        (10, 30, 75, 110, 140, 500, 150, 500),    // RANK 1 - Hard       (50%)
        (15, 45, 112.5, 165, 210, 500, 150, 500), // RANK 2 - Normal     (75%)
        (20, 60, 150, 220, 280, 500, 150, 500),   // RANK 3 - Easy      (100%)
        (25, 75, 187, 275, 350, 500, 150, 500),   // RANK 4 - Very Easy (125%)
    ];

    private readonly int rank = Math.Clamp(rank, 0, 4);

    private double pgreatEarly, pgreatLate;
    private double greatEarly, greatLate;
    private double goodEarly, goodLate;
    private double badEarly, badLate;
    private double poorEarly, poorLate;
    private double emptyPoorEarly;

    public override bool IsHitResultAllowed(HitResult result) => result switch
    {
        HitResult.Perfect or HitResult.Great or HitResult.Good
            or HitResult.Ok or HitResult.Meh => true,
        _ => false,
    };

    public override void SetDifficulty(double difficulty)
    {
        // Swap rank_windows_lr2 → rank_windows_beatoraja to switch implementations.
        loadWindows(rank_windows_lr2[rank]);
    }

    /// <inheritdoc />
    /// <summary>
    ///     Framework-facing symmetric window.  Returns a single value per result tier so
    ///     that the base <see cref="T:osu.Game.Rulesets.Scoring.HitWindows">HitWindows</see> contract (symmetric ±ms, <c>Math.Abs</c>
    ///     comparisons) is satisfied for framework internals, tests, and HUD display.
    ///     <para />
    ///     <b>Do not use <c>WindowFor</c> for gameplay timing decisions.</b>  Use
    ///     <see cref="M:osu.Game.Rulesets.BmsRuleset.Scoring.BmsHitWindows.BmsResultFor(System.Double)">BmsResultFor</see> instead — it correctly handles asymmetric early/late
    ///     windows and the POOR hit zone.
    ///     <para />
    ///     For PGREAT / GREAT / GOOD, returns <c>Min(early, late)</c> — the tighter side.
    ///     <para />
    ///     For BAD (Ok) and POOR (Meh), returns <b>the late side only</b> (<c>badLate</c>).
    ///     Every external caller that queries <c>WindowFor(Ok)</c> or <c>WindowFor(Meh)</c>
    ///     does so on the <b>late side</b> (time has already passed the note):
    ///     <list type="bullet">
    ///         <item><see cref="M:osu.Game.Rulesets.BmsRuleset.Objects.Drawables.DrawableBmsHitObject.CheckForResult(System.Boolean,System.Double)">Objects.Drawables.DrawableBmsHitObject.CheckForResult</see> —
    ///         passive POOR once <c>timeOffset &gt; badLate</c></item>
    ///         <item><see cref="M:osu.Game.Rulesets.BmsRuleset.Objects.Drawables.DrawableBmsHitObject.TryRelease">Objects.Drawables.DrawableBmsHitObject.TryRelease</see> —
    ///         LN tail window: <c>Time.Current &gt; EndTime + badLate</c></item>
    ///         <item><see cref="T:osu.Game.Rulesets.BmsRuleset.Audio.BmsKeySoundPlayer">Audio.BmsKeySoundPlayer</see> —
    ///         key-sound scheduling: <c>Time.Current &gt; StartTime + badLate</c></item>
    ///     </list>
    /// </summary>
    public override double WindowFor(HitResult result) => result switch
    {
        HitResult.Perfect => Math.Min(pgreatEarly, pgreatLate),
        HitResult.Great => Math.Min(greatEarly, greatLate),
        HitResult.Good => Math.Min(goodEarly, goodLate),
        HitResult.Ok => badLate,
        HitResult.Meh => badLate,
        HitResult.Miss => badLate,
        _ => 0,
    };

    /// <summary>
    ///     Whether a key press at <paramref name="timeOffset" /> falls in the Empty POOR zone:
    ///     outside the early POOR window but within the <see cref="emptyPoorEarly" /> EP boundary.
    ///     <para />
    ///     For LR2 (<c>emptyPoorEarly = 1000</c>): E-POOR for early presses
    ///     in <c>[-1000, -badEarly)</c>.
    ///     For Beatoraja (<c>emptyPoorEarly = 500</c>): E-POOR only within
    ///     <c>[-500, -badEarly)</c>.
    /// </summary>
    public bool IsEpoZone(double timeOffset)
    {
        if (timeOffset >= 0)
            return false;

        var abs = -timeOffset;
        return abs > poorEarly && abs <= emptyPoorEarly;
    }

    public HitResult BmsResultFor(double timeOffset)
    {
        if (timeOffset < 0)
        {
            var abs = -timeOffset;

            if (abs <= pgreatEarly) return HitResult.Perfect;
            if (abs <= greatEarly) return HitResult.Great;
            if (abs <= goodEarly) return HitResult.Good;
            if (abs <= badEarly) return HitResult.Ok;
            if (abs <= poorEarly) return HitResult.Meh;

            return HitResult.None;
        }

        if (timeOffset > poorLate)
            return HitResult.None;

        if (timeOffset <= pgreatLate) return HitResult.Perfect;
        if (timeOffset <= greatLate) return HitResult.Great;
        if (timeOffset <= goodLate) return HitResult.Good;
        if (timeOffset <= badLate) return HitResult.Ok;
        if (timeOffset <= poorLate) return HitResult.Meh;

        return HitResult.None;
    }

    private void loadWindows((double pgreat, double great, double good, double badEarly, double badLate, double poorEarly, double poorLate, double emptyPoorEarly) w)
    {
        pgreatEarly = pgreatLate = w.pgreat;
        greatEarly = greatLate = w.great;
        goodEarly = goodLate = w.good;
        badEarly = w.badEarly;
        badLate = w.badLate;
        poorEarly = w.poorEarly;
        poorLate = w.poorLate;
        emptyPoorEarly = w.emptyPoorEarly;
    }
}
