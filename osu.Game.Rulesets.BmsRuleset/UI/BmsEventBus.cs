using System;
using osu.Game.Rulesets.Scoring;

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

    public static void OnJudgementDisplayEvent(HitResult obj)
    {
        JudgementDisplayEvent?.Invoke(obj);
    }

    /// <summary>
    /// BMS text event
    /// </summary>
    public static event Action<string>? TextEvent;

    /// <summary>
    /// BMS scroll speed changed
    /// </summary>
    public static event Action<double>? ScrollSpeedChangeEvent;

    /// <summary>
    /// BMS judgement display requested
    /// </summary>
    public static event Action<HitResult>? JudgementDisplayEvent;
}
