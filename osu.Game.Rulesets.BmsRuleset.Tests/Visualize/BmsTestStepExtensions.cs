using System;
using System.Diagnostics;
using NUnit.Framework.Constraints;
using osu.Framework.Testing;
using osu.Framework.Testing.Drawables.Steps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Convenience extensions for adding NUnit-style setup steps to a <see cref="TestScene"/>.
/// Setup steps are skipped when re-running the test from a failed step in the visual runner.
/// </summary>
public static class BmsTestStepExtensions
{
    public static void AddSetupStep(this TestScene test, string description, Action action)
        => test.AddStep(new SingleStepButton
        {
            Text = description,
            IsSetupStep = true,
            Action = action,
        });

    public static void AddSetupUntilStep(this TestScene test, string description, Func<bool> assertion)
        => test.AddStep(new UntilStepButton
        {
            Text = description,
            IsSetupStep = true,
            CallStack = new StackTrace(1, true),
            Assertion = assertion,
        });

    public static void AddSetupAssert(this TestScene test, string description, Func<bool> assertion)
        => test.AddStep(new AssertButton
        {
            Text = description,
            IsSetupStep = true,
            CallStack = new StackTrace(1, true),
            Assertion = assertion,
        });

    public static void AddSetupAssert<T>(this TestScene test, string description, Func<T> actualValue, Constraint constraint)
        => test.AddSetupAssert(description, () => ((IResolveConstraint)constraint).Resolve().ApplyTo(actualValue()).IsSuccess);
}
