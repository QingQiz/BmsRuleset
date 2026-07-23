using osu.Framework.Input.Bindings;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.IO.Input;

/// <summary>
///     Input manager for native BMS actions.
/// </summary>
/// <remarks>
///     This is intentionally small: it only provides BMS action binding infrastructure so the ruleset
///     no longer borrows osu!mania's input manager. Column-to-action gameplay routing will live in the
///     native BMS playfield/drawable layer as that grows.
/// </remarks>
public partial class BmsInputManager(RulesetInfo ruleset, int variant)
    : RulesetInputManager<BmsAction>(ruleset, variant, SimultaneousBindingMode.Unique);
