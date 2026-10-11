using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;

public class BmsModNoBad : Mod, IApplicableToJudgementWindow, IApplicableToScoreSelection
{
    // This stateless transform can share its result across notes without retaining transient input tables.
    private readonly ConditionalWeakTable<BmsJudgementWindowTable, BmsJudgementWindowTable> windowTables = new();

    public override string Name => BmsStrings.ModNoBadName.ToString();

    public override string Acronym => BmsStrings.ModNoBadAcronym.ToString();

    public override LocalisableString Description => BmsStrings.ModNoBad;

    public override ModType Type => ModType.DifficultyReduction;

    public override IconUsage? Icon => BmsIcons.NoBad;

    public IScoreSelectionRule ScoreSelectionRule => BmsModTypeScoreSelectionRule<BmsModNoBad>.REDUCTION;

    public BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table)
        => windowTables.GetValue(table, static source => createWindow(source));

    private static BmsJudgementWindowTable createWindow(BmsJudgementWindowTable table)
    {
        // An inverted interval removes BAD hits while retaining its passive POOR deadline.
        var rows = table.HitWindows.Select(row => row.Result == HitResult.Ok ? row with { FastDTime = double.NegativeInfinity } : row);
        return new BmsJudgementWindowTable(table.EPoorWindow is { } ep ? rows.Append(ep) : rows);
    }
}
