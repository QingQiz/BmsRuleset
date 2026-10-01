using System.Collections.Generic;
using System.Linq;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.StateChanges.Events;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using static osu.Game.Input.Handlers.ReplayInputHandler;

namespace osu.Game.Rulesets.BmsRuleset.IO.Input;

/// <summary>
///     Input manager for native BMS actions.
/// </summary>
public partial class BmsInputManager(RulesetInfo ruleset, int variant)
    : RulesetInputManager<BmsAction>(ruleset, variant, SimultaneousBindingMode.Unique)
{
    private IBmsActionProducer[] actionProducers = [];
    private IBmsActionFilter[] actionFilters = [];

    internal void ConfigureInputMods(IReadOnlyList<Mod> mods)
    {
        actionProducers = mods.OfType<IBmsActionProducer>().ToArray();
        actionFilters = mods.OfType<IBmsActionFilter>().ToArray();
    }

    protected override void Update()
    {
        base.Update();
        foreach (var producer in actionProducers)
            producer.UpdateActions(Time.Current);
    }

    public override void HandleInputStateChange(InputStateChangeEvent inputStateChange)
    {
        if (actionFilters.Length > 0 && inputStateChange is ReplayStateChangeEvent<BmsAction> replay)
        {
            foreach (var action in replay.ReleasedActions)
                if (allowReplayAction(action)) KeyBindingContainer.TriggerReleased(action);
            foreach (var action in replay.PressedActions)
                if (allowReplayAction(action)) KeyBindingContainer.TriggerPressed(action);
            return;
        }
        base.HandleInputStateChange(inputStateChange);
    }

    private bool allowReplayAction(BmsAction action)
    {
        foreach (var filter in actionFilters)
            if (!filter.AllowReplayAction(action)) return false;
        return true;
    }

    private bool allowPhysicalAction(BmsAction action)
    {
        foreach (var filter in actionFilters)
            if (!filter.AllowPhysicalAction(action)) return false;
        return true;
    }

    protected override KeyBindingContainer<BmsAction> CreateKeyBindingContainer(RulesetInfo info, int layout, SimultaneousBindingMode mode)
        => new BmsKeyBindingContainer(info, layout, mode, this);

    private sealed partial class BmsKeyBindingContainer(RulesetInfo info, int layout, SimultaneousBindingMode mode, BmsInputManager owner)
        : RulesetKeyBindingContainer(info, layout, mode)
    {
        protected override void ReloadMappings(IQueryable<RealmKeyBinding> mappings)
        {
            base.ReloadMappings(mappings);
            // Suppress physical sources before they change the framework's shared pressed-action state.
            KeyBindings = KeyBindings.Where(binding => owner.allowPhysicalAction(binding.GetAction<BmsAction>())).ToArray();
        }
    }
}
