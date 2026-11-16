using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Mania;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

public partial class BmsModManiaProfile : Mod, IApplicableToGameplayProfile, IApplicableAfterBeatmapConversion, IApplicableToScoreSelection
{
    private const float default_difficulty = 8;

    public override string Name => BmsStrings.ModManiaProfileName.ToString();

    public override string Acronym => "OM";

    public override LocalisableString Description => BmsStrings.ModManiaProfile;

    public override ModType Type => ModType.Fun;

    public override IconUsage? Icon => OsuIcon.RulesetMania;

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ManiaOverallDifficulty), nameof(BmsStrings.ManiaDifficultyDescription), SettingControlType = typeof(ManiaDifficultySettingsControl))]
    public DifficultyBindable OverallDifficulty { get; } = new()
    {
        Precision = 0.1f,
        ReadCurrentFromDifficulty = difficulty => difficulty.OverallDifficulty,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ManiaDrainRate), nameof(BmsStrings.ManiaDifficultyDescription), SettingControlType = typeof(ManiaDifficultySettingsControl))]
    public DifficultyBindable DrainRate { get; } = new()
    {
        Precision = 0.1f,
        ReadCurrentFromDifficulty = difficulty => difficulty.DrainRate,
    };

    [SettingSource(typeof(BmsStrings), nameof(BmsStrings.ManiaUseLazer), nameof(BmsStrings.ManiaUseLazerDescription))]
    public BindableBool UseLazer { get; } = new(true);

    public override Type[] IncompatibleMods =>
    [
        typeof(BmsModGauge), typeof(BmsModAutoGauge), typeof(BmsModLr2), typeof(BmsModNoBad),
        typeof(BmsModNoGood), typeof(BmsModNoGreat), typeof(BmsModLongNoteModeBase),
    ];

    private static readonly IScoreSelectionRule score_selection_rule = new ManiaScoreSelectionRule();

    public IScoreSelectionRule ScoreSelectionRule => score_selection_rule;

    private sealed class ManiaScoreSelectionRule : IScoreSelectionRule
    {
        public bool IsScoreEligible(IReadOnlyList<Mod> scoreMods, IReadOnlyList<Mod> selectedMods)
            => EqualityComparer<Mod?>.Default.Equals(scoreMods.OfType<BmsModManiaProfile>().SingleOrDefault(), selectedMods.OfType<BmsModManiaProfile>().SingleOrDefault());
    }

    GameplayProfile IApplicableToGameplayProfile.ApplyToGameplayProfile(GameplayProfile profile)
        => new ManiaGameplayProfile(OverallDifficulty.Value, DrainRate.Value, UseLazer.Value);

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        var bms = (BmsBeatmap)beatmap;
        // Conversion mods can create more holds after this profile has normalised the existing objects.
        bms.LockedLongNoteMode = BmsLongNoteMode.LongNote;
        bms.HitObjects.RemoveAll(note => note is BmsLandmine);

        foreach (var note in bms.HitObjects)
        {
            if (note is BmsLongNote hold)
                hold.Kind = KeyNoteKind.Hold;
        }
    }

    private partial class ManiaDifficultySettingsControl : DifficultyAdjustSettingsControl
    {
        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        private DifficultyBindable? source;
        private Func<IBeatmapDifficultyInfo, float> readOriginal = null!;
        private float currentDefault = default_difficulty;

        public override Bindable<float?> Current
        {
            get => base.Current;
            set
            {
                source?.UnbindAll();
                source = (DifficultyBindable)value.GetBoundCopy();
                readOriginal = source.ReadCurrentFromDifficulty!;
                source.ReadCurrentFromDifficulty = _ => currentDefault;
                base.Current = source;
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            beatmap.BindValueChanged(change =>
            {
                // Bound copies notify in different orders; the event owns the new source metadata.
                currentDefault = change.NewValue.BeatmapInfo.Ruleset.OnlineID == 3
                    ? readOriginal(change.NewValue.BeatmapInfo.Difficulty)
                    : default_difficulty;
                Current.TriggerChange();
            }, true);
        }

        protected override void Dispose(bool isDisposing)
        {
            source?.UnbindAll();
            base.Dispose(isDisposing);
        }
    }

    private sealed class ManiaGameplayProfile(float? overallDifficulty, float? drainRate, bool useLazer) : GameplayProfile
    {
        private static float originalOd(IBeatmapInfo beatmapInfo)
            => beatmapInfo is { Ruleset.OnlineID: 3 } ? beatmapInfo.Difficulty.OverallDifficulty : default_difficulty;

        private static float originalHp(IBeatmapInfo beatmapInfo)
            => beatmapInfo is { Ruleset.OnlineID: 3 } ? beatmapInfo.Difficulty.DrainRate : default_difficulty;

        internal override void AdjustDisplayDifficulty(IBeatmapInfo beatmapInfo, BeatmapDifficulty difficulty)
        {
            difficulty.OverallDifficulty = overallDifficulty ?? originalOd(beatmapInfo);
            difficulty.DrainRate = drainRate ?? originalHp(beatmapInfo);
        }

        internal override IEnumerable<RulesetBeatmapAttribute> BeatmapAttributes(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods, BeatmapDifficulty adjustedDifficulty)
        {
            yield return new RulesetBeatmapAttribute(BmsStrings.DifficultyAttributeLabel("od"), BmsStrings.DifficultyAttributeAcronym("od").ToString(),
                originalOd(beatmapInfo), adjustedDifficulty.OverallDifficulty, 10);
            yield return new RulesetBeatmapAttribute(BmsStrings.DifficultyAttributeLabel("hp"), BmsStrings.DifficultyAttributeAcronym("hp").ToString(),
                originalHp(beatmapInfo), adjustedDifficulty.DrainRate, 10);
        }

        internal override INoteRule AutoplayRule(INoteRule rule) => useLazer ? ((OrderedNoteRule)rule).Inner : rule;

        internal override IEnumerable<KeyValuePair<HitResult, int>> MaximumAuxiliaryResults(ChartNote note)
            => !useLazer && ManiaProfiles.GetStableHoldTicks(note) is > 0 and var ticks ? [new(HitResult.LargeTickHit, ticks)] : [];

        internal override double RatingWindowFor(BmsBeatmap beatmap)
            => ManiaProfiles.CreateHitWindows(new ManiaOptions(overallDifficulty ?? originalOd(beatmap.BeatmapInfo)), !useLazer)
                .Entries.Single(window => window.Result == ManiaResults.GREAT).Late / 1000.0;

        internal override (Chart Chart, SessionConfiguration Configuration) Compile(BmsHitObject[] objects, KeyChart source, IBeatmapInfo beatmapInfo)
        {
            var options = new ManiaOptions(overallDifficulty ?? originalOd(beatmapInfo), drainRate ?? originalHp(beatmapInfo));
            var profile = useLazer ? ManiaProfiles.CreateLazer(source, options) : ManiaProfiles.CreateStable(source, options);
            return (profile.Chart, profile.Configuration);
        }

        internal override IEnumerable<BmsAutoGenerator.ActionPoint> GenerateAutoplayActions(BmsBeatmap beatmap, BmsEngineChart compiled)
        {
            if (useLazer)
                return base.GenerateAutoplayActions(beatmap, compiled);

            return compiled.Objects.GroupBy(note => note.Column).SelectMany(column =>
            {
                var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, column.Key)!.Value;
                var notes = column.OrderBy(note => note.StartTime).ToArray();
                return notes.SelectMany((note, index) =>
                {
                    var next = index + 1 < notes.Length ? notes[index + 1].StartTime : double.PositiveInfinity;
                    var release = note is BmsLongNote hold ? hold.EndTime : Math.Min(note.StartTime + 10, note.StartTime + (next - note.StartTime) * 0.9);
                    return new[] { new BmsAutoGenerator.ActionPoint(note.StartTime, action, true), new BmsAutoGenerator.ActionPoint(release, action, false) };
                });
            });
        }

        internal override HitResult HitResultFor(ResultId? result) => result switch
        {
            null => HitResult.None,
            var id when id == ManiaResults.PERFECT => HitResult.Perfect,
            var id when id == ManiaResults.GREAT => HitResult.Great,
            var id when id == ManiaResults.GOOD => HitResult.Good,
            var id when id == ManiaResults.OK => HitResult.Ok,
            var id when id == ManiaResults.MEH => HitResult.Meh,
            var id when id == ManiaResults.MISS => HitResult.Miss,
            var id when id == ManiaResults.COMBO_BREAK => HitResult.None,
            var id when id == ManiaResults.HOLD_TICK => HitResult.LargeTickHit,
            var id when id == ManiaResults.HOLD_BREAK => HitResult.ComboBreak,
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, @"Undefined mania result."),
        };

        private static ResultId resultId(HitResult result) => result switch
        {
            HitResult.Perfect => ManiaResults.PERFECT,
            HitResult.Great => ManiaResults.GREAT,
            HitResult.Good => ManiaResults.GOOD,
            HitResult.Ok => ManiaResults.OK,
            HitResult.Meh => ManiaResults.MEH,
            HitResult.Miss => ManiaResults.MISS,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };

        internal override int AccuracyValue(HitResult result)
            => result is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss
                ? (int)(useLazer ? ManiaProfiles.GetLazerResultValue(resultId(result)) : ManiaProfiles.GetStableResultValue(resultId(result))).Accuracy
                : 0;

        internal override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
            => ToRank(ManiaProfiles.GradeFor(accuracy, results.Where(pair => pair.Key is HitResult.Perfect or HitResult.Great or HitResult.Good or HitResult.Ok or HitResult.Meh or HitResult.Miss)
                .ToDictionary(pair => resultId(pair.Key), pair => pair.Value)));

        internal override double AccuracyCutoffFromRank(ScoreRank rank) => ManiaProfiles.Grades.CutoffFor(ToGrade(rank));

        internal override LocalisableString ScoreLabel => BmsStrings.ResultScore;

        internal override double? StoredScore(ScoreInfo score, double? knownMaximum = null) => score.TotalScoreWithoutMods;

        // Aggregate results sum several standardised scores; ScoreInfo does not store their total maximum.
        internal override double? StoredMaximumScore(ScoreInfo score) => null;

        internal override LocalisableString RankLabel(ScoreRank rank) => BmsStrings.RankLetter(rank switch
        {
            ScoreRank.X or ScoreRank.XH => "ss",
            ScoreRank.S or ScoreRank.SH => "s",
            ScoreRank.A => "a",
            ScoreRank.B => "b",
            ScoreRank.C => "c",
            ScoreRank.D => "d",
            _ => "f",
        });

        internal override LocalisableString JudgementLabel(HitResult result) => BmsStrings.ManiaJudgement(result);

        internal override BmsLamp LampFor(ScoreInfo score)
        {
            if (score.Rank == ScoreRank.F)
                return BmsLamp.Failed;

            // Stable tick totals depend on press/release offsets, so full combo is determined by breaks.
            var maximumCombo = score.GetMaximumAchievableCombo();
            if (maximumCombo == 0 || (useLazer && score.MaxCombo != maximumCombo)
                                  || score.Statistics.GetValueOrDefault(HitResult.ComboBreak) > 0
                                  || score.Statistics.GetValueOrDefault(HitResult.Miss) > 0)
                return BmsLamp.Clear;

            var played = score.Statistics.Where(pair => pair.Value > 0 && pair.Key.IsBasic()).Select(pair => pair.Key).ToArray();
            if (played.Length > 0 && played.All(result => result == HitResult.Perfect))
                return BmsLamp.Max;
            if (played.Length > 0 && played.All(result => result is HitResult.Perfect or HitResult.Great))
                return BmsLamp.Perfect;

            return BmsLamp.FullCombo;
        }
    }
}
