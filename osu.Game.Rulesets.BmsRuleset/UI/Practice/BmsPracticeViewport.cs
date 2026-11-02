using System;

namespace osu.Game.Rulesets.BmsRuleset.UI.Practice;

/// <summary>Kept separate from practice settings so browsing cannot move exercise boundaries.</summary>
public sealed class BmsPracticeViewport
{
    public double Minimum { get; private set; }
    public double Maximum { get; private set; } = 1;
    public double Start { get; private set; }
    public double Duration { get; private set; } = 1;
    public double End => Start + Duration;
    public double MinimumDuration => Math.Min(250, Maximum - Minimum);

    public void SetBounds(double minimum, double maximum, bool reset = false)
    {
        Minimum = minimum;
        Maximum = Math.Max(minimum + 1, maximum);
        if (reset)
            FitAll();
        else
            Set(Start, Duration);
    }

    public void Set(double start, double duration)
    {
        Duration = Math.Clamp(duration, MinimumDuration, Maximum - Minimum);
        Start = Math.Clamp(start, Minimum, Maximum - Duration);
    }

    public void FitAll() => Set(Minimum, Maximum - Minimum);

    public void Fit(double start, double end)
    {
        var margin = Math.Max(250, (end - start) * 0.08);
        Set(start - margin, end - start + margin * 2);
    }

    public void Pan(double delta) => Set(Start + delta, Duration);

    public void Zoom(double factor, double anchorFraction = 0.5)
    {
        anchorFraction = Math.Clamp(anchorFraction, 0, 1);
        var anchor = Start + Duration * anchorFraction;
        var duration = Math.Clamp(Duration * factor, MinimumDuration, Maximum - Minimum);
        Set(anchor - duration * anchorFraction, duration);
    }
}
