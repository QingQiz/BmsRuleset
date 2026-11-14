using System;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;

/// <summary>Freezes imported chart and mod values into the standalone engine's contracts.</summary>
internal static class BmsEngineChart
{
    internal static Chart Compile(BmsHitObject[] notes, int columns)
    {
        return new Chart(columns, notes.Select((note, id) =>
        {
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

            var rule = BmsEngineRules.ForBeatmap(note.Beatmap).GetProfile(note.EffectiveJudgementRate).CreateRule(source);
            return new ChartNote(id, source.Lane, source.StartTime, source.EndTime, rule);
        }));
    }

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
