using System;
using System.Linq;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Icons;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModNoGreat : Mod, IApplicableToJudgementWindow
{
    public override string Name => "No Great";

    public override string Acronym => "NE";

    public override LocalisableString Description => "GREAT and GOOD count as BAD.";

    public override ModType Type => ModType.DifficultyIncrease;

    public override IconUsage? Icon => BmsIcons.NoGreat;

    public override Type[] IncompatibleMods => [typeof(BmsModNoGood)];

    public BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table)
    {
        var rows = table.HitWindows.Select(row => row.Result is HitResult.Great or HitResult.Good
            ? row with { SlowDTime = 0, FastDTime = 0 }
            : row);

        return table.MissWindow is { } miss
            ? new BmsJudgementWindowTable(rows.Append(miss))
            : new BmsJudgementWindowTable(rows);
    }
}
