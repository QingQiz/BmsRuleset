using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Difficulty;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Difficulty;

[TestFixture]
public class BmsStarRatingProcessorTest
{
    [Test]
    public void TestDifficultyCalculatorUsesStarRatingProcessorV3()
    {
        var property = typeof(BmsDifficultyCalculator).GetProperty(nameof(BmsDifficultyCalculator.StarRatingProcessor));

        Assert.That(property, Is.Not.Null);
        Assert.That(property!.PropertyType, Is.EqualTo(typeof(BmsStarRatingProcessorV3)));
    }

    [Test]
    public void TestDifficultyCalculatorWrapperPathUsesEncodedExRank()
    {
        var beatmap = new Beatmap();
        new BmsDifficultyInfo { Rank = 0, ExRank = 200, KeyCount = 8 }.WriteToOsuDifficulty(beatmap);
        beatmap.HitObjects.AddRange(createSimpleHitObjects());

        var calculator = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new TestWorkingBeatmap(beatmap));
        var method = typeof(BmsDifficultyCalculator).GetMethod("CreateDifficultyAttributes", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(method, Is.Not.Null);
        method!.Invoke(calculator, [beatmap, Array.Empty<Mod>(), Array.Empty<Skill>(), 1.0]);

        Assert.That(calculator.StarRatingProcessor.HitLeniencyX, Is.EqualTo(computeExpectedHitLeniency(90)).Within(1e-12));
    }

