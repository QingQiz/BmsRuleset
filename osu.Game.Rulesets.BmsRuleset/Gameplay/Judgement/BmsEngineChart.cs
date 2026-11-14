using System;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>Freezes imported chart and mod values into the standalone engine's contracts.</summary>
internal static class BmsEngineChart
{
    internal static Chart Compile(BmsHitObject[] notes, int columns)
        => new(columns, notes.Select((note, id) =>
        {
            var head = BmsJudgementProfileProvider.GetTable(note, false);
            var tail = BmsJudgementProfileProvider.GetTable(note, true);
            var behaviour = BmsJudgementProfileProvider.GetBehaviour(note.Beatmap.LayoutVariant);
            var longNote = note as BmsLongNote;
            var kind = note switch
            {
                BmsLandmine => KeyNoteKind.Mine,
                BmsLongNote hold => hold.EffectiveMode switch
                {
                    BmsLongNoteMode.ChargeNote => KeyNoteKind.ReleaseHold,
                    BmsLongNoteMode.HellChargeNote => KeyNoteKind.SustainHold,
                    _ => KeyNoteKind.Hold,
                },
                _ => KeyNoteKind.Tap,
            };
            var source = new KeyNote(id, note.Column, Time.FromMilliseconds(note.StartTime),
                Time.FromMilliseconds(longNote?.EndTime ?? note.StartTime), kind,
                Directional: BmsLayout.IsScratchColumn(note.Column, note.Beatmap.LayoutVariant)
                             && note.Beatmap.LayoutVariant is not (BmsLayoutVariant.Keyboard24K or BmsLayoutVariant.Keyboard24KDouble));
            var headDeadline = kind == KeyNoteKind.Hold && head.LongNoteHeadPoorOffset is { } deadline
                ? Math.Min(deadline, longNote!.Duration)
                : head.SlowWindowFor(HitResult.Ok);
            var options = new BmsRuleOptions(ImmediateBadHead: behaviour.LongNoteBadIsImmediate,
                NonConsumingBad: behaviour.NonConsumingBad, HeadDeadline: Time.FromMilliseconds(headDeadline),
                TailDeadline: Time.FromMilliseconds(head.SlowWindowFor(HitResult.Ok)),
                ReleaseGrace: Time.FromMilliseconds(behaviour.LongNoteReleaseMargin), MineOrder: TimerOrder.AfterInput,
                PassiveOrder: TimerOrder.AfterInput);
            return new ChartNote(id, source.Lane, source.StartTime, source.EndTime,
                BmsProfiles.CreateRule(source, windows(head, behaviour.SearchWindow), windows(tail, behaviour.SearchWindow), options));
        }));

    private static TimingWindows windows(BmsJudgementWindowTable table, double search)
        => new(table.HitWindows.Select(row => new TimingWindow(ResultId(row.Result),
                Time.FromMilliseconds(-row.FastDTime), Time.FromMilliseconds(-row.SlowDTime)))
            .Concat(table.EPoorWindow is { } empty
                ?
                [
                    new TimingWindow(BmsResults.EMPTY_POOR, Math.Max(Time.FromMilliseconds(-empty.FastDTime), -Time.FromMilliseconds(search) + 1),
                        Time.FromMilliseconds(-empty.SlowDTime))
                ]
                : []));

    internal static ResultId ResultId(HitResult result) => result switch
    {
        HitResult.Perfect => BmsResults.PGREAT, HitResult.Great => BmsResults.GREAT, HitResult.Good => BmsResults.GOOD,
        HitResult.Ok => BmsResults.BAD, HitResult.Meh => BmsResults.POOR, HitResult.Miss => BmsResults.EMPTY_POOR,
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    internal static HitResult HitResultFor(ResultId? result) => result switch
    {
        null => HitResult.None,
        var id when id == BmsResults.PGREAT => HitResult.Perfect,
        var id when id == BmsResults.GREAT => HitResult.Great,
        var id when id == BmsResults.GOOD => HitResult.Good,
        var id when id == BmsResults.BAD => HitResult.Ok,
        var id when id == BmsResults.POOR || id == BmsResults.MINE_HIT => HitResult.Meh,
        var id when id == BmsResults.EMPTY_POOR => HitResult.Miss,
        var id when id == BmsResults.MINE_AVOIDED || id == BmsResults.SUSTAIN_GAIN || id == BmsResults.SUSTAIN_LOSS => HitResult.None,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "The BMS adapter requires a BMS result identity."),
    };
}
