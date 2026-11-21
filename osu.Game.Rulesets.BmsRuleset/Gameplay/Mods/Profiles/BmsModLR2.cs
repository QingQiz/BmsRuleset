using System.Collections.Generic;
using System;
using System.Linq;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Common;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;

public class BmsModLr2 : Mod, IApplicableToGameplayProfile,
                         IApplicableAfterBeatmapConversion, IApplicableToScoreSelection,
                         IApplicableToDifficulty
{
    public override string Name => BmsStrings.ModLr2Name.ToString();

    public override string Acronym => "R2";

    public override LocalisableString Description => BmsStrings.ModLr2;

    public override ModType Type => ModType.Fun;

    public override IconUsage? Icon => BmsIcons.Lr2;

    public override Type[] IncompatibleMods => [typeof(BmsModChargeNote), typeof(BmsModHellChargeNote)];

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModLr2>.EXACT;

    GameplayProfile IApplicableToGameplayProfile.ApplyToGameplayProfile(GameplayProfile profile, IEnumerable<Mod> mods)
        => ((BmsGameplayProfile)profile).WithJudgementModifier(BmsJudgementModifiers.Lr2oraja);

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is BmsBeatmap bmsBeatmap)
        {
            bmsBeatmap.LockedLongNoteMode = BmsLongNoteMode.LongNote;
            foreach (var note in bmsBeatmap.HitObjects.OfType<BmsLongNote>())
                note.Kind = KeyNoteKind.Hold;
        }
    }

    public void ApplyToDifficulty(BeatmapDifficulty difficulty) => difficulty.DrainRate = (float)BmsLongNoteMode.LongNote;
}
