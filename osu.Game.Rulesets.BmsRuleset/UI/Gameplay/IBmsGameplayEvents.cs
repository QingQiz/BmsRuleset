using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

public interface IBmsGameplayEvents
{

    void RaiseText(string text);

    void RaiseScrollSpeedChanged(double multiplier);

    void RaiseJudgementDisplayed(HitResult result);

    event Action<string>? Text;

    event Action<double>? ScrollSpeedChanged;

    event Action<HitResult>? JudgementDisplayed;
}
