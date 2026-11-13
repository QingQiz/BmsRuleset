using System.Collections.Immutable;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Patterns;

namespace RhythmEngine.Profiles.O2Jam;

/// <summary>Selects health parameters, not timing windows; Open2Jam compatibility and original 3.38 profiles interpret each difficulty separately.</summary>
/// <remarks>The 3.38 deltas below use a 0..1000 health scale. Open2Jam normalises its deltas by a difficulty-specific base life value.</remarks>
public enum O2JamDifficulty
{
    /// <summary>Open2Jam base life is 24000; 3.38 COOL/GOOD/BAD/MISS deltas are +3/+2/-10/-50.</summary>
    Easy,

    /// <summary>The default difficulty. Open2Jam base life is 36000; 3.38 COOL/GOOD/BAD/MISS deltas are +2/+1/-7/-40.</summary>
    Normal,

    /// <summary>Open2Jam base life is 48000 with half the COOL recovery coefficient; 3.38 COOL/GOOD/BAD/MISS deltas are +1/0/-5/-30.</summary>
    Hard,
}

public sealed record O2JamOptions(O2JamDifficulty Difficulty = O2JamDifficulty.Normal, bool BeatBasedWindows = true);

/// <summary>Versioned Open2Jam compatibility configuration, including its JAM and pill rules.</summary>
public static class O2JamProfile
{
    public static CompiledProfile Create(KeyChart source, O2JamOptions? options = null)
    {
        options ??= new O2JamOptions();
        if (!Enum.IsDefined(options.Difficulty))
            throw new ArgumentOutOfRangeException(nameof(options));

        var chart = source.Compile(note => createRule(source, note, options));
        var maximumLife = 12000 * (2 + (int)options.Difficulty);
        var health = HealthRules.LatchedGauge(0, 1, 1, context => context.Event.Result switch
        {
            "cool" => (options.Difficulty == O2JamDifficulty.Hard ? 48.0 : 96) / maximumLife,
            "good" => 0,
            "bad" => -240.0 / maximumLife,
            "miss" => -1440.0 / maximumLife,
            _ => throw new InvalidOperationException($"Undefined O2Jam health result {context.Event.Result}."),
        }, _ => false, _ => true);
        var descriptor = new ProfileDescriptor("o2jam-open2jam-v1", "11384b3ca957828ae66a72c9e28edd42c97952d5", EvidenceLevel.CommunityCompatibility,
            ["https://github.com/open2jamorg/open2jam/tree/11384b3ca957828ae66a72c9e28edd42c97952d5/src/org/open2jam"],
            ["Open2Jam rules, not original-client binary parity. Accuracy reports normalized 200/100/4/0 credit separately from JAM score."]);
        return new CompiledProfile(chart, new SessionConfiguration(descriptor.Id, createScore(), health, ProfileSupport.Oldest), descriptor);
    }

    private static INoteRule createRule(KeyChart source, KeyNote note, O2JamOptions options)
    {
        TimingWindows windowsAt(long time)
        {
            if (!options.BeatBasedWindows)
                return ProfileSupport.Symmetric(("cool", 41), ("good", 125), ("bad", 173));

            return new TimingWindows(new[] { ("cool", 0.2), ("good", 0.5), ("bad", 0.8) }
                .Select(w => new TimingWindow(w.Item1, source.MoveByBeats(time, -w.Item2 * 0.664) - time,
                    source.MoveByBeats(time, w.Item2 * 0.664) - time)));
        }

        var head = windowsAt(note.StartTime);
        if (note.Kind == KeyNoteKind.Tap)
            return new TapRule(new TapConfiguration(head, head.Entries[^1].Late, "miss", 200));

        if (note.Kind != KeyNoteKind.Hold)
            throw ProfileSupport.Unsupported("O2Jam", note.Kind);

        var tail = windowsAt(note.EndTime);
        return new HoldRule(new HoldConfiguration(head, tail, head.Entries[^1].Late, tail.Entries[^1].Late,
            "miss", "cool", "miss", 200, 200, HoldEndMode.Release, HoldDropMode.Resolve,
            evaluation => evaluation.Resolution == HoldResolution.MissedHead
                ? null
                : new NoteEmission("tail", evaluation.TailResult, evaluation.Note.EndTime, evaluation.TailOffset)));
    }

    private static ScoreConfiguration createScore()
    {
        var values = ProfileSupport.Values(("cool", 200, 200, ComboChange.Increment), ("good", 100, 100, ComboChange.Increment),
            ("bad", 4, 4, ComboChange.Reset), ("miss", -10, 0, ComboChange.Reset));
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
            {
                if (context.Opportunity == null)
                    return state;

                var result = context.Event.Result;
                var rescued = context.Event.OriginalResult == "bad" && result == "good";
                var pills = state.Counters.GetValueOrDefault("pills") - (rescued ? 1 : 0);
                var streak = result == "cool" ? state.Counters.GetValueOrDefault("cool-streak") + 1 : 0;

                if (streak >= 15 && pills < 5)
                {
                    streak -= 15;
                    pills++;
                }

                var jam = state.Counters.GetValueOrDefault("jam");
                var progress = state.Counters.GetValueOrDefault("jam-progress");
                var scoreDelta = values[result].Score + (result == "cool" ? 10 * jam : 0);
                progress = result switch { "cool" => progress + 2, "good" => progress + 1, _ => 0 };
                if (result is "bad" or "miss")
                    jam = 0;

                if (progress >= 50)
                {
                    progress = 0;
                    jam++;
                }

                var next = ProfileSupport.Accumulate(state, context, values);
                return next with
                {
                    Score = Math.Max(0, state.Score + scoreDelta),
                    Counters = state.Counters.SetItem("pills", pills).SetItem("cool-streak", streak)
                        .SetItem("jam", jam).SetItem("jam-progress", progress),
                };
            }, plan => Enumerable.Range(0, plan.Opportunities.Length).Sum(i => 200 + Math.Floor(i / 25.0) * 10),
            (state, judgement) => judgement.Result == "bad" && state.Counters.GetValueOrDefault("pills") > 0
                ? judgement with { Result = "good" }
                : judgement);
    }
}
