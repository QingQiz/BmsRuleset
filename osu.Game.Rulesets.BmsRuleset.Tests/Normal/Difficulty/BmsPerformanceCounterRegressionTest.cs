using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Difficulty;

[TestFixture, NonParallelizable]
public class BmsPerformanceCounterRegressionTest
{
    [Test]
    public void SupportedPerformanceCounterStillRequestsTimedDifficulty()
    {
        using var probe = new CounterProbe(24, supportsPerformance: true);
        probe.Load();
        Assert.That(probe.Requests, Is.EqualTo(1));
    }

    [Test]
    public void UnsupportedPerformanceCounterDoesNotRequestTimedDifficulty()
    {
        using var probe = new CounterProbe(24);
        probe.Load();
        Assert.That(probe.Requests, Is.Zero);
    }

    internal sealed class CounterProbe : IDisposable
    {
        private readonly ScopedMethodProbe patch;
        private readonly ScopedMethodProbe loadPatch;
        private readonly BeatmapDifficultyCache cache = new();
        private readonly ArgonPerformancePointsCounter counter = new();
        private readonly GameplayState state;
        private static Task<List<TimedDifficultyAttributes>> requested;

        public int Requests { get; private set; }

        public CounterProbe(int notes, bool supportsPerformance = false)
        {
            var beatmap = BmsDifficultyPerformanceRegressionTest.CreateBeatmap(notes);
            state = new GameplayState(beatmap, supportsPerformance ? new SupportedBmsRuleset() : new BmsRuleset());
            typeof(PerformancePointsCounter).GetProperty("gameplayState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(counter, state);
            patch = new ScopedMethodProbe(typeof(BeatmapDifficultyCache).GetMethod(nameof(BeatmapDifficultyCache.GetTimedDifficultyAttributesAsync))!,
                postfix: typeof(CounterProbe).GetMethod(nameof(capture), BindingFlags.NonPublic | BindingFlags.Static));
            // Recompile the caller after installing the probe so an earlier JIT-inlined cache
            // request cannot bypass instrumentation when the production HUD patch is installed.
            loadPatch = new ScopedMethodProbe(typeof(PerformancePointsCounter).GetMethod("load", BindingFlags.NonPublic | BindingFlags.Instance)!,
                postfix: typeof(CounterProbe).GetMethod(nameof(afterLoad), BindingFlags.NonPublic | BindingFlags.Static));
        }

        public void Load()
        {
            requested = null;
            typeof(PerformancePointsCounter).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(counter, [cache]);
            if (requested == null)
                return;

            Requests++;
            requested.GetAwaiter().GetResult();
        }

        // ReSharper disable once InconsistentNaming
        private static void capture(Task<List<TimedDifficultyAttributes>> __result) => requested = __result;

        private static void afterLoad()
        {
        }

        public void Dispose()
        {
            loadPatch.Dispose();
            patch.Dispose();
            counter.Dispose();
            cache.Dispose();
            state.ScoreProcessor.Dispose();
            state.HealthProcessor.Dispose();
            requested = null;
        }
    }

    private sealed class SupportedBmsRuleset : BmsRuleset
    {
        public override PerformanceCalculator CreatePerformanceCalculator() => new TestPerformanceCalculator(this);
    }

    private sealed class TestPerformanceCalculator(BmsRuleset ruleset) : PerformanceCalculator(ruleset)
    {
        protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes) => new();
    }
}
