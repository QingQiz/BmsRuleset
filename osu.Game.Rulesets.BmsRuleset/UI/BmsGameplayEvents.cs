using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI;

public sealed class BmsGameplayEvents : IBmsGameplayEvents
{

    public void RaiseText(string text) => Text?.Invoke(text);

    public void RaiseScrollSpeedChanged(double multiplier) => ScrollSpeedChanged?.Invoke(multiplier);

    public void RaiseJudgementDisplayed(HitResult result) => JudgementDisplayed?.Invoke(result);

    public event Action<string>? Text;

    public event Action<double>? ScrollSpeedChanged;

    public event Action<HitResult>? JudgementDisplayed;
}
