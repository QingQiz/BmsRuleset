using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Difficulty;

[TestFixture]
public class BmsDifficultyPerformanceRegressionTest
{
    [TestCase(20000, 10.748954856597857)]
    public void MixedChartKeepsOriginalStarRating(int count, double expected)
    {
        var notes = CreateBeatmap(count).HitObjects
            .Select(n => new BmsNoteTiming(n.Column, n.StartTime, n is BmsLongNote ln ? ln.EndTime : n.StartTime)).ToArray();
        Assert.That(new BmsStarRatingProcessor().ComputeStarRating(notes, 8, 2), Is.EqualTo(expected).Within(1e-12));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationAfterPlayableBeatmapStopsCalculationThroughBaseApi(bool timed)
    {
        using var cancellation = new CancellationTokenSource();
        var beatmap = CreateBeatmap(24);
        var bms = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new PlayableWorkingBeatmap(beatmap, cancellation.Cancel));
        DifficultyCalculator calculator = bms;
        Assert.Throws<OperationCanceledException>(() =>
        {
            if (timed) calculator.CalculateTimed(cancellation.Token);
            else calculator.Calculate(cancellation.Token);
        });
        Assert.That(bms.CalculationCancellationToken, Is.EqualTo(default(CancellationToken)));
        Assert.That(calculator.Calculate().StarRating, Is.GreaterThan(0));
    }

    [Test]
    public void CancellingProcessorDuringPreprocessingReleasesStateAndAllowsReuse()
    {
        using var cancellation = new CancellationTokenSource();
        var processor = new BmsStarRatingProcessor();
        var notes = new CancellingNotes(cancellation);
        Assert.Throws<OperationCanceledException>(() => processor.Compute(notes, 8, 2, cancellationToken: cancellation.Token));
        Assert.That(notes.Reads, Is.LessThan(notes.Count));
        BmsNoteTiming[] next = [new(0, 0, 0), new(1, 500, 500)];
        Assert.That(processor.ComputeStarRating(next, 8, 2), Is.EqualTo(new BmsStarRatingProcessor().ComputeStarRating(next, 8, 2)));
    }

    private sealed class CancellingNotes(CancellationTokenSource cancellation) : IReadOnlyList<BmsNoteTiming>
    {
        public int Count => 2048;

        public int Reads { get; private set; }

        public BmsNoteTiming this[int index]
        {
            get
            {
                Reads++;
                if (index == 16) cancellation.Cancel();
                return new BmsNoteTiming(index % 8, index * 100, index * 100);
            }
        }

        public IEnumerator<BmsNoteTiming> GetEnumerator() => throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Test]
    public void UnusedSkillPreprocessingDoesNotAllocatePerNote()
    {
        var beatmap = CreateBeatmap(10000);
        var calculator = new InspectableCalculator(beatmap);
        calculator.Preprocess(beatmap);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var objects = calculator.Preprocess(beatmap);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(objects.Length, Is.Zero);
            Assert.That(allocated, Is.LessThan(1024));
        });
    }

    [TestCase(8, 1.25)]
    [TestCase(16, 1.5)]
    public void TimedAttributesStillMatchEachExactPrefix(int columns, double rate)
    {
        var beatmap = CreateBeatmap(24, columns);
        var calculator = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new PlayableWorkingBeatmap(beatmap));
        var mod = new osu.Game.Rulesets.BmsRuleset.Mods.BmsModDoubleTime { SpeedChange = { Value = rate } };
        var actual = calculator.CalculateTimed([mod]);
        for (var i = 0; i < actual.Count; i++)
        {
            var notes = beatmap.HitObjects.Take(i + 1)
                .Select(n => new BmsNoteTiming(n.Column, n.StartTime, n is BmsLongNote ln ? ln.EndTime : n.StartTime)).ToArray();
            var expected = new BmsStarRatingProcessor().ComputeStarRating(notes, columns, 2, rate);
            Assert.That(actual[i].Attributes.StarRating, Is.EqualTo(expected).Within(1e-12));
            Assert.That(actual[i].Attributes.MaxCombo, Is.EqualTo(i + 1));
        }
    }

    [Test]
    public void DensityWindowKeepsHalfOpenBoundariesAndDuplicateHeads()
    {
        var type = typeof(BmsStarRatingProcessor);
        var processor = new BmsStarRatingProcessor();
        type.GetProperty(nameof(BmsStarRatingProcessor.TotalColumns))!.SetValue(processor, 8);
        BmsNoteTiming[] notes = [new(0, 0, 0), new(1, 0, 0), new(2, 500, 500), new(3, 1000, 1000), new(4, 1000, 1000)];
        type.GetMethod("preprocessFile", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(processor, [notes, 2, 1.0, BmsLayoutVariant.Bme7K, null]);
        double[] corners = [0, 1, 499, 500, 501, 999, 1000, 1001, 1500, 1501];
        type.GetField("baseCorners", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(processor, corners);
        var workspaceType = type.GetNestedType("ComputeWorkspace", BindingFlags.NonPublic)!;
        using var workspace = (IDisposable)Activator.CreateInstance(workspaceType, corners.Length, corners.Length, corners.Length, 8, 0, 0)!;
        type.GetMethod("computeCAndKsInto", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(processor, [workspace]);
        var density = (double[])workspaceType.GetProperty("CArr")!.GetValue(workspace)!;
        Assert.That(density.Take(corners.Length), Is.EqualTo(corners.Select(t => (double)notes.Count(n => n.StartTime >= t - 500 && n.StartTime < t + 500))));
    }

    internal static BmsBeatmap CreateBeatmap(int count, int columns = 8)
    {
        var beatmap = new BmsBeatmap { TotalColumns = columns, LayoutVariant = BmsLayout.VariantFromTotalColumns(columns), Rank = 2 };
        new BmsDifficultyInfo { KeyCount = columns, Rank = 2 }.WriteToOsuDifficulty(beatmap);
        for (var i = 0; i < count; i++)
        {
            var time = i / 3 * 37.25;
            beatmap.HitObjects.Add(i % 17 == 0
                ? new BmsLongNote { Column = i % columns, StartTime = time, Duration = 210 }
                : new BmsNote { Column = i % columns, StartTime = time });
        }

        foreach (var note in beatmap.HitObjects)
        {
            note.Beatmap = beatmap;
            note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        }

        return beatmap;
    }

    internal sealed class PlayableWorkingBeatmap(BmsBeatmap beatmap, Action onGet = null) : TestWorkingBeatmap(beatmap)
    {
        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
        {
            onGet?.Invoke();
            return beatmap;
        }
    }

    internal sealed class InspectableCalculator(BmsBeatmap beatmap)
        : BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new PlayableWorkingBeatmap(beatmap))
    {
        public DifficultyHitObject[] Preprocess(BmsBeatmap input) => CreateDifficultyHitObjects(input, []).ToArray();
    }
}
