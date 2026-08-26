using System;
using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModNoGood : Mod, IApplicableToJudgementWindow
{
    public override string Name => "No Good";

    public override string Acronym => "NG";

    public override LocalisableString Description => BmsStrings.ModNoGood;

    public override ModType Type => ModType.DifficultyIncrease;

    public override IconUsage? Icon => BmsIcons.NoGood;

    public override Type[] IncompatibleMods => [typeof(BmsModNoGreat)];

    public BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table)
    {
        var rows = table.HitWindows.Select(row => row.Result == HitResult.Good
            ? row with { SlowDTime = 0, FastDTime = 0 }
            : row);

        return table.MissWindow is { } miss
            ? new BmsJudgementWindowTable(rows.Append(miss))
            : new BmsJudgementWindowTable(rows);
    }
}
