using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <inheritdoc />
/// <summary>
///     Native BMS hit windows driven by the chart's <c>#RANK</c> value.
/// </summary>
/// <remarks>
///     <para>
///         BMS six-tier judgement mapped to osu! <see cref="T:osu.Game.Rulesets.Scoring.HitResult">HitResult</see> values:
///         <list type="table">
///             <item><term>PGREAT → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Perfect">HitResult.Perfect</see></term><description>Tightest window; 2 EX points.</description></item>
///             <item><term>GREAT  → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Great">HitResult.Great</see></term> <description>1 EX point; no combo break.</description></item>
///             <item><term>GOOD   → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Good">HitResult.Good</see></term>  <description>0 EX points; no combo break.</description></item>
///             <item><term>BAD    → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Ok">HitResult.Ok</see></term>    <description>0 EX points; breaks combo in native BMS.</description></item>
///             <item><term>POOR (normal, passive) → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Meh">HitResult.Meh</see></term><description>Note passed with no input; breaks combo.</description></item>
///             <item><term>EARLY POOR (excess input) → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Miss">HitResult.Miss</see></term><description>Key pressed far before note window; does not break combo in native BMS but treated as auto-miss here.</description></item>
///         </list>
///     </para>
///     <para>
///         Two reference implementations exist for BMS timing windows. <b>Currently using LR2.</b>
///     </para>
///     <para>
///         <b>Lunatic Rave 2 (LR2)</b> — symmetric ±ms windows per <c>#RANK</c>:
///         <list type="table">
///             <listheader><term>RANK</term><description>PGREAT / GREAT / GOOD / BAD  (all ±ms; POOR and EARLY POOR share the BAD boundary on the late side; EARLY POOR extends to −1000 ms on the early side)</description></listheader>
///             <item><term>0 Very Hard</term> <description>±8   / ±24  / ±40  / ±200</description></item>
///             <item><term>1 Hard</term>      <description>±15  / ±30  / ±60  / ±200</description></item>
///             <item><term>2 Normal</term>    <description>±18  / ±40  / ±100 / ±200</description></item>
///             <item><term>3 Easy</term>      <description>±21  / ±60  / ±120 / ±200</description></item>
///             <item><term>4 Very Easy</term> <description>±21  / ±60  / ±200 / ±200</description></item>
///         </list>
///     </para>
///     <para>
///         <b>Beatoraja</b> — asymmetric BAD window (early/late differ); GOOD is also tighter overall:
///         <list type="table">
///             <listheader><term>RANK</term><description>PGREAT / GREAT / GOOD / BAD (early / late)  / Empty POOR early / Empty POOR late</description></listheader>
///             <item><term>0 Very Hard</term> <description>±5   / ±15  / ±37.5  / −385 / +490  / −500 / +150</description></item>
///             <item><term>1 Hard</term>      <description>±10  / ±30  / ±75    / −330 / +420  / −500 / +150</description></item>
///             <item><term>2 Normal</term>    <description>±15  / ±45  / ±112.5 / −275 / +350  / −500 / +150</description></item>
///             <item><term>3 Easy</term>      <description>±20  / ±60  / ±150   / −200 / +280  / −500 / +150</description></item>
///             <item><term>4 Very Easy</term> <description>±25  / ±75  / ±187   / −350 / +275  / −500 / +150</description></item>
///         </list>
///     </para>
/// </remarks>
public class BmsHitWindows : HitWindows
{
    // ── LR2 windows (currently active) ──────────────────────────────────────
    // Indexed by RANK 0-4: (pgreat, great, good).
    // BAD, POOR, and EARLY POOR all share the 200 ms outer boundary.
    private static readonly (double pgreat, double great, double good)[] rank_windows_lr2 =
    [
        (8, 24, 40),   // RANK 0 - Very Hard
        (15, 30, 60),  // RANK 1 - Hard
        (18, 40, 100), // RANK 2 - Normal (default)
        (21, 60, 120), // RANK 3 - Easy
        (21, 60, 200), // RANK 4 - Very Easy
    ];

    // ── Beatoraja windows (reference, not used) ──────────────────────────────
    // Indexed by RANK 0-4: (pgreat, great, good).
    // BAD window is asymmetric in Beatoraja; only the tighter (early) side is stored here
    // as a symmetric approximation — the real implementation would need separate early/late fields.
    //
    // private static readonly (double pgreat, double great, double good)[] rankWindowsBeatoraja =
    // [
    //     (5,  15,  37.5),  // RANK 0 - Very Hard
    //     (10, 30,  75),    // RANK 1 - Hard
    //     (15, 45,  112.5), // RANK 2 - Normal (default)
    //     (20, 60,  150),   // RANK 3 - Easy
    //     (25, 75,  187),   // RANK 4 - Very Easy
    // ];
    // Beatoraja BAD (early / late) per rank:
    //   RANK 0: −385 / +490 ms
    //   RANK 1: −330 / +420 ms
    //   RANK 2: −275 / +350 ms
    //   RANK 3: −200 / +280 ms
    //   RANK 4: −350 / +275 ms
    // Beatoraja Empty POOR: early = −500 ms, late = +150 ms (all ranks).

