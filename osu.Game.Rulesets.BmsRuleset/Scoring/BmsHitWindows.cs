using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Scoring;

/// <inheritdoc />
/// <summary>
///     Native BMS hit windows driven by the chart's <c>#RANK</c> value.
/// </summary>
/// <remarks>
///     <para>
///         BMS five-tier judgement mapped to osu! <see cref="T:osu.Game.Rulesets.Scoring.HitResult">HitResult</see> values:
///         <list type="table">
///             <item><term>PGREAT → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Perfect">HitResult.Perfect</see></term><description>Tightest window; 2 EX points.</description></item>
///             <item><term>GREAT  → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Great">HitResult.Great</see></term> <description>1 EX point; no combo break.</description></item>
///             <item><term>GOOD   → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Good">HitResult.Good</see></term>  <description>0 EX points; no combo break.</description></item>
///             <item><term>BAD    → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Ok">HitResult.Ok</see></term>    <description>0 EX points; breaks combo.</description></item>
///             <item><term>POOR   → <see cref="F:osu.Game.Rulesets.Scoring.HitResult.Meh">HitResult.Meh</see></term>  <description>0 EX points; breaks combo. Two causes: (1) passive miss — note's BAD window expired with no keypress; (2) in-range keypress — key pressed in the POOR zone (+BAD..+poor_window ms before the note), consuming the note. Both display as "POOR".</description></item>
///         </list>
///         Empty POOR (空POOR): keypress with no note in any window — handled separately via <c>RegisterEmptyPoor</c>; no <see cref="T:osu.Game.Rulesets.Judgements.JudgementResult"/> is created.
///         <see cref="T:osu.Game.Rulesets.Scoring.HitResult.Miss"/> is not used for note judgements in BMS.
///     </para>
///     <para>
///         Two reference implementations exist for BMS timing windows. <b>Currently using LR2.</b>
///     </para>
///     <para>
///         <b>Lunatic Rave 2 (LR2)</b> — symmetric ±ms windows per <c>#RANK</c>:
///         <list type="table">
///             <listheader><term>RANK</term><description>PGREAT / GREAT / GOOD / BAD (all ±ms). POOR zone extends from ±BAD to +poor_window ms before the note (LR2: +1000 ms). A keypress in the POOR zone consumes the note as POOR. Earlier than +poor_window ms → Empty POOR territory (keypress does not consume note).</description></listheader>
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

    /// <summary>BAD window half-width (±ms). Also the boundary for passive POOR and the POOR zone outer edge.</summary>
    public const double BAD_WINDOW = 200; // LR2 BAD outer boundary (±ms)

    private const double bad_poor_window = BAD_WINDOW;

    /// <summary>
    ///     POOR zone: a keypress between +<see cref="bad_poor_window"/> ms and
    ///     +<see cref="poor_window"/> ms <em>before</em> the note (positive = before note in LR2 convention)
    ///     consumes the note as POOR (<see cref="HitResult.Meh"/>).
    ///     Presses earlier than +<see cref="poor_window"/> ms return
    ///     <see cref="HitResult.None"/> so the playfield registers an Empty POOR instead.
    /// </summary>
    private const double poor_window = 1000; // ms before the note head (LR2 空PR = +1000)

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
            or HitResult.Ok or HitResult.Meh => true,
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
        HitResult.Ok => bad_poor_window,  // BAD
        HitResult.Meh => bad_poor_window, // POOR (passive miss: auto-judged after BAD window expires)
        _ => 0,
    };

    /// <summary>
    ///     BMS-native asymmetric result lookup for a user keypress. Use this instead of the base
    ///     <c>ResultFor</c> everywhere in BMS gameplay code.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         LR2 timing is <b>asymmetric on the early side</b>. The POOR zone extends further
    ///         before the note than the BAD window does, consuming the note without giving BAD credit:
    ///         <list type="bullet">
    ///             <item>Offset within ±<see cref="bad_poor_window"/> ms: normal PGREAT/GREAT/GOOD/BAD by tightest matching window.</item>
    ///             <item>Early press beyond −<see cref="bad_poor_window"/> ms up to −<see cref="poor_window"/> ms (POOR zone):
    ///             consumes the note as POOR (<see cref="HitResult.Meh"/>).</item>
    ///             <item>Earlier than −<see cref="poor_window"/> ms: <see cref="HitResult.None"/> —
    ///             note is not consumed; playfield registers an Empty POOR instead.</item>
    ///             <item>Late press beyond +<see cref="bad_poor_window"/> ms: <see cref="HitResult.None"/> —
    ///             note has already been passively missed; passive POOR is applied by <c>CheckForResult</c>.</item>
    ///         </list>
    ///         <b>Note on sign convention</b>: <paramref name="timeOffset"/> = <c>Time.Current − note.StartTime</c>.
    ///         Negative = keypress before the note. Positive = keypress after the note.
    ///     </para>
    /// </remarks>
    public HitResult BmsResultFor(double timeOffset)
    {
        if (timeOffset < -poor_window)
            return HitResult.None; // Too early — Empty POOR territory; note not consumed.

        if (timeOffset < -bad_poor_window)
            return HitResult.Meh; // POOR zone: keypress before note outside BAD window; consumes note as POOR.

        // Within the normal ±bad_poor_window range: descending search on absolute offset
        // so early and late are symmetric within this zone.
        var abs = System.Math.Abs(timeOffset);
        for (var result = HitResult.Perfect; result >= HitResult.Meh; --result)
        {
            if (IsHitResultAllowed(result) && abs <= WindowFor(result))
                return result;
        }

        // Late press beyond +bad_poor_window: None (passive POOR applied by CheckForResult).
        return HitResult.None;
    }
}
