using System;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public class BmsGameplayTextEventController(BmsTextEvents events)
{
    private int index;
    private double lastTime = double.MinValue;

    public void Update(double currentTime, Action<string> showText)
    {
        if (currentTime < lastTime)
            seek(currentTime);

        while (index < events.TextEvents.Length && currentTime >= events.TextEvents[index].Time)
        {
            showText(events.TextEvents[index].Text);
            index++;
        }

        lastTime = currentTime;
    }

    private void seek(double time)
    {
        index = 0;

        while (index < events.TextEvents.Length && events.TextEvents[index].Time < time)
            index++;

        lastTime = time;
    }

    public void TriggerMistake(Action<string> showText)
    {
        if (!string.IsNullOrEmpty(events.MistakeText))
            showText(events.MistakeText);
    }
}