    private const double bad_poor_window = 200; // LR2 BAD / POOR outer boundary (late side)

    /// <summary>
    ///     Early POOR window: a keypress between −<see cref="bad_poor_window"/> ms and
    ///     −<see cref="early_poor_window"/> ms <em>before</em> the note consumes the note as
    ///     <see cref="HitResult.Miss"/> (EARLY POOR / combo break).
    ///     Presses earlier than −<see cref="early_poor_window"/> ms return
    ///     <see cref="HitResult.None"/> so the playfield can register an Empty POOR instead.
    /// </summary>
    private const double early_poor_window = 1000; // ms before the note head

    private readonly int rank;
    private double pgreat;
    private double great;
    private double good;

    /// <summary>
    ///     Creates a <see cref="BmsHitWindows"/> for the given BMS <paramref name="rank"/>.
    /// </summary>
    /// <param name="rank">The chart's <c>#RANK</c> value (0–4). Values outside this range are clamped to 2 (Normal).</param>
    public BmsHitWindows(int rank = 2)
    {
        this.rank = System.Math.Clamp(rank, 0, 4);
    }

    public override bool IsHitResultAllowed(HitResult result) => result switch
    {
        HitResult.Perfect or HitResult.Great or HitResult.Good
            or HitResult.Ok or HitResult.Meh or HitResult.Miss => true,
        _ => false,
    };

    /// <inheritdoc />
    /// <summary>
    ///     Applies timing windows from the stored <c>#RANK</c>.
    ///     The <paramref name="difficulty" /> (OD) parameter is ignored; BMS timing is driven entirely by <c>#RANK</c>.
    /// </summary>
    public override void SetDifficulty(double difficulty)
    {
        // Using LR2 windows. Swap rankWindowsLr2 → rankWindowsBeatoraja to switch implementations.
        var (p, g, gd) = rank_windows_lr2[rank];
        pgreat = p;
        great = g;
        good = gd;
    }

    public override double WindowFor(HitResult result) => result switch
    {
        HitResult.Perfect => pgreat,
        HitResult.Great => great,
        HitResult.Good => good,
        HitResult.Ok => bad_poor_window,   // BAD
        HitResult.Meh => bad_poor_window,  // POOR (normal, passive miss)
        HitResult.Miss => bad_poor_window, // EARLY POOR / auto-miss
        _ => 0,
    };

    /// <summary>
    ///     BMS-native asymmetric result lookup. Use this instead of the base
    ///     <c>ResultFor</c> everywhere in BMS gameplay code.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         BMS timing is <b>not symmetric</b>. The early side has an extended
    ///         "EARLY POOR" zone that the late side does not:
    ///         <list type="bullet">
    ///             <item>Late press (+offset): normal windows up to +<see cref="bad_poor_window"/> ms,
    ///             then <see cref="HitResult.None"/> (note already passively missed).</item>
    ///             <item>Early press (−offset, small): same normal windows down to −<see cref="bad_poor_window"/> ms.</item>
    ///             <item>Early press (−offset, large): between −<see cref="bad_poor_window"/> and
    ///             −<see cref="early_poor_window"/> ms → <see cref="HitResult.Miss"/> (EARLY POOR,
    ///             consumes the note).</item>
    ///             <item>Earlier than −<see cref="early_poor_window"/> ms →
    ///             <see cref="HitResult.None"/> (Empty POOR zone; note not consumed).</item>
    ///         </list>
    ///     </para>
    /// </remarks>
    public HitResult BmsResultFor(double timeOffset)
    {
        if (timeOffset < -early_poor_window)
            return HitResult.None; // Too early even for Early POOR — Empty POOR territory.

        if (timeOffset < -bad_poor_window)
            return HitResult.Miss; // Early POOR zone: consumes the note as Miss.

        // Within the normal ±bad_poor_window range: use the standard descending search,
        // but on the absolute offset so early and late are symmetric within this zone.
        var abs = System.Math.Abs(timeOffset);
        for (var result = HitResult.Perfect; result >= HitResult.Miss; --result)
        {
            if (IsHitResultAllowed(result) && abs <= WindowFor(result))
                return result;
        }

        // Late press beyond +bad_poor_window: None (passive miss path handles this).
        return HitResult.None;
    }
}
