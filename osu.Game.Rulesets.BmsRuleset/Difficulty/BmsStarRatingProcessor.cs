using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using osu.Game.Rulesets.BmsRuleset.BmsParser;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class BmsStarRatingProcessor
{
    private static readonly double[] target_percentiles = [0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815];
    private static readonly EventTimeComparer event_time_comparer = new();

    private readonly record struct NoteEntry(int Column, double Head, double Tail);

    public BmsStarRatingResult Result { get; private set; } = new();

    public double HitLeniencyX { get; private set; }

    public int TotalColumns { get; private set; }

    public double TotalTimeT { get; private set; }

    private NoteEntry[] noteSeq = [];
    private NoteEntry[] noteSeqByColumn = [];
    private int[] noteSeqByColumnStarts = [];
    private int[] noteSeqByColumnCounts = [];
    private NoteEntry[] lnSeq = [];
    private NoteEntry[] tailSeq = [];

    private double[] baseCorners = [];
    private double[] aCorners = [];
    private double[] allCorners = [];
    private double[] anchorCountsScratch = [];

    // One owner per compute call keeps pooled scratch tied to the stages that share it, so new buffers do not have to replicate return logic in every helper.
    private sealed class ComputeWorkspace : IDisposable
    {
        public ulong[] ActiveColumnMask { get; }

        public double[] KeyUsage400 { get; }

        public double[] Anchor { get; }

        public double[] CumSumBuffer { get; }

        public double[] CumSumBufferA { get; }

        public int[] SmoothWl { get; }

        public int[] SmoothWr { get; }

        public int[] SmoothWlA { get; }

        public int[] SmoothWrA { get; }

        public double[] Jbar { get; }

        public double[] Xbar { get; }

        public double[] Pbar { get; }

        public double[] Abar { get; }

        public double[] Rbar { get; }

        public double[] CArr { get; }

        public double[] KsArr { get; }

        public double[] DeltaKs { get; }

        public int[] BaseInterpIdx { get; }

        public int[] AInterpIdx { get; }

        public double[] D { get; }

        public double[] Jks { get; }

        public double[] SmoothedJks { get; }

        public double[] JbarNum { get; }

        public double[] JbarDen { get; }

        public double[] Xks { get; }

        public double[] FastCrossCurrent { get; }

        public double[] FastCrossPrevious { get; }

        public double[] XBase { get; }

        public double[] SqrtSums { get; }

        public double[] PStep { get; }

        public double[] Dks { get; }

        public double[] AStep { get; }

        public double[] RStep { get; }

        public double[] IRelease { get; }

        public double[] NoteHitTimes { get; }

        public double[] EffectiveWeights { get; }

        public (double time, double change)[] LnEvents { get; }

        public double[] LnPointCandidates { get; }

        public double[] LnCumsum { get; }

        public double[] LnValues { get; }

        public ComputeWorkspace(int baseCount, int aCount, int allCount, int totalColumns, int noteCount, int tailCount, int lnCount)
        {
            ActiveColumnMask = ArrayPool<ulong>.Shared.Rent(baseCount);
            KeyUsage400 = ArrayPool<double>.Shared.Rent(totalColumns * baseCount);
            Anchor = ArrayPool<double>.Shared.Rent(baseCount);
            CumSumBuffer = ArrayPool<double>.Shared.Rent(baseCount);
            CumSumBufferA = ArrayPool<double>.Shared.Rent(aCount);
            SmoothWl = ArrayPool<int>.Shared.Rent(baseCount);
            SmoothWr = ArrayPool<int>.Shared.Rent(baseCount);
            SmoothWlA = ArrayPool<int>.Shared.Rent(aCount);
            SmoothWrA = ArrayPool<int>.Shared.Rent(aCount);
            Jbar = ArrayPool<double>.Shared.Rent(baseCount);
            Xbar = ArrayPool<double>.Shared.Rent(baseCount);
            Pbar = ArrayPool<double>.Shared.Rent(baseCount);
            Abar = ArrayPool<double>.Shared.Rent(aCount);
            Rbar = ArrayPool<double>.Shared.Rent(baseCount);
            CArr = ArrayPool<double>.Shared.Rent(baseCount);
            KsArr = ArrayPool<double>.Shared.Rent(baseCount);
            DeltaKs = ArrayPool<double>.Shared.Rent(totalColumns * baseCount);
            BaseInterpIdx = ArrayPool<int>.Shared.Rent(allCount);
            AInterpIdx = ArrayPool<int>.Shared.Rent(allCount);
            D = ArrayPool<double>.Shared.Rent(allCount);
            Jks = ArrayPool<double>.Shared.Rent(baseCount);
            SmoothedJks = ArrayPool<double>.Shared.Rent(baseCount);
            JbarNum = ArrayPool<double>.Shared.Rent(baseCount);
            JbarDen = ArrayPool<double>.Shared.Rent(baseCount);
            Xks = ArrayPool<double>.Shared.Rent(baseCount);
            FastCrossCurrent = ArrayPool<double>.Shared.Rent(baseCount);
            FastCrossPrevious = ArrayPool<double>.Shared.Rent(baseCount);
            XBase = ArrayPool<double>.Shared.Rent(baseCount);
            SqrtSums = ArrayPool<double>.Shared.Rent(baseCount);
            PStep = ArrayPool<double>.Shared.Rent(baseCount);
            Dks = ArrayPool<double>.Shared.Rent(totalColumns * baseCount);
            AStep = ArrayPool<double>.Shared.Rent(aCount);
            RStep = ArrayPool<double>.Shared.Rent(baseCount);
            IRelease = ArrayPool<double>.Shared.Rent(tailCount);
            NoteHitTimes = ArrayPool<double>.Shared.Rent(noteCount);
            EffectiveWeights = ArrayPool<double>.Shared.Rent(allCount);

            var lnEventCount = lnCount * 3;
            LnEvents = ArrayPool<(double time, double change)>.Shared.Rent(lnEventCount);
            LnPointCandidates = ArrayPool<double>.Shared.Rent(lnEventCount + 2);
            LnCumsum = ArrayPool<double>.Shared.Rent(lnEventCount + 2);
            LnValues = ArrayPool<double>.Shared.Rent(lnEventCount + 1);
        }

        public void Dispose()
        {
            ArrayPool<double>.Shared.Return(LnValues);
            ArrayPool<double>.Shared.Return(LnCumsum);
            ArrayPool<double>.Shared.Return(LnPointCandidates);
            ArrayPool<(double time, double change)>.Shared.Return(LnEvents);
            ArrayPool<double>.Shared.Return(EffectiveWeights);
            ArrayPool<double>.Shared.Return(NoteHitTimes);
            ArrayPool<double>.Shared.Return(IRelease);
            ArrayPool<double>.Shared.Return(RStep);
            ArrayPool<double>.Shared.Return(AStep);
            ArrayPool<double>.Shared.Return(Dks);
            ArrayPool<double>.Shared.Return(PStep);
            ArrayPool<double>.Shared.Return(SqrtSums);
            ArrayPool<double>.Shared.Return(XBase);
            ArrayPool<double>.Shared.Return(FastCrossPrevious);
            ArrayPool<double>.Shared.Return(FastCrossCurrent);
            ArrayPool<double>.Shared.Return(Xks);
            ArrayPool<double>.Shared.Return(JbarDen);
            ArrayPool<double>.Shared.Return(JbarNum);
            ArrayPool<double>.Shared.Return(SmoothedJks);
            ArrayPool<double>.Shared.Return(Jks);
            ArrayPool<double>.Shared.Return(D);
            ArrayPool<int>.Shared.Return(AInterpIdx);
            ArrayPool<int>.Shared.Return(BaseInterpIdx);
            ArrayPool<double>.Shared.Return(DeltaKs);
            ArrayPool<int>.Shared.Return(SmoothWrA);
            ArrayPool<int>.Shared.Return(SmoothWlA);
            ArrayPool<int>.Shared.Return(SmoothWr);
            ArrayPool<int>.Shared.Return(SmoothWl);
            ArrayPool<double>.Shared.Return(CumSumBufferA);
            ArrayPool<double>.Shared.Return(CumSumBuffer);
            ArrayPool<double>.Shared.Return(Anchor);
            ArrayPool<double>.Shared.Return(KeyUsage400);
            ArrayPool<ulong>.Shared.Return(ActiveColumnMask);
            ArrayPool<double>.Shared.Return(KsArr);
            ArrayPool<double>.Shared.Return(CArr);
            ArrayPool<double>.Shared.Return(Rbar);
            ArrayPool<double>.Shared.Return(Abar);
            ArrayPool<double>.Shared.Return(Pbar);
            ArrayPool<double>.Shared.Return(Xbar);
            ArrayPool<double>.Shared.Return(Jbar);
        }
    }

    public BmsStarRatingResult Compute(IReadOnlyList<BmsNoteTiming> noteTimings, int totalColumns, int rank, double clockRate = 1.0, BmsLayoutVariant? layout = null, double? judgementRate = null)
        => compute(noteTimings, totalColumns, rank, clockRate, layout ?? BmsLayout.VariantFromTotalColumns(totalColumns), judgementRate);

    public double ComputeStarRating(IReadOnlyList<BmsNoteTiming> noteTimings, int totalColumns, int rank, double clockRate = 1.0, BmsLayoutVariant? layout = null, double? judgementRate = null)
        => compute(noteTimings, totalColumns, rank, clockRate, layout ?? BmsLayout.VariantFromTotalColumns(totalColumns), judgementRate).StarRating;

    private BmsStarRatingResult compute(IReadOnlyList<BmsNoteTiming> noteTimings, int totalColumns, int rank, double clockRate, BmsLayoutVariant layout, double? judgementRate)
    {
        // === Basic Setup and Parsing ===
        if (totalColumns > 64)
            throw new ArgumentOutOfRangeException(nameof(totalColumns), totalColumns, @"Bitmask active-column tracking supports at most 64 columns.");

        TotalColumns = totalColumns;
        preprocessFile(noteTimings, rank, clockRate, layout, judgementRate);
        getCorners();

        var baseCount = baseCorners.Length;
        var aCount = aCorners.Length;
        var allCount = allCorners.Length;
        using var workspace = new ComputeWorkspace(baseCount, aCount, allCount, TotalColumns, noteSeq.Length, tailSeq.Length, lnSeq.Length);

        getActiveColumnMaskInto(workspace.ActiveColumnMask);
        getKeyUsage400Into(workspace.KeyUsage400);
        computeAnchorInto(workspace.KeyUsage400, workspace.Anchor);
        buildWindowBoundsInto(baseCorners, 500, workspace.SmoothWl, workspace.SmoothWr);
        buildWindowBoundsInto(aCorners, 250, workspace.SmoothWlA, workspace.SmoothWrA);

        computeJbarInto(workspace);
        computeXbarInto(workspace);
        computePbarInto(workspace);
        computeAbarInto(workspace);
        computeRbarInto(workspace);
        computeCAndKsInto(workspace);

        buildInterpIdxInto(allCorners, baseCorners, workspace.BaseInterpIdx);
        buildInterpIdxInto(allCorners, aCorners, workspace.AInterpIdx);

        computeDifficulty(workspace);
        var sr = computeStarRating(workspace);

        var percentile93 = Result.Percentile93;
        var percentile83 = Result.Percentile83;
        var weightedMean = Result.WeightedMean;

        Result = new BmsStarRatingResult
        {
            StarRating = sr,
            Percentile93 = percentile93,
            Percentile83 = percentile83,
            WeightedMean = weightedMean,
        };

        clearWorkingState();

        return Result;
    }

    private void clearWorkingState()
    {
        noteSeq = [];
        noteSeqByColumn = [];
        noteSeqByColumnStarts = [];
        noteSeqByColumnCounts = [];
        lnSeq = [];
        tailSeq = [];
        baseCorners = [];
        aCorners = [];
        allCorners = [];
    }

    private static double[] generateCrossCoeffs(int k)
    {
        if (k <= 0) return [-1];

        var len = k + 1;
        var coeffs = new double[len];

        if (k == 1)
        {
            coeffs[0] = coeffs[1] = 0.075;
            return coeffs;
        }

        var m = k >> 1;
        var outer = 0.05 * m + 0.075;

        if (k % 2 == 0)
        {
            coeffs[m] = 0.05;
            for (var i = 1; i < m; i++)
            {
                coeffs[m - i] = coeffs[m + i] = 0.15 + 0.10 * i;
            }
        }
        else
        {
            coeffs[m] = coeffs[m + 1] = outer;
            for (var i = 1; m - i > 0; i++)
            {
                coeffs[m - i] = coeffs[m + 1 + i] = 0.15 + 0.10 * i;
            }
        }

        coeffs[0] = coeffs[k] = outer;

        return coeffs;
    }

    private static double rescaleHigh(double sr)
    {
        if (sr <= 9) return sr;

        return 9 + (sr - 9) * (1.0 / 1.2);
    }

    /// <summary>Write cumulative sum into a pre-allocated buffer. Avoids allocating a new array per call.</summary>
    private static void cumulativeSum(double[] x, double[] f, double[] F)
    {
        F[0] = 0;
        for (var i = 1; i < x.Length; i++)
            F[i] = F[i - 1] + f[i - 1] * (x[i] - x[i - 1]);
    }

    /// <summary>
    /// Precompute sliding-window bounds for smoothOnCorners using a linear walk.
    /// leftIdx[i] = first index where x[idx] >= max(x[i]-window, x[0])
    /// rightIdx[i] = first index where x[idx] >= min(x[i]+window, x[^1])
    /// </summary>
    private static void buildWindowBoundsInto(double[] x, double window, int[] left, int[] right)
    {
        int l = 0, r = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var a = Math.Max(x[i] - window, x[0]);
            var b = Math.Min(x[i] + window, x[^1]);
            while (l < x.Length && x[l] < a) l++;
            while (r < x.Length && x[r] < b) r++;
            left[i] = Math.Min(l, x.Length - 1);
            right[i] = Math.Min(r, x.Length - 1);
        }
    }

    private static void smoothOnCornersFastInto(double[] x, double[] f, double window, double scale, bool averageMode,
                                                int[] wl, int[] wr, double[] F, double[] g)
    {
        cumulativeSum(x, f, F);

        for (var i = 0; i < x.Length; i++)
        {
            var a = Math.Max(x[i] - window, x[0]);
            var b = Math.Min(x[i] + window, x[^1]);

            // queryCumSum(b) - queryCumSum(a) using precomputed indices (no binary search)
            double qa, qb;

            if (a <= x[0])
                qa = 0;
            else
            {
                var seg = wl[i] - 1;
                qa = F[seg] + f[seg] * (a - x[seg]);
            }

            if (b >= x[^1])
                qb = F[x.Length - 1];
            else
            {
                var seg = wr[i] - 1;
                qb = F[seg] + f[seg] * (b - x[seg]);
            }

            var val = qb - qa;
            g[i] = averageMode
                ? b - a > 0 ? val / (b - a) : 0
                : scale * val;
        }
    }

    /// <summary>
    /// Precompute a walking index: for each position in newX, the index of the greatest oldX value ≤ newX[i].
    /// Both arrays must be sorted ascending. Uses a two-pointer walk O(newX+oldX) instead of binary search.
    /// </summary>
    private static void buildInterpIdxInto(double[] newX, double[] oldX, int[] idx)
    {
        var j = 0;
        for (var i = 0; i < newX.Length; i++)
        {
            while (j < oldX.Length - 1 && oldX[j + 1] <= newX[i])
                j++;
            idx[i] = j;
        }
    }

    private static double lnSum(double a, double b, double[] points, int pointCount, double[] cumsum, double[] values)
    {
        // Locate the segments that contain a and b using bisect_right semantics.
        var i = searchSortedRight(points, pointCount, a) - 1;
        var j = searchSortedRight(points, pointCount, b) - 1;

        double total;
        if (i == j)
        {
            // Both a and b lie in the same segment.
            total = (b - a) * values[i];
        }
        else
        {
            // First segment: from a to the end of the i-th segment.
            total = (points[i + 1] - a) * values[i];
            // Full segments between i+1 and j-1.
            total += cumsum[j] - cumsum[i + 1];
            // Last segment: from start of segment j to b.
            total += (b - points[j]) * values[j];
        }

        return total;
    }

    // ---- Binary search helpers ----
    // Pure binary search, O(log n) even with duplicate values.
    // Returns the leftmost index where array[index] >= value.

    private static int searchSortedLeft(double[] array, double value) => searchSortedLeft(array, array.Length, value);

    private static int searchSortedLeft(double[] array, int length, double value)
    {
        int lo = 0, hi = length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (array[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private static int searchSortedLeft(NoteEntry[] array, int start, int count, double value)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (array[start + mid].Head < value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private static int searchSortedRight(double[] array, double value) => searchSortedRight(array, array.Length, value);

    private static int searchSortedRight(double[] array, int length, double value)
    {
        int lo = 0, hi = length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (array[mid] <= value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private int getColumnNoteStart(int column) => noteSeqByColumnStarts[column];

    private int getColumnNoteCount(int column) => noteSeqByColumnCounts[column];

    private static double[] toDedupedFilteredArray(double[] sorted, int count, double maxTime)
    {
        var writeCount = 0;
        for (var i = 0; i < count; i++)
        {
            var v = sorted[i];
            if (v >= 0 && v <= maxTime)
            {
                if (writeCount == 0 || v > sorted[writeCount - 1])
                    sorted[writeCount++] = v;
            }
        }

        if (writeCount == 0)
            return [];

        if (writeCount == count && count == sorted.Length)
            return sorted;

        var result = new double[writeCount];
        Array.Copy(sorted, result, writeCount);
        return result;
    }

    private static double[] mergeSortedUnique(double[] a, double[] b)
    {
        // Counting first keeps the union exact-sized instead of paying for a max-length buffer and a trim copy.
        var mergedCount = mergeSortedUniqueInto(a, b, []);
        if (mergedCount == 0)
            return [];

        var result = new double[mergedCount];
        mergeSortedUniqueInto(a, b, result);
        return result;
    }

    private static int mergeSortedUniqueInto(double[] a, double[] b, double[] destination)
    {
        int i = 0, j = 0, k = 0;
        double last = 0;
        var hasLast = false;

        while (i < a.Length && j < b.Length)
        {
            var va = a[i];
            var vb = b[j];

            if (va < vb)
            {
                appendUnique(va, destination, ref k, ref last, ref hasLast);
                i++;
            }
            else if (vb < va)
            {
                appendUnique(vb, destination, ref k, ref last, ref hasLast);
                j++;
            }
            else
            {
                appendUnique(va, destination, ref k, ref last, ref hasLast);
                i++;
                j++;
            }
        }

        while (i < a.Length)
            appendUnique(a[i++], destination, ref k, ref last, ref hasLast);

        while (j < b.Length)
            appendUnique(b[j++], destination, ref k, ref last, ref hasLast);

        return k;
    }

    private static void appendUnique(double value, double[] destination, ref int writeCount, ref double last, ref bool hasLast)
    {
        if (hasLast && value <= last)
            return;

        if (writeCount < destination.Length)
            destination[writeCount] = value;

        last = value;
        hasLast = true;
        writeCount++;
    }


    /// <summary>Walk forward from hint to find leftmost index with array[idx] >= value.
    /// Values must be non-decreasing between calls with the same hint variable.</summary>
    private static int walkForward(double[] array, double value, int hint)
    {
        while (hint < array.Length && array[hint] < value) hint++;
        return hint;
    }

    /// <summary>Insertion sort in descending order. Avoids delegate allocation overhead of Array.Sort with Comparison.</summary>
    private static void sortDescending(double[] arr, int len)
    {
        for (var i = 1; i < len; i++)
        {
            var key = arr[i];
            var j = i - 1;
            while (j >= 0 && arr[j] < key)
            {
                arr[j + 1] = arr[j];
                j--;
            }

            arr[j + 1] = key;
        }
    }

    private sealed class EventTimeComparer : IComparer<(double time, double change)>
    {
        public int Compare((double time, double change) x, (double time, double change) y) => x.time.CompareTo(y.time);
    }

    private void preprocessFile(IReadOnlyList<BmsNoteTiming> noteTimings, int rank, double clockRate, BmsLayoutVariant layout, double? judgementRate)
    {
        HitLeniencyX = judgementRate.HasValue ? BmsHitLeniency.FromJudgementRate(judgementRate.Value, layout) : BmsHitLeniency.FromRank(rank, layout);

        var sortedNotes = new List<(int column, double head, double tail, int order)>(noteTimings.Count);
        for (var i = 0; i < noteTimings.Count; i++)
        {
            var obj = noteTimings[i];
            var head = Math.Floor(obj.StartTime / clockRate);
            var tail = obj.EndTime > obj.StartTime ? Math.Floor(obj.EndTime / clockRate) : -1;
            sortedNotes.Add((obj.Column, head, tail, i));
        }

        sortedNotes.Sort(static (a, b) =>
        {
            var timeComparison = a.head.CompareTo(b.head);
            if (timeComparison != 0) return timeComparison;

            var columnComparison = a.column.CompareTo(b.column);
            return columnComparison != 0 ? columnComparison : a.order.CompareTo(b.order);
        });

        var columnCounts = new int[TotalColumns];
        var lnCount = 0;
        foreach (var sortedNote in sortedNotes)
        {
            columnCounts[sortedNote.column]++;
            if (sortedNote.tail >= 0)
                lnCount++;
        }

        noteSeq = new NoteEntry[sortedNotes.Count];
        noteSeqByColumnStarts = new int[TotalColumns];
        noteSeqByColumnCounts = columnCounts;
        noteSeqByColumn = new NoteEntry[sortedNotes.Count];
        lnSeq = new NoteEntry[lnCount];

        var runningStart = 0;
        for (var k = 0; k < TotalColumns; k++)
        {
            noteSeqByColumnStarts[k] = runningStart;
            runningStart += columnCounts[k];
        }

        var columnWriteIdx = new int[TotalColumns];
        Array.Copy(noteSeqByColumnStarts, columnWriteIdx, TotalColumns);

        var sortedTails = new List<(NoteEntry note, int lnOrder)>(lnCount);
        var noteWriteIdx = 0;
        var lnWriteIdx = 0;
        foreach (var sortedNote in sortedNotes)
        {
            var note = new NoteEntry(sortedNote.column, sortedNote.head, sortedNote.tail);
            noteSeq[noteWriteIdx++] = note;
            noteSeqByColumn[columnWriteIdx[note.Column]++] = note;

            if (note.Tail >= 0)
            {
                var lnOrder = lnWriteIdx;
                lnSeq[lnWriteIdx++] = note;
                sortedTails.Add((note, lnOrder));
            }
        }

        sortedTails.Sort(static (a, b) =>
        {
            var tailComparison = a.note.Tail.CompareTo(b.note.Tail);
            return tailComparison != 0 ? tailComparison : a.lnOrder.CompareTo(b.lnOrder);
        });

        tailSeq = new NoteEntry[sortedTails.Count];
        for (var i = 0; i < sortedTails.Count; i++)
            tailSeq[i] = sortedTails[i].note;

        TotalTimeT = Math.Max(
            noteSeq.Length > 0 ? noteSeq[^1].Head : 0,
            tailSeq.Length > 0 ? tailSeq[^1].Tail : 0
        ) + 1;
    }

    private void getCorners()
    {
        // The boundary-derived raw counts are fixed up front, so exact arrays sidestep List growth and the extra copy that follows it.
        var boundaryCount = noteSeq.Length + lnSeq.Length;
        var rawBase = new double[boundaryCount * 4 + 2];
        var rawA = new double[boundaryCount * 3 + 2];
        var seedCount = fillCornerSeeds(rawBase, rawA);

        var baseWrite = seedCount;
        for (var i = 0; i < seedCount; i++)
        {
            var s = rawBase[i];
            rawBase[baseWrite++] = s + 501;
            rawBase[baseWrite++] = s - 499;
            rawBase[baseWrite++] = s + 1;
        }

        rawBase[baseWrite++] = 0;
        rawBase[baseWrite++] = TotalTimeT;

        var aWrite = seedCount;
        for (var i = 0; i < seedCount; i++)
        {
            var s = rawA[i];
            rawA[aWrite++] = s + 1000;
            rawA[aWrite++] = s - 1000;
        }

        rawA[aWrite++] = 0;
        rawA[aWrite++] = TotalTimeT;

        Array.Sort(rawBase, 0, baseWrite);
        Array.Sort(rawA, 0, aWrite);

        baseCorners = toDedupedFilteredArray(rawBase, baseWrite, TotalTimeT);
        aCorners = toDedupedFilteredArray(rawA, aWrite, TotalTimeT);

        allCorners = mergeSortedUnique(baseCorners, aCorners);
    }

    private int fillCornerSeeds(double[] rawBase, double[] rawA)
    {
        var writeCount = 0;
        foreach (var note in noteSeq)
        {
            rawBase[writeCount] = note.Head;
            rawA[writeCount++] = note.Head;
            if (note.Tail >= 0)
            {
                rawBase[writeCount] = note.Tail;
                rawA[writeCount++] = note.Tail;
            }
        }

        return writeCount;
    }


    private void getActiveColumnMaskInto(ulong[] usage)
    {
        Array.Clear(usage, 0, baseCorners.Length);

        var leftHint = 0;
        foreach (var note in noteSeq)
        {
            var start = Math.Max(note.Head - 150, 0);
            var end = note.Tail < 0 ? note.Head + 150 : Math.Min(note.Tail + 150, TotalTimeT - 1);

            leftHint = walkForward(baseCorners, start, leftHint);
            var left = leftHint;
            var right = searchSortedLeft(baseCorners, end);
            var bit = 1UL << note.Column;
            for (var i = left; i < right; i++)
                usage[i] |= bit;
        }
    }

    private void getKeyUsage400Into(double[] usage)
    {
        var nCols = TotalColumns;
        var baseCount = baseCorners.Length;
        Array.Clear(usage, 0, nCols * baseCount);

        int leftHint = 0, left400Hint = 0;
        foreach (var note in noteSeq)
        {
            var start = Math.Max(note.Head, 0);
            var end = note.Tail < 0 ? note.Head : Math.Min(note.Tail, TotalTimeT - 1);

            left400Hint = walkForward(baseCorners, start - 400, left400Hint);
            leftHint = walkForward(baseCorners, start, leftHint);
            var left400 = left400Hint;
            var left = leftHint;
            var right = searchSortedLeft(baseCorners, end);
            var right400 = searchSortedLeft(baseCorners, end + 400);

            var lnBonus = 3.75 + Math.Min(end - start, 1500) / 150.0;
            var column = note.Column;

            for (int i = left, offset = left * nCols + column; i < right; i++, offset += nCols)
                usage[offset] += lnBonus;

            for (int i = left400, offset = left400 * nCols + column; i < left && i >= 0 && i < baseCorners.Length; i++, offset += nCols)
                usage[offset] += 3.75 - 3.75 / 160000.0 * (baseCorners[i] - start) * (baseCorners[i] - start);

            for (int i = right, offset = right * nCols + column; i < right400 && i < baseCorners.Length; i++, offset += nCols)
                usage[offset] += 3.75 - 3.75 / 160000.0 * (baseCorners[i] - end) * (baseCorners[i] - end);
        }
    }

    private void computeAnchorInto(double[] keyUsage400, double[] result)
    {
        var nCols = TotalColumns;
        var baseCount = baseCorners.Length;
        if (anchorCountsScratch.Length < nCols)
            Array.Resize(ref anchorCountsScratch, nCols);

        var counts = anchorCountsScratch;
        Array.Clear(result, 0, baseCount);

        for (var idx = 0; idx < baseCount; idx++)
        {
            var offset = idx * nCols;
            for (var k = 0; k < nCols; k++)
                counts[k] = keyUsage400[offset + k];

            sortDescending(counts, nCols); // avoids delegate + boxing overhead

            var nonZeroCount = 0;
            for (var i = 0; i < nCols && counts[i] != 0; i++)
                nonZeroCount++;

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

                result[idx] = walk / maxWalk;
            }
        }

        for (var i = 0; i < result.Length; i++)
        {
            var r = result[i] - 0.22;
            result[i] = 1 + Math.Min(result[i] - 0.18, 5 * r * r * r);
        }
    }

    private void computeJbarInto(ComputeWorkspace workspace)
    {
        var nCols = TotalColumns;
        var baseCount = baseCorners.Length;
        var jbar = workspace.Jbar;
        var deltaKs = workspace.DeltaKs;
        var totalDeltas = nCols * baseCount;
        Array.Fill(deltaKs, 1e9, 0, totalDeltas);

        double jackNerfer(double delta)
        {
            var x = 0.15 + Math.Abs(delta - 0.08);
            return 1 - 7e-5 / (x * x * x * x);
        }

        var jks = workspace.Jks;
        var smoothedJks = workspace.SmoothedJks;
        var jbarNum = workspace.JbarNum;
        var jbarDen = workspace.JbarDen;
        var smoothWl = workspace.SmoothWl;
        var smoothWr = workspace.SmoothWr;
        var cumSumBuffer = workspace.CumSumBuffer;

        Array.Clear(jbarNum, 0, baseCount);
        Array.Clear(jbarDen, 0, baseCount);

        for (var k = 0; k < nCols; k++)
        {
            var noteStart = getColumnNoteStart(k);
            var noteCount = getColumnNoteCount(k);
            var dksOffset = k * baseCount;
            Array.Clear(jks, 0, baseCount);

            var baseHint = 0;
            for (var i = 0; i < noteCount - 1; i++)
            {
                var start = noteSeqByColumn[noteStart + i].Head;
                var end = noteSeqByColumn[noteStart + i + 1].Head;

                // The sliding hint keeps this O(n) across a sorted note stream instead of paying a binary search per interval.
                baseHint = walkForward(baseCorners, start, baseHint);
                var left = baseHint;
                baseHint = walkForward(baseCorners, end, baseHint);
                var right = baseHint;
                if (left >= right) continue;

                var delta = 0.001 * (end - start);
                var val = 1.0 / delta / (delta + 0.11 * Math.Sqrt(Math.Sqrt(HitLeniencyX)));
                var jVal = val * jackNerfer(delta);

                for (var j = left; j < right; j++)
                {
                    jks[j] = jVal;
                    deltaKs[dksOffset + j] = delta;
                }
            }

            smoothOnCornersFastInto(baseCorners, jks, 500, 0.001, false, smoothWl, smoothWr, cumSumBuffer, smoothedJks);

            for (var i = 0; i < baseCount; i++)
            {
                var v = smoothedJks[i];
                if (v < 0) v = 0;
                var w = 1.0 / deltaKs[dksOffset + i];
                jbarNum[i] += v * v * v * v * v * w;
                jbarDen[i] += w;
            }
        }

        for (var i = 0; i < baseCount; i++)
            jbar[i] = Math.Pow(jbarNum[i] / Math.Max(1e-9, jbarDen[i]), 1.0 / 5.0);
    }

    private void computeXbarInto(ComputeWorkspace workspace)
    {
        var nCols = TotalColumns;
        var baseCount = baseCorners.Length;
        var crossCoeff = generateCrossCoeffs(nCols);
        var xbar = workspace.Xbar;
        var xks = workspace.Xks;
        var xBase = workspace.XBase;
        var sqrtSums = workspace.SqrtSums;
        var fastCrossCurrent = workspace.FastCrossCurrent;
        var fastCrossPrevious = workspace.FastCrossPrevious;
        var activeColumnMask = workspace.ActiveColumnMask;
        var smoothWl = workspace.SmoothWl;
        var smoothWr = workspace.SmoothWr;
        var cumSumBuffer = workspace.CumSumBuffer;

        Array.Clear(fastCrossPrevious, 0, baseCount);
        Array.Clear(xBase, 0, baseCount);
        Array.Clear(sqrtSums, 0, baseCount);

        for (var k = 0; k <= nCols; k++)
        {
            Array.Clear(xks, 0, baseCount);
            Array.Clear(fastCrossCurrent, 0, baseCount);

            var baseHint = 0;

            void processInterval(double start, double end)
            {
                baseHint = walkForward(baseCorners, start, baseHint);
                var left = baseHint;
                baseHint = walkForward(baseCorners, end, baseHint);
                var right = baseHint;
                if (left >= right) return;

                var delta = 0.001 * (end - start);
                var val = 0.16 / (Math.Max(HitLeniencyX, delta) * Math.Max(HitLeniencyX, delta));

                // Matching the Python edge-column check keeps the fast cross term from counting gaps where one side was never actually occupied.
                bool col1AbsentAtBoth;
                bool col2AbsentAtBoth;

                var maskLeft = activeColumnMask[left];
                var maskRight = activeColumnMask[right];

                if (k - 1 >= 0)
                {
                    var bit = 1UL << (k - 1);
                    col1AbsentAtBoth = (maskLeft & bit) == 0 && (maskRight & bit) == 0;
                }
                else
                    col1AbsentAtBoth = true;

                if (k < TotalColumns)
                {
                    var bit = 1UL << k;
                    col2AbsentAtBoth = (maskLeft & bit) == 0 && (maskRight & bit) == 0;
                }
                else
                    col2AbsentAtBoth = true;

                if (col1AbsentAtBoth || col2AbsentAtBoth)
                    val *= 1 - crossCoeff[k];

                var fastVal = Math.Max(0, 0.4 / (Math.Max(delta, Math.Max(0.06, 0.75 * HitLeniencyX))
                                                 * Math.Max(delta, Math.Max(0.06, 0.75 * HitLeniencyX))) - 80);

                for (var j = left; j < right; j++)
                {
                    xks[j] = val;
                    fastCrossCurrent[j] = fastVal;
                }
            }

            if (k == 0 || k == TotalColumns)
            {
                var column = k == 0 ? 0 : TotalColumns - 1;
                var noteStart = getColumnNoteStart(column);
                var noteCount = getColumnNoteCount(column);
                for (var i = 1; i < noteCount; i++)
                    processInterval(noteSeqByColumn[noteStart + i - 1].Head, noteSeqByColumn[noteStart + i].Head);
            }
            else
            {
                var leftStart = getColumnNoteStart(k - 1);
                var rightStart = getColumnNoteStart(k);
                var leftCount = getColumnNoteCount(k - 1);
                var rightCount = getColumnNoteCount(k);
                var leftIdx = 0;
                var rightIdx = 0;

                if (leftCount > 0 || rightCount > 0)
                {
                    double previous;
                    if (rightCount == 0 || (leftIdx < leftCount && noteSeqByColumn[leftStart + leftIdx].Head <= noteSeqByColumn[rightStart + rightIdx].Head))
                        previous = noteSeqByColumn[leftStart + leftIdx++].Head;
                    else
                        previous = noteSeqByColumn[rightStart + rightIdx++].Head;

                    while (leftIdx < leftCount || rightIdx < rightCount)
                    {
                        double current;
                        if (rightIdx >= rightCount || (leftIdx < leftCount && noteSeqByColumn[leftStart + leftIdx].Head <= noteSeqByColumn[rightStart + rightIdx].Head))
                            current = noteSeqByColumn[leftStart + leftIdx++].Head;
                        else
                            current = noteSeqByColumn[rightStart + rightIdx++].Head;

                        processInterval(previous, current);
                        previous = current;
                    }
                }
            }

            for (var i = 0; i < baseCount; i++)
            {
                xBase[i] += xks[i] * crossCoeff[k];

                if (k > 0)
                    sqrtSums[i] += Math.Sqrt(fastCrossPrevious[i] * crossCoeff[k - 1] * fastCrossCurrent[i] * crossCoeff[k]);
            }

            (fastCrossPrevious, fastCrossCurrent) = (fastCrossCurrent, fastCrossPrevious);
        }

        for (var i = 0; i < baseCount; i++)
            xBase[i] += sqrtSums[i];

        smoothOnCornersFastInto(baseCorners, xBase, 500, 0.001, false, smoothWl, smoothWr, cumSumBuffer, xbar);
    }

    private void computePbarInto(ComputeWorkspace workspace)
    {
        double streamBooster(double delta)
        {
            var r = 7.5 / delta;
            if (r > 160 && r < 360)
                return 1 + 1.7e-7 * (r - 160) * (r - 360) * (r - 360);

            return 1;
        }

        var pbar = workspace.Pbar;
        var pStep = workspace.PStep;
        var anchor = workspace.Anchor;
        var smoothWl = workspace.SmoothWl;
        var smoothWr = workspace.SmoothWr;
        var cumSumBuffer = workspace.CumSumBuffer;
        var (lnPoints, lnPointCount, lnCumsum, lnValues) = lnBodiesCountSparseRepresentation(workspace);

        var baseCount = baseCorners.Length;
        Array.Clear(pStep, 0, baseCount);

        for (var i = 0; i < noteSeq.Length - 1; i++)
        {
            var hl = noteSeq[i].Head;
            var hr = noteSeq[i + 1].Head;
            var deltaTime = hr - hl;

            if (deltaTime < 1e-9)
            {
                // The spike has to land on the shared head instant, otherwise simultaneous notes lose their density contribution after smoothing.
                var spike = 1000 * Math.Sqrt(Math.Sqrt(0.02 * (4.0 / HitLeniencyX - 24)));
                var left = searchSortedLeft(baseCorners, hl);
                var right = searchSortedRight(baseCorners, hl);
                for (var j = left; j < right; j++)
                    pStep[j] += spike;

                continue;
            }

            var lIdx = searchSortedLeft(baseCorners, hl);
            var rIdx = searchSortedLeft(baseCorners, hr);
            if (lIdx >= rIdx) continue;

            var delta = 0.001 * deltaTime;
            var v = 1 + 6 * 0.001 * lnSum(hl, hr, lnPoints, lnPointCount, lnCumsum, lnValues);
            var bVal = streamBooster(delta);

            double inc;
            var xf2 = 0.08 / HitLeniencyX;
            if (delta < 2 * HitLeniencyX / 3)
            {
                var dShift = delta - HitLeniencyX / 2;
                inc = 1.0 / delta * Math.Sqrt(Math.Sqrt(xf2 * (1 - 24.0 / HitLeniencyX * dShift * dShift))) * Math.Max(bVal, v);
            }
            else
            {
                var x6 = HitLeniencyX / 6;
                inc = 1.0 / delta * Math.Sqrt(Math.Sqrt(xf2 * (1 - 24.0 / HitLeniencyX * x6 * x6))) * Math.Max(bVal, v);
            }

            for (var j = lIdx; j < rIdx; j++)
                pStep[j] += Math.Min(inc * anchor[j], Math.Max(inc, inc * 2 - 10));
        }

        smoothOnCornersFastInto(baseCorners, pStep, 500, 0.001, false, smoothWl, smoothWr, cumSumBuffer, pbar);
    }

    private void computeAbarInto(ComputeWorkspace workspace)
    {
        var nCols = TotalColumns;
        var baseCount = baseCorners.Length;
        var abar = workspace.Abar;
        var deltaKs = workspace.DeltaKs;
        var totalDeltas = nCols * baseCount;
        var dks = workspace.Dks;
        var aStep = workspace.AStep;
        var activeColumnMask = workspace.ActiveColumnMask;
        var smoothWlA = workspace.SmoothWlA;
        var smoothWrA = workspace.SmoothWrA;
        var cumSumBufferA = workspace.CumSumBufferA;

        Array.Clear(dks, 0, totalDeltas);
        Array.Fill(aStep, 1.0, 0, aCorners.Length);

        for (var i = 0; i < baseCount; i++)
        {
            var mask = activeColumnMask[i];
            var prevActive = -1;
            for (var k = 0; k < nCols; k++)
            {
                if ((mask & (1UL << k)) == 0) continue;

                if (prevActive >= 0)
                {
                    var prevDelta = deltaKs[prevActive * baseCount + i];
                    var currentDelta = deltaKs[k * baseCount + i];
                    dks[prevActive * baseCount + i] = Math.Abs(prevDelta - currentDelta)
                                                      + 0.4 * Math.Max(0, Math.Max(prevDelta, currentDelta) - 0.11);
                }

                prevActive = k;
            }
        }

        for (var i = 0; i < aCorners.Length; i++)
        {
            var idx = searchSortedLeft(baseCorners, aCorners[i]);
            if (idx >= baseCorners.Length) idx = baseCorners.Length - 1;

            var mask = activeColumnMask[idx];
            var prevActive = -1;
            for (var k = 0; k < nCols; k++)
            {
                if ((mask & (1UL << k)) == 0) continue;

                if (prevActive >= 0)
                {
                    var prevDelta = deltaKs[prevActive * baseCount + idx];
                    var currentDelta = deltaKs[k * baseCount + idx];
                    var dVal = dks[prevActive * baseCount + idx];

                    if (dVal < 0.02)
                        aStep[i] *= Math.Min(0.75 + 0.5 * Math.Max(prevDelta, currentDelta), 1);
                    else if (dVal < 0.07)
                        aStep[i] *= Math.Min(0.65 + 5 * dVal + 0.5 * Math.Max(prevDelta, currentDelta), 1);
                }

                prevActive = k;
            }
        }

        smoothOnCornersFastInto(aCorners, aStep, 250, 1.0, true, smoothWlA, smoothWrA, cumSumBufferA, abar);
    }

    private void computeRbarInto(ComputeWorkspace workspace)
    {
        var baseCount = baseCorners.Length;
        var rbar = workspace.Rbar;
        var rStep = workspace.RStep;
        var iList = workspace.IRelease;
        var smoothWl = workspace.SmoothWl;
        var smoothWr = workspace.SmoothWr;
        var cumSumBuffer = workspace.CumSumBuffer;

        Array.Clear(rStep, 0, baseCount);

        for (var i = 0; i < tailSeq.Length; i++)
        {
            var tail = tailSeq[i];
            var k = tail.Column;
            var hi = tail.Head;
            var ti = tail.Tail;
            var colNoteStart = getColumnNoteStart(k);
            var colNoteCount = getColumnNoteCount(k);
            var idx = searchSortedLeft(noteSeqByColumn, colNoteStart, colNoteCount, hi);
            var hNext = idx + 1 < colNoteCount ? noteSeqByColumn[colNoteStart + idx + 1].Head : 1e9;

            var iH = 0.001 * Math.Abs(ti - hi - 80) / HitLeniencyX;
            var iT = 0.001 * Math.Abs(hNext - ti - 80) / HitLeniencyX;
            iList[i] = 2.0 / (2 + Math.Exp(-5 * (iH - 0.75)) + Math.Exp(-5 * (iT - 0.75)));
        }

        for (var i = 0; i < tailSeq.Length - 1; i++)
        {
            var tStart = tailSeq[i].Tail;
            var tEnd = tailSeq[i + 1].Tail;

            var left = searchSortedLeft(baseCorners, tStart);
            var right = searchSortedLeft(baseCorners, tEnd);
            if (left >= right) continue;

            var deltaR = 0.001 * (tEnd - tStart);
            var rVal = 0.08 / Math.Sqrt(deltaR) / HitLeniencyX * (1 + 0.8 * (iList[i] + iList[i + 1]));

            for (var j = left; j < right; j++)
                rStep[j] = rVal;
        }

        smoothOnCornersFastInto(baseCorners, rStep, 500, 0.001, false, smoothWl, smoothWr, cumSumBuffer, rbar);
    }

    private void computeCAndKsInto(ComputeWorkspace workspace)
    {
        var cArr = workspace.CArr;
        var ksArr = workspace.KsArr;
        var noteHitTimes = workspace.NoteHitTimes;
        var activeColumnMask = workspace.ActiveColumnMask;
        for (var i = 0; i < noteSeq.Length; i++)
            noteHitTimes[i] = noteSeq[i].Head;

        for (var i = 0; i < baseCorners.Length; i++)
        {
            var low = baseCorners[i] - 500;
            var high = baseCorners[i] + 500;
            var cnt = searchSortedLeft(noteHitTimes, noteSeq.Length, high) - searchSortedLeft(noteHitTimes, noteSeq.Length, low);
            cArr[i] = cnt;
        }

        for (var i = 0; i < baseCorners.Length; i++)
        {
            var count = BitOperations.PopCount(activeColumnMask[i]);
            ksArr[i] = Math.Max(count, 1);
        }
    }

    private static double interpolateAt(double x, double[] oldX, int idx, double[] oldVals)
    {
        if (x <= oldX[0])
            return oldVals[0];

        if (x >= oldX[^1])
            return oldVals[oldX.Length - 1];

        var t = (x - oldX[idx]) / (oldX[idx + 1] - oldX[idx]);
        return oldVals[idx] + t * (oldVals[idx + 1] - oldVals[idx]);
    }

    private void computeDifficulty(ComputeWorkspace workspace)
    {
        var d = workspace.D;
        var jbar = workspace.Jbar;
        var xbar = workspace.Xbar;
        var pbar = workspace.Pbar;
        var abar = workspace.Abar;
        var rbar = workspace.Rbar;
        var cArr = workspace.CArr;
        var ksArr = workspace.KsArr;
        var baseInterpIdx = workspace.BaseInterpIdx;
        var aInterpIdx = workspace.AInterpIdx;

        for (var i = 0; i < allCorners.Length; i++)
        {
            var x = allCorners[i];
            var baseIdx = baseInterpIdx[i];
            var aIdx = aInterpIdx[i];

            var ks = ksArr[baseIdx];
            var c = cArr[baseIdx];
            var a = interpolateAt(x, aCorners, aIdx, abar);
            var j = interpolateAt(x, baseCorners, baseIdx, jbar);
            var xVal = interpolateAt(x, baseCorners, baseIdx, xbar);
            var p = interpolateAt(x, baseCorners, baseIdx, pbar);
            var r = interpolateAt(x, baseCorners, baseIdx, rbar);
            var jCap = Math.Min(j, 8 + 0.85 * j);
            var a3Ks = Math.Pow(a, 3.0 / ks);

            var s1 = a3Ks * jCap * Math.Sqrt(a3Ks * jCap);
            var streamTerm = 0.8 * p + r * 35.0 / (c + 8);
            var a23 = Math.Pow(a, 2.0 / 3.0);
            var s2Base = a23 * streamTerm;
            var s2 = s2Base * Math.Sqrt(s2Base);

            var s = Math.Pow(0.4 * s1 + 0.6 * s2, 2.0 / 3.0);
            var t = a3Ks * xVal / (xVal + s + 1);
            d[i] = 2.7 * Math.Sqrt(s) * t * Math.Sqrt(t) + s * 0.27;
        }
    }

    private static (double p93Sum, double p83Sum) computePercentileSumsFromSorted(double[] sortedDifficulty, double[] sortedWeights, int count, double totalWeight)
    {
        var percentileIdx = target_percentiles.Length - 1;
        var nextTarget = target_percentiles[percentileIdx] * totalWeight;
        double cumulativeWeight = 0;
        double p93Sum = 0;
        double p83Sum = 0;

        for (var i = 0; i < count; i++)
        {
            var dVal = sortedDifficulty[i];
            cumulativeWeight += sortedWeights[i];

            while (percentileIdx >= 0 && cumulativeWeight >= nextTarget)
            {
                if (percentileIdx < 4)
                    p93Sum += dVal;
                else
                    p83Sum += dVal;

                percentileIdx--;
                if (percentileIdx >= 0)
                    nextTarget = target_percentiles[percentileIdx] * totalWeight;
            }
        }

        if (percentileIdx >= 0)
        {
            var last = sortedDifficulty[count - 1];
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

    private double computeStarRating(ComputeWorkspace workspace)
    {
        var d = workspace.D;
        var cArr = workspace.CArr;
        var baseInterpIdx = workspace.BaseInterpIdx;
        var effectiveWeights = workspace.EffectiveWeights;
        var n = allCorners.Length;

        effectiveWeights[0] = cArr[baseInterpIdx[0]] * (allCorners[1] - allCorners[0]) / 2.0;
        effectiveWeights[n - 1] = cArr[baseInterpIdx[n - 1]] * (allCorners[n - 1] - allCorners[n - 2]) / 2.0;

        double totalWeight = 0;
        double weightedSum5 = 0;

        var firstDifficulty = d[0];
        var firstWeight = effectiveWeights[0];
        totalWeight += firstWeight;
        weightedSum5 += firstDifficulty * firstDifficulty * firstDifficulty * firstDifficulty * firstDifficulty * firstWeight;

        for (var i = 1; i < n - 1; i++)
        {
            var weight = cArr[baseInterpIdx[i]] * (allCorners[i + 1] - allCorners[i - 1]) / 2.0;
            effectiveWeights[i] = weight;

            var dVal = d[i];
            totalWeight += weight;
            weightedSum5 += dVal * dVal * dVal * dVal * dVal * weight;
        }

        var lastDifficulty = d[n - 1];
        var lastWeight = effectiveWeights[n - 1];
        totalWeight += lastWeight;
        weightedSum5 += lastDifficulty * lastDifficulty * lastDifficulty * lastDifficulty * lastDifficulty * lastWeight;

        Array.Sort(d, effectiveWeights, 0, n);

        // Weighted percentile thresholds depend on the exact cumulative weights of the fully sorted sequence,
        // so we keep the complete sort and only move the order-independent weighted-mean accumulation out of the hot scan.
        var (p93Sum, p83Sum) = computePercentileSumsFromSorted(d, effectiveWeights, n, totalWeight);

        var p93 = p93Sum / 4.0;
        var p83 = p83Sum / 4.0;
        var weightedMean = Math.Pow(weightedSum5 / totalWeight, 1.0 / 5.0);

        var sr = 0.88 * p93 * 0.25 + 0.94 * p83 * 0.2 + weightedMean * 0.55;
        sr = sr / 8.0 * 8.0;

        double lnNoteBonus = 0;
        foreach (var ln in lnSeq)
            lnNoteBonus += Math.Min(ln.Tail - ln.Head, 1000) / 200.0;

        var totalNotes = noteSeq.Length + 0.5 * lnNoteBonus;
        sr *= totalNotes / (totalNotes + 60);

        sr = rescaleHigh(sr);
        sr *= 0.975;

        Result.Percentile93 = p93;
        Result.Percentile83 = p83;
        Result.WeightedMean = weightedMean;

        return sr;
    }

    // -----End of Helper methods--------

    private (double[] points, int pointCount, double[] cumsum, double[] values) lnBodiesCountSparseRepresentation(ComputeWorkspace workspace)
    {
        var eventCount = lnSeq.Length * 3;
        var events = workspace.LnEvents;
        var pointCandidates = workspace.LnPointCandidates;
        var cumsum = workspace.LnCumsum;
        var values = workspace.LnValues;
        var eventIdx = 0;

        foreach (var (_, head, tail) in lnSeq)
        {
            var t0 = Math.Min(head + 60, tail);
            var t1 = Math.Min(head + 120, tail);
            events[eventIdx++] = (t0, 1.3);
            events[eventIdx++] = (t1, -0.3);
            events[eventIdx++] = (tail, -1);
        }

        Array.Sort(events, 0, eventCount, event_time_comparer);

        pointCandidates[0] = 0;
        pointCandidates[1] = TotalTimeT;
        for (var i = 0; i < eventCount; i++)
            pointCandidates[i + 2] = events[i].time;

        var candidateCount = eventCount + 2;
        Array.Sort(pointCandidates, 0, candidateCount);

        var writeIdx = 0;
        for (var i = 0; i < candidateCount; i++)
        {
            var v = pointCandidates[i];
            if (writeIdx == 0 || v > pointCandidates[writeIdx - 1])
                pointCandidates[writeIdx++] = v;
        }

        double curr = 0;
        eventIdx = 0;
        cumsum[0] = 0;

        for (var i = 0; i < writeIdx - 1; i++)
        {
            var t = pointCandidates[i];
            while (eventIdx < eventCount && events[eventIdx].time <= t)
                curr += events[eventIdx++].change;

            var v = Math.Min(curr, 2.5 + 0.5 * curr);
            values[i] = v;
            var segLength = pointCandidates[i + 1] - pointCandidates[i];
            cumsum[i + 1] = cumsum[i] + segLength * v;
        }

        return (pointCandidates, writeIdx, cumsum, values);
    }
}
