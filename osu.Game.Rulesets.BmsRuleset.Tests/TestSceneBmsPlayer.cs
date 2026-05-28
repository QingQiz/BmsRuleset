using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsPlayer : PlayerTestScene
{
    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    public override void SetUpSteps()
    {
        base.SetUpSteps();

        AddStep("change scroll speed to slow", () => changeScrollSpeedTo(4));
        AddStep("change scroll speed to default", () => changeScrollSpeedTo(8));
    }

    private void changeScrollSpeedTo(double speed)
    {
        var rulesetConfig = (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!;
        rulesetConfig.SetValue(BmsRulesetSetting.ScrollSpeed, speed);
    }
}
