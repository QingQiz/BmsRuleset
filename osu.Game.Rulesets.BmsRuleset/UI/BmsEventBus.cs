using System;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public static class BmsEventBus
{

    public static void OnTextEvent(string obj)
    {
        TextEvent?.Invoke(obj);
    }

    public static void OnScrollSpeedChangeEvent(double obj)
    {
        ScrollSpeedChangeEvent?.Invoke(obj);
    }

    /// <summary>
    /// BMS text event
    /// </summary>
    public static event Action<string>? TextEvent;

    /// <summary>
    /// BMS scroll speed changed
    /// </summary>
    public static event Action<double>? ScrollSpeedChangeEvent;
}
