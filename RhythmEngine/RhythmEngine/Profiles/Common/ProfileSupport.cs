using System.Collections.Immutable;
using RhythmEngine.Core;

namespace RhythmEngine.Profiles.Common;

/// <summary>How one scoring opportunity affects the current combo, independently of its score value.</summary>
public enum ComboChange
{
    /// <summary>Adds one to the current combo and updates the maximum if a new peak is reached.</summary>
    Increment,

    /// <summary>Leaves the current combo unchanged without adding to or breaking it.</summary>
    Preserve,

    /// <summary>Resets the current combo to zero while retaining the previous maximum.</summary>
    Reset,
}

public sealed record ResultValue(double Score, double Accuracy, ComboChange Combo);

internal static class ProfileSupport
{
    internal static TimingWindows Symmetric(params (ResultId Result, double Milliseconds)[] windows)
        => new(windows.Select(w => new TimingWindow(w.Result, -Time.FromMilliseconds(w.Milliseconds), Time.FromMilliseconds(w.Milliseconds))));

    internal static ImmutableArray<InputCandidate> Oldest(InputArbitrationContext context)
        => [.. context.Candidates.Where(c => c.Shared), .. context.Candidates.Where(c => !c.Shared).OrderBy(c => c.Priority).ThenBy(c => c.StartTime).ThenBy(c => c.NoteIndex).Take(1)];

    internal static ScoreState Accumulate(ScoreState state, ScoreContext context, ImmutableDictionary<ResultId, ResultValue> values)
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
            JudgedOpportunities = state.JudgedOpportunities + 1,
            ResultCounts = state.ResultCounts.SetItem(context.Event.Result, state.ResultCounts.GetValueOrDefault(context.Event.Result) + 1),
        };
    }

    internal static ImmutableDictionary<ResultId, ResultValue> Values(params (ResultId Id, double Score, double Accuracy, ComboChange Combo)[] values)
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
