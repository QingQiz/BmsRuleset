using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

internal sealed partial class BmsGameplayProfile : GameplayProfile
{
    internal static readonly BmsGameplayProfile INSTANCE = new();

    private readonly BmsJudgementModifiers modifiers;

    internal BmsGameplayProfile(BmsJudgementModifiers modifiers = BmsJudgementModifiers.None)
    {
        this.modifiers = modifiers;
    }

    internal BmsGameplayProfile WithJudgementModifier(BmsJudgementModifiers modifier) => new(modifiers | modifier);

    internal override GameplayHealth CreateHealthModel(BindableDouble health) => new BmsGaugeController(health, this);

    internal BmsGaugeProfile CreateGaugeProfile(BmsGaugeType type, BmsGaugeProfileFamily family)
        => (modifiers & BmsJudgementModifiers.Lr2oraja) != 0
            ? BmsGaugeProfiles.CreateLr2Oraja(BmsGaugeProfileFactory.ToEngineGauge(type))
            : BmsGaugeProfileFactory.Create(type, family);

    internal override double RatingWindowFor(BmsBeatmap beatmap)
    {
        // Result constraints must not remove the rating model's base GREAT tolerance.
        var ratingModifiers = modifiers & BmsJudgementModifiers.Lr2oraja;
        var rate = BmsEngineRules.ForLayout(beatmap.LayoutVariant, ratingModifiers).ResolveRate(beatmap.Rank, beatmap.ExRank);
        var column = BmsLayout.IsScratchColumn(0, beatmap.LayoutVariant) ? 1 : 0;
        return BmsEngineRules.GetTiming(beatmap.LayoutVariant, column, rate, false, ratingModifiers).FrameworkWindowFor(HitResult.Great);
    }

    internal override bool ShowHitEffect(HitResult result) => result != HitResult.Meh && result.IsHit();

    internal override Drawable CreateJudgementDrawable(HitResult result)
        => new SkinnableDrawable(new SkinComponentLookup<HitResult>(result))
        {
            RelativeSizeAxes = Axes.None,
            AutoSizeAxes = Axes.Both,
        };

    internal override IReadOnlyList<GameplayHealthHistoryEvent> ReconstructHealthHistory(ScoreInfo score, IBeatmap beatmap, IReadOnlyList<HitEvent> events)
        => BmsLegacyGaugeHistory.Reconstruct(score, beatmap, events);

    internal override GameplayEventKind EventKind(JudgementEvent result) => result switch
    {
        { Part: var part } when part == NoteParts.EMPTY => result.Result == BmsResults.BAD ? GameplayEventKind.NonConsumingHit : GameplayEventKind.EmptyInput,
        { Result: var id } when id == BmsResults.MINE_HIT => GameplayEventKind.MineHit,
        { Result: var id } when id == BmsResults.MINE_AVOIDED => GameplayEventKind.MineAvoided,
        _ => base.EventKind(result),
    };

    internal override bool IncludeHeadOnly(NotePartState tail) => tail.Reason == HoldReasons.MISSED_HEAD || tail.Reason == HoldReasons.HEAD;

    internal override bool PlayTailSample(NotePartState tail, InputEvent? input)
        => input != null && (tail.Reason == HoldReasons.RELEASE || tail.Reason == HoldReasons.DROP);

    internal override ResultTimingKind TimingKindFor(HitResult result) => result switch
    {
        HitResult.Meh => ResultTimingKind.Untimed,
        HitResult.Miss => ResultTimingKind.EmptyInput,
        _ => base.TimingKindFor(result),
    };

    // Historical BMS fast/slow totals include POOR's direction, although averages exclude it.
    internal override bool CountsForFastSlow(HitResult result) => result.IsHit();

