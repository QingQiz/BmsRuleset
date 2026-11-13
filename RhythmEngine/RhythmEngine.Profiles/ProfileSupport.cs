using System.Collections.Immutable;

namespace RhythmEngine.Profiles;

public enum ComboChange
{
    Increment,
    Preserve,
    Reset,
}

public sealed record ResultValue(double Score, double Accuracy, ComboChange Combo);

internal static class ProfileSupport
{
    internal static TimingWindows Symmetric(params (string Result, double Milliseconds)[] windows)
        => new(windows.Select(w => new TimingWindow(w.Result, -Time.FromMilliseconds(w.Milliseconds), Time.FromMilliseconds(w.Milliseconds))));

    internal static ImmutableArray<InputCandidate> Oldest(InputArbitrationContext context)
        => [.. context.Candidates.Where(c => c.Shared), .. context.Candidates.Where(c => !c.Shared).OrderBy(c => c.Priority).ThenBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1)];

    internal static ScoreState Accumulate(ScoreState state, ScoreContext context, ImmutableDictionary<string, ResultValue> values)
    {
        if (context.Opportunity is not { } opportunity)
            return state;

        var value = values[context.Event.Result];
        var combo = value.Combo switch
        {
            ComboChange.Increment => state.Combo + 1,
            ComboChange.Reset => 0,
            _ => state.Combo,
        };
        return state with
        {
            Score = state.Score + value.Score * opportunity.Weight,
            AccuracyPoints = state.AccuracyPoints + value.Accuracy * opportunity.Weight,
            AccuracyMaximum = state.AccuracyMaximum + opportunity.MaximumAccuracy * opportunity.Weight,
            Combo = combo,
            MaximumCombo = Math.Max(state.MaximumCombo, combo),
        };
    }

    internal static ImmutableDictionary<string, ResultValue> Values(params (string Id, double Score, double Accuracy, ComboChange Combo)[] values)
        => values.ToImmutableDictionary(v => v.Id, v => new ResultValue(v.Score, v.Accuracy, v.Combo));

    internal static double DifficultyRange(double value, double low, double middle, double high)
        => value > 5 ? middle + (high - middle) * (value - 5) / 5 : middle + (middle - low) * (value - 5) / 5;

    internal static void RequireRange(double value, double minimum, double maximum, string name)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }

    internal static NotSupportedException Unsupported(string profile, KeyNoteKind kind) => new($"{profile} does not define {kind} objects.");
}
