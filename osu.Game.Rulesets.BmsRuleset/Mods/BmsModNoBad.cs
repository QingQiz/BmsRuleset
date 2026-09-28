using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModNoBad : Mod, IApplicableToJudgementWindow, IApplicableToScoreSelection
{
    public override string Name => BmsStrings.ModNoBadName.ToString();

    public override string Acronym => BmsStrings.ModNoBadAcronym.ToString();

    public override LocalisableString Description => BmsStrings.ModNoBad;

    public override ModType Type => ModType.DifficultyReduction;

    public override IconUsage? Icon => BmsIcons.NoBad;

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModNoBad>.REDUCTION;

    public BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table)
    {
        // An inverted interval removes BAD hits while retaining its passive POOR deadline.
        var rows = table.HitWindows.Select(row => row.Result == HitResult.Ok ? row with { FastDTime = double.NegativeInfinity } : row);
        return new BmsJudgementWindowTable(table.MissWindow is { } miss ? rows.Append(miss) : rows);
    }
}
