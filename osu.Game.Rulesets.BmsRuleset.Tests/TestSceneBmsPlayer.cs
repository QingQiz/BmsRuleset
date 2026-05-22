using NUnit.Framework;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsPlayer : PlayerTestScene
{
    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();
}
