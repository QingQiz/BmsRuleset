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

        var chart = source.Compile(note => createRule(source, note, options), note => scoringOpportunities(note));
        var maximumLife = 12000 * (2 + (int)options.Difficulty);
        var health = HealthRules.LatchedGauge(0, 1, 1, context => context.Event.Result switch
        {
            var id when id == O2JamResults.COOL => (options.Difficulty == O2JamDifficulty.Hard ? 48.0 : 96) / maximumLife,
            var id when id == O2JamResults.GOOD => 0,
            var id when id == O2JamResults.BAD => -240.0 / maximumLife,
            var id when id == O2JamResults.MISS => -1440.0 / maximumLife,
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
                return ProfileSupport.Symmetric((O2JamResults.COOL, 41), (O2JamResults.GOOD, 125), (O2JamResults.BAD, 173));

            return new TimingWindows(new[] { (O2JamResults.COOL, 0.2), (O2JamResults.GOOD, 0.5), (O2JamResults.BAD, 0.8) }
                .Select(w => new TimingWindow(w.Item1, source.MoveByBeats(time, -w.Item2 * 0.664) - time,
                    source.MoveByBeats(time, w.Item2 * 0.664) - time)));
        }

        var head = windowsAt(note.StartTime);
        if (note.Kind == KeyNoteKind.Tap)
            return new TapRule(new TapConfiguration(head, head.Entries[^1].Late, O2JamResults.MISS));

        if (note.Kind != KeyNoteKind.Hold)
            throw ProfileSupport.Unsupported("O2Jam", note.Kind);

        var tail = windowsAt(note.EndTime);
        return new HoldRule(new HoldConfiguration(head, tail, head.Entries[^1].Late, tail.Entries[^1].Late,
            O2JamResults.MISS, O2JamResults.COOL, O2JamResults.MISS, HoldEndMode.Release, HoldDropMode.Resolve,
            evaluation => evaluation.Resolution == HoldResolution.MissedHead
                ? null
                : new NoteEmission(NoteParts.TAIL, evaluation.TailResult, evaluation.Note.EndTime, evaluation.TailOffset)));
    }

    private static ImmutableArray<ScoringOpportunity> scoringOpportunities(KeyNote note) => note.Kind switch
    {
        KeyNoteKind.Tap => [new ScoringOpportunity(note.Id, NoteParts.TAP, 200)],
        KeyNoteKind.Hold => [new ScoringOpportunity(note.Id, NoteParts.HEAD, 200), new ScoringOpportunity(note.Id, NoteParts.TAIL, 200)],
        _ => [],
    };

    private static ScoreConfiguration createScore()
    {
        var values = ProfileSupport.Values((O2JamResults.COOL, 200, 200, ComboChange.Increment), (O2JamResults.GOOD, 100, 100, ComboChange.Increment),
            (O2JamResults.BAD, 4, 4, ComboChange.Reset), (O2JamResults.MISS, -10, 0, ComboChange.Reset));
        return new ScoreConfiguration(new ScoreState(), (state, context) =>
            {
                if (context.Opportunity == null)
                    return state;

                var result = context.Event.Result;
                var rescued = context.Event.OriginalResult == O2JamResults.BAD && result == O2JamResults.GOOD;
                var pills = state.Counters.GetValueOrDefault(O2JamCounters.PILLS) - (rescued ? 1 : 0);
                var streak = result == O2JamResults.COOL ? state.Counters.GetValueOrDefault(O2JamCounters.COOL_STREAK) + 1 : 0;

                if (streak >= 15 && pills < 5)
                {
                    streak -= 15;
                    pills++;
                }

                var jam = state.Counters.GetValueOrDefault(O2JamCounters.JAM);
                var progress = state.Counters.GetValueOrDefault(O2JamCounters.JAM_PROGRESS);
                var scoreDelta = values[result].Score + (result == O2JamResults.COOL ? 10 * jam : 0);
                progress = result switch
                {
                    var id when id == O2JamResults.COOL => progress + 2,
                    var id when id == O2JamResults.GOOD => progress + 1,
                    _ => 0,
                };
                if (result == O2JamResults.BAD || result == O2JamResults.MISS)
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
                    Counters = state.Counters.SetItem(O2JamCounters.PILLS, pills).SetItem(O2JamCounters.COOL_STREAK, streak)
                        .SetItem(O2JamCounters.JAM, jam).SetItem(O2JamCounters.JAM_PROGRESS, progress),
                };
            }, plan => Enumerable.Range(0, plan.Opportunities.Length).Sum(i => 200 + Math.Floor(i / 25.0) * 10),
            (state, judgement) => judgement.Result == O2JamResults.BAD && state.Counters.GetValueOrDefault(O2JamCounters.PILLS) > 0
                ? judgement with { Result = O2JamResults.GOOD }
                : judgement);
    }
}