    internal override (Chart Chart, SessionConfiguration Configuration) Compile(BmsHitObject[] objects, KeyChart source, IBeatmapInfo beatmapInfo)
    {
        var chart = new Chart(source.LaneCount, source.Notes.Select(note => new ChartNote(note.Id, note.Lane, note.StartTime, note.EndTime,
                BmsEngineRules.ForBeatmap(objects[note.Id].Beatmap, modifiers).GetProfile(BmsEngineRules.RateFor(objects[note.Id], modifiers)).CreateRule(note))),
            source.Notes.SelectMany(note => BmsProfiles.CreateScoringOpportunities(note.Id, note.Kind)));
        var rules = objects.Length == 0 ? BmsJudgementRules.For(BmsJudgementMode.SevenKey, modifiers) : BmsEngineRules.ForBeatmap(objects[0].Beatmap, modifiers);
        return (chart, new SessionConfiguration("bms-ruleset-v1", BmsProfiles.CreateScore(rules.EmptyPoorBreaksCombo),
            BmsEngineGauge.CreateDefault(objects, chart.ScorePlan, this), context => BmsProfiles.SelectCandidates(context, chart, rules.SelectionOverride ?? BmsSelection.Combo),
            BmsProfiles.CreateCompletion(source, objects.Select(note => BmsEngineRules.GetTiming(note, false, modifiers).PassiveDeadline))));
    }

    internal override SessionConfiguration ConfigureSession(BmsEngineChart compiled, BmsJudgementAlgorithm? algorithm, HealthConfiguration health)
    {
        var rules = compiled.Objects.Length == 0 ? BmsJudgementRules.For(BmsJudgementMode.SevenKey, modifiers) : BmsEngineRules.ForBeatmap(compiled.Objects[0].Beatmap, modifiers);
        var selection = rules.SelectionOverride ?? algorithm switch
        {
            BmsJudgementAlgorithm.Duration => BmsSelection.Nearest,
            BmsJudgementAlgorithm.Lowest => BmsSelection.Oldest,
            BmsJudgementAlgorithm.Score => BmsSelection.Score,
            _ => BmsSelection.Combo,
        };
        return compiled.Configuration with { Health = health, SelectCandidates = context => BmsProfiles.SelectCandidates(context, compiled.Chart, selection) };
    }

    internal static ResultId ResultIdFor(HitResult result) => result switch
    {
        HitResult.Perfect => BmsResults.PGREAT, HitResult.Great => BmsResults.GREAT, HitResult.Good => BmsResults.GOOD,
        HitResult.Ok => BmsResults.BAD, HitResult.Meh => BmsResults.POOR, HitResult.Miss => BmsResults.EMPTY_POOR,
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    internal override HitResult HitResultFor(ResultId? result) => result switch
    {
        null => HitResult.None,
        var id when id == BmsResults.PGREAT => HitResult.Perfect,
        var id when id == BmsResults.GREAT => HitResult.Great,
        var id when id == BmsResults.GOOD => HitResult.Good,
        var id when id == BmsResults.BAD => HitResult.Ok,
        var id when id == BmsResults.POOR || id == BmsResults.MINE_HIT => HitResult.Meh,
        var id when id == BmsResults.EMPTY_POOR => HitResult.Miss,
        var id when id == BmsResults.MINE_AVOIDED || id == BmsResults.SUSTAIN_GAIN || id == BmsResults.SUSTAIN_LOSS => HitResult.None,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, @"Undefined BMS result."),
    };

    // Stored BMS endpoint offsets have always used the source head's framework clamp.
    // Actual release judgement still uses the engine's tail windows.
    internal override HitWindows CreateHitWindows(BmsHitObject source, NoteTiming timing, PartId part)
    {
        var head = BmsEngineRules.GetTiming(source, false, modifiers);
        return new BmsHitWindows(head.Windows, head.PassiveDeadline, this);
    }

    // BMS historically exposes its passive deadline for BAD/POOR/empty POOR to framework consumers.
    internal override double FrameworkWindowFor(TimingWindows windows, long deadline, HitResult result)
        => result is HitResult.Ok or HitResult.Meh or HitResult.Miss
            ? deadline / 1000.0
            : base.FrameworkWindowFor(windows, deadline, result);

    internal override int AccuracyValue(HitResult result)
        => result is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh
            ? (int)BmsProfiles.GetResultValue(ResultIdFor(result)).Accuracy
            : 0;

    internal override int AccuracyJudgementCount(ScoreState score)
        => score.JudgedOpportunities + score.ResultCounts.GetValueOrDefault(BmsResults.MINE_HIT);

    internal override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
        => ToRank(BmsProfiles.GradeFor(accuracy, results.Where(pair => pair.Key is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss)
            .ToDictionary(pair => ResultIdFor(pair.Key), pair => pair.Value)));

    internal override double AccuracyCutoffFromRank(ScoreRank rank) => BmsProfiles.Grades.CutoffFor(ToGrade(rank));
}
