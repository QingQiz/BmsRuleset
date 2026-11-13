using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Patterns;

public static class HealthRules
{
    /// <summary>A bounded gauge whose first failure is permanent and whose clear is evaluated at completion.</summary>
    public static HealthConfiguration LatchedGauge(
        double minimum, double maximum, double initial,
        Func<HealthContext, double> delta, Func<double, bool> fails, Func<double, bool> clears)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || !double.IsFinite(initial)
            || minimum > maximum || initial < minimum || initial > maximum)
            throw new ArgumentException("Finite, ordered gauge bounds containing the initial value are required.");

        return new HealthConfiguration(new HealthState(initial, fails(initial)), context =>
        {
            var value = Math.Clamp(context.Health.Value + delta(context), minimum, maximum);
            return new HealthState(value, context.Health.HasFailed || fails(value));
        }, context => context.Health.HasFailed ? SessionOutcome.Failed
            : !context.Completed ? SessionOutcome.InProgress
            : clears(context.Health.Value) ? SessionOutcome.Cleared : SessionOutcome.Failed);
    }
}
