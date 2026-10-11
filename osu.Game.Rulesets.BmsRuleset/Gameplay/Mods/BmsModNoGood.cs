using System;
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

public class BmsModNoGood : Mod, IApplicableToJudgementWindow
{
    private readonly ConditionalWeakTable<BmsJudgementWindowTable, BmsJudgementWindowTable> windowTables = new();

    public override string Name => "No Good";

    public override string Acronym => "NG";

    public override LocalisableString Description => BmsStrings.ModNoGood;

    public override ModType Type => ModType.DifficultyIncrease;

    public override IconUsage? Icon => BmsIcons.NoGood;

    public override Type[] IncompatibleMods => [typeof(BmsModNoGreat)];

    public BmsJudgementWindowTable ApplyToJudgementWindow(BmsJudgementWindowTable table)
        => windowTables.GetValue(table, static source => createWindow(source));

    private static BmsJudgementWindowTable createWindow(BmsJudgementWindowTable table)
    {
        var rows = table.HitWindows.Select(row => row.Result == HitResult.Good
            ? row with { SlowDTime = 0, FastDTime = 0 }
            : row);

        return table.EPoorWindow is { } ep
            ? new BmsJudgementWindowTable(rows.Append(ep))
            : new BmsJudgementWindowTable(rows);
    }
}
