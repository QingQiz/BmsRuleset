using System;
using System.Collections.Generic;
using System.Linq;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;

/// <summary>The host boundary for a complete engine profile and its osu! result representation.</summary>
internal abstract partial class GameplayProfile
{
    internal virtual GameplayHealth CreateHealthModel(BindableDouble health) => new(health);

    internal static GameplayProfile FromMods(IEnumerable<Mod> mods)
        => mods.OfType<IApplicableToGameplayProfile>().Aggregate((GameplayProfile)BmsGameplayProfile.INSTANCE,
            (profile, mod) => mod.ApplyToGameplayProfile(profile));

    internal virtual IReadOnlyList<GameplayHealthHistoryEvent> ReconstructHealthHistory(ScoreInfo score, IBeatmap beatmap, IReadOnlyList<HitEvent> events) => [];

    internal abstract (Chart Chart, SessionConfiguration Configuration) Compile(BmsHitObject[] objects, KeyChart source);

    internal virtual SessionConfiguration ConfigureSession(BmsEngineChart compiled, BmsJudgementAlgorithm? algorithm, HealthConfiguration health)
        => compiled.Configuration with { Health = health };

    internal virtual void AdjustDisplayDifficulty(BeatmapDifficulty difficulty)
    {
    }

    internal abstract IEnumerable<RulesetBeatmapAttribute> BeatmapAttributes(IBeatmapInfo beatmapInfo, IReadOnlyCollection<Mod> mods, BeatmapDifficulty adjustedDifficulty);

    internal virtual double RatingWindowFor(BmsBeatmap beatmap, BmsEngineChart compiled)
    {
        var note = compiled.Objects[compiled.Chart.ScorePlan.Opportunities[0].NoteId];
        var timing = compiled.TimingFor(note);
        return FrameworkWindowFor(timing.HeadWindows, timing.HeadDeadline, HitResult.Great);
    }

    internal abstract HitResult HitResultFor(ResultId? result);

    // HitEvent stores a framework grade and an offset, including for passive results.
    // Interpret that historical representation using the profile that produced it.
    internal virtual ResultTimingKind TimingKindFor(HitResult result)
        => result.IsHit() ? ResultTimingKind.Timed : ResultTimingKind.Untimed;

    internal virtual bool CountsForFastSlow(HitResult result) => TimingKindFor(result) == ResultTimingKind.Timed;

    internal bool IsTimingEvent(HitEvent hit)
        => hit.Result.IsBasic() && hit.HitObject is not BmsLandmine
                                && (hit.HitObject is BmsHitObject || TimingKindFor(hit.Result) == ResultTimingKind.EmptyInput);

    internal virtual HitWindows CreateHitWindows(BmsHitObject source, NoteTiming timing, PartId part)
        => part == NoteParts.TAIL
            ? new BmsHitWindows(timing.TailWindows!, timing.TailDeadline, this)
            : new BmsHitWindows(timing.HeadWindows, timing.HeadDeadline, this);

    internal virtual double FrameworkWindowFor(TimingWindows windows, long deadline, HitResult result)
    {
        if (result == HitResult.Miss)
            return deadline / 1000.0;

        foreach (var window in windows.Entries)
        {
            if (HitResultFor(window.Result) == result)
                return Math.Max(0, Math.Min(-window.Early, window.Late)) / 1000.0;
        }

        return 0;
    }

    internal virtual GameplayEventKind EventKind(JudgementEvent result)
        => result.Part == NoteParts.BODY ? GameplayEventKind.Auxiliary : GameplayEventKind.Note;

    internal virtual bool IncludeHeadOnly(NotePartState tail) => false;

    internal virtual bool PlayTailSample(NotePartState tail, InputEvent? input) => input is { Pressed: false };

    internal virtual BmsLongNotePresentation LongNotePresentation(BmsLongNote note, NoteSnapshot snapshot, in BmsLongNotePresentationContext context)
    {
        var head = snapshot.Parts.Single(part => part.Part == NoteParts.HEAD);
        var tail = snapshot.Parts.Single(part => part.Part == NoteParts.TAIL);
        var body = snapshot.Parts.Single(part => part.Part == NoteParts.BODY);
        return new BmsLongNotePresentation(head.Resolved, body.Active && context.Held, true, !tail.Resolved,
            tail.Resolved, true, note.EndTime + context.Timing.TailDeadline / 1000.0 + 100, body.Active && context.Held)
        {
            AnimateBody = body.Active,
            ReleasedStyle = head.Resolved && !body.Active,
            PrepareHeadPin = head.Resolved && !tail.Resolved,
            RestoreHeadPin = body.Active,
            HeadReached = context.Time >= note.StartTime,
            KeepVisibleAfterHit = !tail.Resolved,
        };
    }

    internal abstract int AccuracyValue(HitResult result);

    internal virtual int AccuracyJudgementCount(ScoreState score) => score.JudgedOpportunities;

    internal abstract ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results);

    internal abstract double AccuracyCutoffFromRank(ScoreRank rank);

    internal abstract LocalisableString ScoreLabel { get; }

    internal abstract double? StoredScore(ScoreInfo score, double? knownMaximum = null);

    internal abstract double? StoredMaximumScore(ScoreInfo score);

    // A grade based on accuracy need not have a corresponding total-score threshold.
    internal virtual double? ScoreForRank(ScoreRank rank, double maximum) => null;

    internal abstract LocalisableString RankLabel(ScoreRank rank);

    internal abstract LocalisableString JudgementLabel(HitResult result);

    internal virtual bool ShowHitEffect(HitResult result) => result.IsHit();

    internal virtual Drawable CreateJudgementDrawable(HitResult result)
        => new BmsJudgementPiece(!result.IsHit(), new OsuSpriteText
        {
            Text = JudgementLabel(result),
            Font = OsuFont.Default.With(size: 32, weight: FontWeight.Bold),
            Colour = BmsHitResultColours.ForHitResult(result),
        });

    internal abstract BmsLamp LampFor(ScoreInfo score);

    internal static ScoreRank ToRank(ScoreGrade grade) => grade switch
    {
        ScoreGrade.Perfect => ScoreRank.X,
        ScoreGrade.S => ScoreRank.S,
        ScoreGrade.A => ScoreRank.A,
        ScoreGrade.B => ScoreRank.B,
        ScoreGrade.C => ScoreRank.C,
        ScoreGrade.D => ScoreRank.D,
        _ => throw new ArgumentOutOfRangeException(nameof(grade)),
    };

    internal static ScoreGrade ToGrade(ScoreRank rank) => rank switch
    {
        ScoreRank.X or ScoreRank.XH => ScoreGrade.Perfect,
        ScoreRank.S or ScoreRank.SH => ScoreGrade.S,
        ScoreRank.A => ScoreGrade.A,
        ScoreRank.B => ScoreGrade.B,
        ScoreRank.C => ScoreGrade.C,
        ScoreRank.D => ScoreGrade.D,
        _ => throw new ArgumentOutOfRangeException(nameof(rank)),
    };
}

internal enum GameplayEventKind
{
    Note,
    Auxiliary,
    EmptyInput,
    NonConsumingHit,
    MineHit,
    MineAvoided,
}

internal enum ResultTimingKind
{
    Timed,
    Untimed,
    EmptyInput,
}