    [Test]
    public void TestFileImporterUsesStarRatingProcessorV3()
    {
        var method = typeof(BmsFileImporter).GetMethod("computeStarRating", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(methodConstructs(method!, typeof(BmsStarRatingProcessorV3)), Is.True);
            Assert.That(methodConstructs(method!, typeof(BmsStarRatingProcessorV2)), Is.False);
        });
    }

    [Test]
    public void TestStarRatingProcessorV3AllocatesLessThanV2()
    {
        var noteTimings = createDenseNoteTimings();

        new BmsStarRatingProcessorV2().Compute(noteTimings, 8, 2);
        new BmsStarRatingProcessorV3().Compute(noteTimings, 8, 2);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var srV2 = new BmsStarRatingProcessorV2().Compute(noteTimings, 8, 2).StarRating;
        var allocatedV2 = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        var srV3 = new BmsStarRatingProcessorV3().Compute(noteTimings, 8, 2).StarRating;
        var allocatedV3 = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(srV3, Is.EqualTo(srV2).Within(1e-10));
            Assert.That(allocatedV3, Is.LessThan(allocatedV2));
        });
    }

    [Test]
    public void TestStarRatingProcessorV3MatchesV2()
    {
        var noteTimings = createDenseNoteTimings();

        var reference = new BmsStarRatingProcessorV2().Compute(noteTimings, 8, 2).StarRating;
        var actual = new BmsStarRatingProcessorV3().Compute(noteTimings, 8, 2).StarRating;

        Assert.That(actual, Is.EqualTo(reference).Within(1e-10));
    }

    [Test]
    public void TestStarRatingProcessorV3ComputesStarRatingOnly()
    {
        var noteTimings = createDenseNoteTimings();

        var reference = new BmsStarRatingProcessorV2().Compute(noteTimings, 8, 2).StarRating;
        var actual = new BmsStarRatingProcessorV3().ComputeStarRating(noteTimings, 8, 2);

        Assert.That(actual, Is.EqualTo(reference).Within(1e-10));
    }

    [TestCase(BmsLayoutVariant.Bme7K, 2, 45)]
    [TestCase(BmsLayoutVariant.Bms5K, 2, 37.5)]
    [TestCase(BmsLayoutVariant.Pms9K, 2, 35)]
    public void TestStarRatingProcessorV3HitLeniencyUsesBmsGreatWindow(BmsLayoutVariant layout, int rank, double greatWindow)
    {
        var totalColumns = BmsLayout.GetTotalColumns(layout);
        var noteTimings = createSimpleNoteTimings(totalColumns);
        var processor = new BmsStarRatingProcessorV3();

        processor.Compute(noteTimings, totalColumns, rank, 1.0, layout);

        Assert.That(processor.HitLeniencyX, Is.EqualTo(computeExpectedHitLeniency(greatWindow)).Within(1e-12));
    }

    [Test]
    public void TestStarRatingProcessorV3HitLeniencyUsesExplicitJudgementRate()
    {
        var layout = BmsLayoutVariant.Bme7K;
        var totalColumns = BmsLayout.GetTotalColumns(layout);
        var noteTimings = createSimpleNoteTimings(totalColumns);
        var processor = new BmsStarRatingProcessorV3();
        var judgementRate = BmsJudgementProfileProvider.RateForExRank(layout, 200);

        processor.Compute(noteTimings, totalColumns, 2, 1.0, layout, judgementRate);

        Assert.That(processor.HitLeniencyX, Is.EqualTo(computeExpectedHitLeniency(90)).Within(1e-12));
    }

    [Test]
    public void TestStarRatingProcessorV3ReusesScratchAcrossDifferentChartSizes()
    {
        var denseNoteTimings = createDenseNoteTimings();
        var sparseNoteTimings = createSparseNoteTimings();
        var processor = new BmsStarRatingProcessorV3();

        var denseReference = new BmsStarRatingProcessorV2().Compute(denseNoteTimings, 8, 2).StarRating;
        var sparseReference = new BmsStarRatingProcessorV2().Compute(sparseNoteTimings, 8, 2).StarRating;

        var denseActual = processor.Compute(denseNoteTimings, 8, 2).StarRating;
        var sparseActual = processor.Compute(sparseNoteTimings, 8, 2).StarRating;

        Assert.Multiple(() =>
        {
            Assert.That(denseActual, Is.EqualTo(denseReference).Within(1e-10));
            Assert.That(sparseActual, Is.EqualTo(sparseReference).Within(1e-10));
        });
    }

    [Test]
    public void TestStarRatingProcessorV3UsesArrayRangesForPreprocessedNotes()
    {
        var processorType = typeof(BmsStarRatingProcessorV3);
        var noteSeqField = processorType.GetField("noteSeq", BindingFlags.NonPublic | BindingFlags.Instance);
        var noteSeqByColumnField = processorType.GetField("noteSeqByColumn", BindingFlags.NonPublic | BindingFlags.Instance);
        var noteSeqByColumnStartsField = processorType.GetField("noteSeqByColumnStarts", BindingFlags.NonPublic | BindingFlags.Instance);
        var noteSeqByColumnCountsField = processorType.GetField("noteSeqByColumnCounts", BindingFlags.NonPublic | BindingFlags.Instance);
        var lnSeqField = processorType.GetField("lnSeq", BindingFlags.NonPublic | BindingFlags.Instance);
        var tailSeqField = processorType.GetField("tailSeq", BindingFlags.NonPublic | BindingFlags.Instance);
        var totalColumnsProperty = processorType.GetProperty("TotalColumns", BindingFlags.Public | BindingFlags.Instance);
        var preprocessMethod = processorType.GetMethod("preprocessFile", BindingFlags.NonPublic | BindingFlags.Instance, null,
            [typeof(IReadOnlyList<BmsNoteTiming>), typeof(int), typeof(double), typeof(BmsLayoutVariant), typeof(double?)], null);
        var clearMethod = processorType.GetMethod("clearWorkingState", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(noteSeqField, Is.Not.Null);
            Assert.That(noteSeqByColumnField, Is.Not.Null);
            Assert.That(noteSeqByColumnStartsField, Is.Not.Null);
            Assert.That(noteSeqByColumnCountsField, Is.Not.Null);
            Assert.That(lnSeqField, Is.Not.Null);
            Assert.That(tailSeqField, Is.Not.Null);
            Assert.That(totalColumnsProperty, Is.Not.Null);
            Assert.That(preprocessMethod, Is.Not.Null);
            Assert.That(clearMethod, Is.Not.Null);
            Assert.That(noteSeqField!.FieldType.IsArray, Is.True);
            Assert.That(noteSeqByColumnField!.FieldType.IsArray, Is.True);
            Assert.That(noteSeqByColumnStartsField!.FieldType, Is.EqualTo(typeof(int[])));
            Assert.That(noteSeqByColumnCountsField!.FieldType, Is.EqualTo(typeof(int[])));
            Assert.That(lnSeqField!.FieldType.IsArray, Is.True);
            Assert.That(tailSeqField!.FieldType.IsArray, Is.True);
        });

        var noteTimings = new List<BmsNoteTiming>
        {
            new(1, 100, 100),
            new(1, 100, 160),
            new(0, 100, 160),
            new(1, 90, 140),
            new(1, 100, 100),
        };

        var processor = new BmsStarRatingProcessorV3();

        try
        {
            totalColumnsProperty!.SetValue(processor, 2);
            preprocessMethod!.Invoke(processor, [noteTimings, 2, 1.0, BmsLayoutVariant.Bme7K, null]);

            var noteSeq = (Array)noteSeqField!.GetValue(processor)!;
            var noteSeqByColumn = (Array)noteSeqByColumnField!.GetValue(processor)!;
            var noteSeqByColumnStarts = (int[])noteSeqByColumnStartsField!.GetValue(processor)!;
            var noteSeqByColumnCounts = (int[])noteSeqByColumnCountsField!.GetValue(processor)!;
            var tailSeq = (Array)tailSeqField!.GetValue(processor)!;

            Assert.Multiple(() =>
            {
                Assert.That(noteSeq.Length, Is.EqualTo(5));
                Assert.That(noteSeqByColumn.Length, Is.EqualTo(5));
                Assert.That(noteSeqByColumnStarts, Is.EqualTo(new[] { 0, 1 }));
                Assert.That(noteSeqByColumnCounts, Is.EqualTo(new[] { 1, 4 }));
                Assert.That(getNoteEntryHead(noteSeq.GetValue(0)!), Is.EqualTo(90));
                Assert.That(getNoteEntryColumn(noteSeq.GetValue(1)!), Is.EqualTo(0));
                Assert.That(getNoteEntryColumn(noteSeq.GetValue(2)!), Is.EqualTo(1));
                Assert.That(getNoteEntryTail(tailSeq.GetValue(0)!), Is.EqualTo(140));
                Assert.That(getNoteEntryColumn(tailSeq.GetValue(1)!), Is.EqualTo(0));
                Assert.That(getNoteEntryColumn(tailSeq.GetValue(2)!), Is.EqualTo(1));
            });
        }
        finally
        {
            clearMethod!.Invoke(processor, []);
        }
    }

    [Test]
    public void TestStarRatingProcessorV3KeepsLnSequenceOrderForEqualTailTimes()
    {
        var processorType = typeof(BmsStarRatingProcessorV3);
        var lnSeqField = processorType.GetField("lnSeq", BindingFlags.NonPublic | BindingFlags.Instance);
        var tailSeqField = processorType.GetField("tailSeq", BindingFlags.NonPublic | BindingFlags.Instance);
        var totalColumnsProperty = processorType.GetProperty("TotalColumns", BindingFlags.Public | BindingFlags.Instance);
        var preprocessMethod = processorType.GetMethod("preprocessFile", BindingFlags.NonPublic | BindingFlags.Instance, null,
            [typeof(IReadOnlyList<BmsNoteTiming>), typeof(int), typeof(double), typeof(BmsLayoutVariant), typeof(double?)], null);
        var clearMethod = processorType.GetMethod("clearWorkingState", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Multiple(() =>
        {
            Assert.That(lnSeqField, Is.Not.Null);
            Assert.That(tailSeqField, Is.Not.Null);
            Assert.That(totalColumnsProperty, Is.Not.Null);
            Assert.That(preprocessMethod, Is.Not.Null);
            Assert.That(clearMethod, Is.Not.Null);
        });

        var noteTimings = new List<BmsNoteTiming>
        {
            new(1, 100, 200),
            new(0, 90, 200),
            new(1, 140, 140),
        };

        var processor = new BmsStarRatingProcessorV3();

        try
        {
            totalColumnsProperty!.SetValue(processor, 2);
            preprocessMethod!.Invoke(processor, [noteTimings, 2, 1.0, BmsLayoutVariant.Bme7K, null]);

            var lnSeq = (Array)lnSeqField!.GetValue(processor)!;
            var tailSeq = (Array)tailSeqField!.GetValue(processor)!;

            Assert.Multiple(() =>
            {
                Assert.That(lnSeq.Length, Is.EqualTo(2));
                Assert.That(tailSeq.Length, Is.EqualTo(2));
                Assert.That(getNoteEntryColumn(lnSeq.GetValue(0)!), Is.EqualTo(0));
                Assert.That(getNoteEntryColumn(lnSeq.GetValue(1)!), Is.EqualTo(1));
                Assert.That(getNoteEntryTail(tailSeq.GetValue(0)!), Is.EqualTo(200));
                Assert.That(getNoteEntryTail(tailSeq.GetValue(1)!), Is.EqualTo(200));
                Assert.That(getNoteEntryColumn(tailSeq.GetValue(0)!), Is.EqualTo(getNoteEntryColumn(lnSeq.GetValue(0)!)));
                Assert.That(getNoteEntryColumn(tailSeq.GetValue(1)!), Is.EqualTo(getNoteEntryColumn(lnSeq.GetValue(1)!)));
            });
        }
        finally
        {
            clearMethod!.Invoke(processor, []);
        }
    }

    [Test]
    public void TestStarRatingProcessorV3DoesNotKeepDiagnosticArrays()
    {
        var noteTimings = createDenseNoteTimings();

        var result = new BmsStarRatingProcessorV3().Compute(noteTimings, 8, 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.StarRating, Is.GreaterThan(0));
            Assert.That(result.AllCorners, Is.Empty);
            Assert.That(result.BaseCorners, Is.Empty);
            Assert.That(result.ACorners, Is.Empty);
            Assert.That(result.Jbar, Is.Empty);
            Assert.That(result.Xbar, Is.Empty);
            Assert.That(result.Pbar, Is.Empty);
            Assert.That(result.Abar, Is.Empty);
            Assert.That(result.Rbar, Is.Empty);
            Assert.That(result.DensityC, Is.Empty);
            Assert.That(result.ActiveColumnsKs, Is.Empty);
            Assert.That(result.DifficultyD, Is.Empty);
            Assert.That(result.AnchorValues, Is.Empty);
        });
    }

    [Test]
    public void TestStarRatingProcessorV3CornerHelpersPreserveExactSemantics()
    {
        var dedupeMethod = typeof(BmsStarRatingProcessorV3).GetMethod("toDedupedFilteredArray", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(double[]), typeof(int), typeof(double)], null);
        var mergeMethod = typeof(BmsStarRatingProcessorV3).GetMethod("mergeSortedUnique", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(double[]), typeof(double[])], null);

        Assert.Multiple(() =>
        {
            Assert.That(dedupeMethod, Is.Not.Null);
            Assert.That(mergeMethod, Is.Not.Null);
        });

        var filtered = (double[])dedupeMethod!.Invoke(null, [new double[] { -10, 0, 0, 1, 3, 3, 4, 8, 9, 12 }, 10, 10.0])!;
        var merged = (double[])mergeMethod!.Invoke(null, [new double[] { 0, 1, 4, 8, 10 }, new double[] { 0, 2, 4, 9, 10 }])!;

        Assert.Multiple(() =>
        {
            Assert.That(filtered, Is.EqualTo(new[] { 0.0, 1.0, 3.0, 4.0, 8.0, 9.0 }));
            Assert.That(merged, Is.EqualTo(new[] { 0.0, 1.0, 2.0, 4.0, 8.0, 9.0, 10.0 }));
        });
    }

    [Test]
    public void TestStarRatingProcessorV3SortedPercentileHelperPreservesExactThresholdCrossingSemantics()
    {
        var helperMethod = typeof(BmsStarRatingProcessorV3).GetMethod("computePercentileSumsFromSorted", BindingFlags.NonPublic | BindingFlags.Static, null, [typeof(double[]), typeof(double[]), typeof(int), typeof(double)], null);

        Assert.That(helperMethod, Is.Not.Null);

        static (double p93Sum, double p83Sum) invokeHelper(MethodInfo method, double[] sortedDifficulty, double[] sortedWeights)
        {
            double totalWeight = 0;
            for (var i = 0; i < sortedWeights.Length; i++)
                totalWeight += sortedWeights[i];

            var tuple = ((double p93Sum, double p83Sum))method.Invoke(null, [sortedDifficulty, sortedWeights, sortedDifficulty.Length, totalWeight])!;
            return tuple;
        }

        static (double p93Sum, double p83Sum) referenceScan(double[] sortedDifficulty, double[] sortedWeights)
        {
            double totalWeight = 0;
            for (var i = 0; i < sortedWeights.Length; i++)
                totalWeight += sortedWeights[i];

            var percentileIdx = 7;
            var nextTarget = totalWeight * new[] { 0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815 }[percentileIdx];
            double cumulativeWeight = 0;
            double p93Sum = 0;
            double p83Sum = 0;

            for (var i = 0; i < sortedDifficulty.Length; i++)
            {
                cumulativeWeight += sortedWeights[i];
                while (percentileIdx >= 0 && cumulativeWeight >= nextTarget)
                {
                    if (percentileIdx < 4)
                        p93Sum += sortedDifficulty[i];
                    else
                        p83Sum += sortedDifficulty[i];

                    percentileIdx--;
                    if (percentileIdx >= 0)
                        nextTarget = totalWeight * new[] { 0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815 }[percentileIdx];
                }
            }

            if (percentileIdx >= 0)
            {
                var last = sortedDifficulty[^1];
                while (percentileIdx >= 0)
                {
                    if (percentileIdx < 4)
                        p93Sum += last;
                    else
                        p83Sum += last;

                    percentileIdx--;
                }
            }

            return (p93Sum, p83Sum);
        }

        var weightedCrossingsDifficulty = new[] { 1.0, 2.0, 3.0, 3.0, 4.0 };
        var weightedCrossingsWeights = new[] { 1.0, 5.0, 1.5, 1.5, 1.0 };
        var zeroWeightDifficulty = new[] { 2.0, 7.0, 9.0 };
        var zeroWeightWeights = new[] { 0.0, 0.0, 0.0 };

        Assert.Multiple(() =>
        {
            Assert.That(invokeHelper(helperMethod!, weightedCrossingsDifficulty, weightedCrossingsWeights), Is.EqualTo(referenceScan(weightedCrossingsDifficulty, weightedCrossingsWeights)));
            Assert.That(invokeHelper(helperMethod!, zeroWeightDifficulty, zeroWeightWeights), Is.EqualTo(referenceScan(zeroWeightDifficulty, zeroWeightWeights)));
        });
    }

    [Test]
    public void TestStarRatingProcessorV3AnchorComputationUsesCornerMajorKeyUsageWithoutAllocations()
    {
        var processorType = typeof(BmsStarRatingProcessorV3);
        var totalColumnsProperty = processorType.GetProperty("TotalColumns", BindingFlags.Public | BindingFlags.Instance);
        var baseCornersField = processorType.GetField("baseCorners", BindingFlags.NonPublic | BindingFlags.Instance);
        var computeAnchorMethod = processorType.GetMethod("computeAnchorInto", BindingFlags.NonPublic | BindingFlags.Instance, null, [typeof(double[]), typeof(double[])], null);

        Assert.Multiple(() =>
        {
            Assert.That(totalColumnsProperty, Is.Not.Null);
            Assert.That(baseCornersField, Is.Not.Null);
            Assert.That(computeAnchorMethod, Is.Not.Null);
        });

        var processor = new BmsStarRatingProcessorV3();
        totalColumnsProperty!.SetValue(processor, 4);
        baseCornersField!.SetValue(processor, new[] { 0.0, 100.0, 200.0 });

        var keyUsage400 =
            new[]
            {
                10.0, 5.0, 0.0, 0.0,
                8.0, 4.0, 2.0, 0.0,
                9.0, 3.0, 1.0, 0.0,
            };
        var actual = new double[3];
        var expected =
            new[]
            {
                computeExpectedAnchorValue(10.0, 5.0, 0.0, 0.0),
                computeExpectedAnchorValue(8.0, 4.0, 2.0, 0.0),
                computeExpectedAnchorValue(9.0, 3.0, 1.0, 0.0),
            };

        var computeAnchor = (Action<BmsStarRatingProcessorV3, double[], double[]>)computeAnchorMethod!.CreateDelegate(typeof(Action<BmsStarRatingProcessorV3, double[], double[]>));

        computeAnchor(processor, keyUsage400, actual);

        Assert.That(actual, Is.EqualTo(expected).Within(1e-10));

        computeAnchor(processor, keyUsage400, actual);

        var before = GC.GetAllocatedBytesForCurrentThread();
        computeAnchor(processor, keyUsage400, actual);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.EqualTo(0));
    }

    [TestCase("_sphyper.json")]
    [TestCase("STR_debut_LN_______________________.json")]
    public void TestStarRatingProcessorV3MatchesV2ForBenchmarkSample(string fileName)
    {
        var input = loadBenchmarkInput(fileName);
        var noteTimings = toNoteTimings(input);

        var reference = new BmsStarRatingProcessorV2().Compute(noteTimings, input.TotalColumns, input.Rank).StarRating;
        var actual = new BmsStarRatingProcessorV3().Compute(noteTimings, input.TotalColumns, input.Rank).StarRating;

        Assert.That(actual, Is.EqualTo(reference).Within(1e-10));
    }

    private static List<BmsNoteTiming> createDenseNoteTimings()
    {
        var noteTimings = new List<BmsNoteTiming>(2500);

        for (var i = 0; i < 2500; i++)
        {
            var startTime = i * 37;
            var column = i % 8;

            if (i % 11 == 0)
                noteTimings.Add(new BmsNoteTiming(column, startTime, startTime + 420));
            else
                noteTimings.Add(new BmsNoteTiming(column, startTime, startTime));
        }

        return noteTimings;
    }

    private static List<BmsNoteTiming> createSparseNoteTimings()
    {
        return
        [
            new BmsNoteTiming(0, 0, 0),
            new BmsNoteTiming(3, 180, 180),
            new BmsNoteTiming(5, 360, 720),
            new BmsNoteTiming(1, 1080, 1080),
            new BmsNoteTiming(6, 1320, 1320),
            new BmsNoteTiming(2, 1680, 2100),
            new BmsNoteTiming(7, 2460, 2460),
        ];
    }

    private static List<BmsNoteTiming> createSimpleNoteTimings(int totalColumns) =>
    [
        new BmsNoteTiming(0, 0, 0),
        new BmsNoteTiming(Math.Min(1, totalColumns - 1), 180, 180),
        new BmsNoteTiming(Math.Min(2, totalColumns - 1), 360, 720),
    ];

    private static IEnumerable<BmsHitObject> createSimpleHitObjects() =>
    [
        new BmsNote { Column = 0, StartTime = 0 },
        new BmsNote { Column = 1, StartTime = 180 },
        new BmsLongNote { Column = 2, StartTime = 360, Duration = 360 },
    ];

    private static SrBenchmarkInput loadBenchmarkInput(string fileName)
    {
        var path = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "benchmark_data", fileName));

        var input = JsonSerializer.Deserialize<SrBenchmarkInput>(File.ReadAllText(path));
        Assert.That(input, Is.Not.Null);
        return input!;
    }

    private static double computeExpectedAnchorValue(params double[] counts)
    {
        Array.Sort(counts);
        Array.Reverse(counts);

        var nonZeroCount = 0;
        while (nonZeroCount < counts.Length && counts[nonZeroCount] != 0)
            nonZeroCount++;

        var result = 0.0;

        if (nonZeroCount > 1)
        {
            double walk = 0;
            double maxWalk = 0;
            for (var i = 0; i < nonZeroCount - 1; i++)
            {
                var ratio = counts[i + 1] / counts[i];
                walk += counts[i] * (1 - 4 * (0.5 - ratio) * (0.5 - ratio));
                maxWalk += counts[i];
            }

            result = walk / maxWalk;
        }

        var r = result - 0.22;
        return 1 + Math.Min(result - 0.18, 5 * r * r * r);
    }

    private static double computeExpectedHitLeniency(double greatWindowMs)
    {
        var x = 0.3 * Math.Sqrt(greatWindowMs / 500.0);
        return Math.Min(x, 0.6 * (x - 0.09) + 0.09);
    }

    private static List<BmsNoteTiming> toNoteTimings(SrBenchmarkInput input)
    {
        var noteTimings = new List<BmsNoteTiming>(input.HitObjects.Count);
        foreach (var hitObject in input.HitObjects)
        {
            noteTimings.Add(new BmsNoteTiming(
                hitObject.Column,
                hitObject.StartTime,
                hitObject.IsLongNote ? hitObject.StartTime + hitObject.Duration : hitObject.StartTime));
        }

        return noteTimings;
    }

    private static int getNoteEntryColumn(object noteEntry) => (int)noteEntry.GetType().GetProperty("Column")!.GetValue(noteEntry)!;

    private static double getNoteEntryHead(object noteEntry) => (double)noteEntry.GetType().GetProperty("Head")!.GetValue(noteEntry)!;

    private static double getNoteEntryTail(object noteEntry) => (double)noteEntry.GetType().GetProperty("Tail")!.GetValue(noteEntry)!;

    private static bool methodConstructs(MethodInfo method, Type constructedType)
    {
        var body = method.GetMethodBody()?.GetILAsByteArray();
        var constructor = constructedType.GetConstructor(Type.EmptyTypes);

        Assert.That(body, Is.Not.Null);
        Assert.That(constructor, Is.Not.Null);

        var token = constructor!.MetadataToken;
        for (var i = 0; i <= body!.Length - 5; i++)
        {
            if (body[i] == 0x73 && BitConverter.ToInt32(body, i + 1) == token)
                return true;
        }

        return false;
    }

    private sealed record SrBenchmarkInput(
        [property: JsonPropertyName("tc")] int TotalColumns,
        [property: JsonPropertyName("rk")] int Rank,
        [property: JsonPropertyName("ho")] List<HitObjectData> HitObjects);

    private sealed record HitObjectData(
        [property: JsonPropertyName("c")] int Column,
        [property: JsonPropertyName("st")] double StartTime,
        [property: JsonPropertyName("d")] double Duration,
        [property: JsonPropertyName("ln")] bool IsLongNote);
}
