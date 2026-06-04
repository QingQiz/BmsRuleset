using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using osu.Game.Rulesets.BmsRuleset.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Difficulty;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class BmsStarRatingProcessor
{
    public BmsStarRatingResult Result { get; private set; } = new();

    public double HitLeniencyX { get; private set; }

    public int TotalColumns { get; private set; }

    public double TotalTimeT { get; private set; }

    // note_tuple: (column, head_time, tail_time)
    private List<(int column, double head, double tail)> noteSeq = [];
    private List<(int column, double head, double tail)>[] noteSeqByColumn = [];
    private List<(int column, double head, double tail)> lnSeq = [];
    private List<(int column, double head, double tail)> tailSeq = [];

    private double[] baseCorners = [];
    private double[] aCorners = [];
    private double[] allCorners = [];
    private bool[][] keyUsage = [];
    private bool[][] activeColumnMask = [];
    private double[][] keyUsage400 = [];
    private double[][] deltaKs = [];
    private double[] anchor = [];

    public BmsStarRatingResult Compute(IReadOnlyList<BmsHitObject> hitObjects, int totalColumns, int rank, double clockRate = 1.0)
    {
        // === Basic Setup and Parsing ===
        TotalColumns = totalColumns;
        preprocessFile(hitObjects, rank, clockRate);
        getCorners();

        // For each column, store a boolean of its usage (whether non-empty within 150 ms) over time. Example: key_usage[k][idx].
        keyUsage = getKeyUsage();
        // At each time in base_corners, build a list of columns that are active:
        buildActiveColumns();
        keyUsage400 = getKeyUsage400();

        anchor = computeAnchor();

        var jbar = computeJbar();
        var xbar = computeXbar();

        // Build the sparse representation of cumulative LN bodies.
        var pbar = computePbar();
        var abar = computeAbar();
        var rbar = computeRbar();

        computeCAndKs(out var cArr, out var ksArr);

        jbar = interpValues(allCorners, baseCorners, jbar);
        xbar = interpValues(allCorners, baseCorners, xbar);
        pbar = interpValues(allCorners, baseCorners, pbar);
        abar = interpValues(allCorners, aCorners, abar);
        rbar = interpValues(allCorners, baseCorners, rbar);
        cArr = stepInterp(allCorners, baseCorners, cArr);
        ksArr = stepInterp(allCorners, baseCorners, ksArr);

        // === Final Computations ===
        var d = computeDifficulty(abar, jbar, xbar, pbar, rbar, cArr, ksArr);
        var sr = computeStarRating(d, cArr);

        Result = new BmsStarRatingResult
        {
            StarRating = sr,
            AllCorners = allCorners,
            BaseCorners = baseCorners,
            ACorners = aCorners,
            Jbar = jbar,
            Xbar = xbar,
            Pbar = pbar,
            Abar = abar,
            Rbar = rbar,
            DensityC = cArr,
            ActiveColumnsKs = ksArr,
            DifficultyD = d,
            AnchorValues = anchor,
        };

        return Result;
    }

    public static float RankToOd(int rank)
    {
        return rank switch
        {
            0 => 10f,
            1 => 8f,
            2 => 7f,
            3 => 6f,
            4 => 5f,
            _ => 7f,
        };
    }

    private void preprocessFile(IReadOnlyList<BmsHitObject> hitObjects, int rank, double clockRate)
    {
        var od = RankToOd(rank);

        // Hit leniency x
        var x = 0.3 * Math.Pow((64.5 - Math.Ceiling(od * 3.0)) / 500.0, 0.5);
        x = Math.Min(x, 0.6 * (x - 0.09) + 0.09);
        HitLeniencyX = x;

        // Build note_seq as a list of tuples (column, head_time, tail_time)
        noteSeq = [];
        foreach (var obj in hitObjects)
        {
            var head = Math.Floor(obj.StartTime / clockRate);
            // Only set tail_time when IsLongNote; otherwise use -1.
            var tail = obj.IsLongNote ? Math.Floor(obj.EndTime / clockRate) : -1;
            noteSeq.Add((obj.Column, head, tail));
        }

        noteSeq = noteSeq.OrderBy(n => n.head).ThenBy(n => n.column).ToList();

        // Group notes by column
        var noteDict = new Dictionary<int, List<(int column, double head, double tail)>>();
        foreach (var n in noteSeq)
        {
            if (!noteDict.TryGetValue(n.column, out var list))
                noteDict[n.column] = list = [];
            list.Add(n);
        }

        noteSeqByColumn = noteDict.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();

        // Long notes (LN) are those with a tail (t>=0)
        lnSeq = noteSeq.Where(n => n.tail >= 0).ToList();
        tailSeq = lnSeq.OrderBy(n => n.tail).ToList();

        var lnDict = new Dictionary<int, List<(int column, double head, double tail)>>();
        foreach (var n in lnSeq)
        {
            if (!lnDict.TryGetValue(n.column, out var list))
                lnDict[n.column] = list = [];
            list.Add(n);
        }

        TotalTimeT = Math.Max(
            noteSeq.Count > 0 ? noteSeq.Max(n => n.head) : 0,
            lnSeq.Count > 0 ? lnSeq.Max(n => n.tail) : 0
        ) + 1;
    }

    private void getCorners()
    {
        var cornersBase = new HashSet<double>();
        var cornersA = new HashSet<double>();

        foreach (var (_, head, tail) in noteSeq)
        {
            cornersBase.Add(head);
            cornersA.Add(head);
            if (tail >= 0)
            {
                cornersBase.Add(tail);
                cornersA.Add(tail);
            }
        }

        foreach (var s in cornersBase.ToList())
        {
            cornersBase.Add(s + 501);
            cornersBase.Add(s - 499);
            cornersBase.Add(s + 1); // To resolve the Dirac-Delta additions exactly at notes
        }

        cornersBase.Add(0);
        cornersBase.Add(TotalTimeT);

        // For Abar, unsmoothed values (KU and A) usually change at ±500 relative to note boundaries, hence ±1000 overall.
        foreach (var s in cornersA.ToList())
        {
            cornersA.Add(s + 1000);
            cornersA.Add(s - 1000);
        }

        cornersA.Add(0);
        cornersA.Add(TotalTimeT);

        // Finally, take the union of all corners for final interpolation
        baseCorners = cornersBase.Where(v => v >= 0 && v <= TotalTimeT).OrderBy(v => v).Select(v => v).ToArray();
        aCorners = cornersA.Where(v => v >= 0 && v <= TotalTimeT).OrderBy(v => v).Select(v => v).ToArray();
        allCorners = baseCorners.Union(aCorners).OrderBy(v => v).ToArray();
    }

    private bool[][] getKeyUsage()
    {
        var usage = new bool[TotalColumns][];
        for (var k = 0; k < TotalColumns; k++)
            usage[k] = new bool[baseCorners.Length];

        foreach (var (k, head, tail) in noteSeq)
        {
            var start = Math.Max(head - 150, 0);
            var end = tail < 0 ? head + 150 : Math.Min(tail + 150, TotalTimeT - 1);

            var left = searchSortedLeft(baseCorners, start);
            var right = searchSortedLeft(baseCorners, end);
            for (var i = left; i < right; i++)
                usage[k][i] = true;
        }

        return usage;
    }

    private void buildActiveColumns()
    {
        activeColumnMask = new bool[baseCorners.Length][];
        for (var i = 0; i < baseCorners.Length; i++)
        {
            var mask = new bool[TotalColumns];
            for (var k = 0; k < TotalColumns; k++)
            {
                if (keyUsage[k][i])
                    mask[k] = true;
            }

            activeColumnMask[i] = mask;
        }
    }

    private double[][] getKeyUsage400()
    {
        var usage = new double[TotalColumns][];
        for (var k = 0; k < TotalColumns; k++)
            usage[k] = new double[baseCorners.Length];

        foreach (var (k, head, tail) in noteSeq)
        {
            var start = Math.Max(head, 0);
            var end = tail < 0 ? head : Math.Min(tail, TotalTimeT - 1);

            var left400 = searchSortedLeft(baseCorners, start - 400);
            var left = searchSortedLeft(baseCorners, start);
            var right = searchSortedLeft(baseCorners, end);
            var right400 = searchSortedLeft(baseCorners, end + 400);

            var lnBonus = 3.75 + Math.Min(end - start, 1500) / 150.0;

            for (var i = left; i < right; i++)
                usage[k][i] += lnBonus;

            for (var i = left400; i < left && i >= 0 && i < baseCorners.Length; i++)
                usage[k][i] += 3.75 - 3.75 / 160000.0 * Math.Pow(baseCorners[i] - start, 2);

            for (var i = right; i < right400 && i < baseCorners.Length; i++)
                usage[k][i] += 3.75 - 3.75 / 160000.0 * Math.Pow(Math.Abs(baseCorners[i] - end), 2);
        }

        return usage;
    }

    private double[] computeAnchor()
    {
        var result = new double[baseCorners.Length];

        for (var idx = 0; idx < baseCorners.Length; idx++)
        {
            // Collect the counts for each group at this base corner
            var counts = new double[TotalColumns];
            for (var k = 0; k < TotalColumns; k++)
                counts[k] = keyUsage400[k][idx];

            Array.Sort(counts);
            Array.Reverse(counts); // e.g.  8, 5, 2, 2, 0

            var nonZeroCount = 0;
            for (var i = 0; i < TotalColumns && counts[i] != 0; i++)
                nonZeroCount++;

            if (nonZeroCount > 1)
            {
                double walk = 0;
                double maxWalk = 0;
                for (var i = 0; i < nonZeroCount - 1; i++)
                {
                    var ratio = counts[i + 1] / counts[i];
                    walk += counts[i] * (1 - 4 * Math.Pow(0.5 - ratio, 2));
                    maxWalk += counts[i];
                }

                result[idx] = walk / maxWalk;
            }
        }

        for (var i = 0; i < result.Length; i++)
            result[i] = 1 + Math.Min(result[i] - 0.18, 5 * Math.Pow(result[i] - 0.22, 3));

        return result;
    }

    private double[] computeJbar()
    {
        var jks = new double[TotalColumns][];
        var dks = new double[TotalColumns][];

        for (var k = 0; k < TotalColumns; k++)
        {
            jks[k] = new double[baseCorners.Length];
            dks[k] = new double[baseCorners.Length];
            Array.Fill(dks[k], 1e9);
        }

        double jackNerfer(double delta) => 1 - 7e-5 * Math.Pow(0.15 + Math.Abs(delta - 0.08), -4);

        for (var k = 0; k < TotalColumns; k++)
        {
            var notes = noteSeqByColumn[k];
            for (var i = 0; i < notes.Count - 1; i++)
            {
                var start = notes[i].head;
                var end = notes[i + 1].head;

                // Find indices in base_corners that lie in [start, end)
                var left = searchSortedLeft(baseCorners, start);
                var right = searchSortedLeft(baseCorners, end);
                if (left >= right) continue;

                var delta = 0.001 * (end - start);
                var val = 1.0 / delta / (delta + 0.11 * Math.Pow(HitLeniencyX, 0.25));
                var jVal = val * jackNerfer(delta);

                for (var j = left; j < right; j++)
                {
                    jks[k][j] = jVal;
                    dks[k][j] = delta;
                }
            }
        }

        // Now smooth each column's J_ks
        var jbarKs = new double[TotalColumns][];
        for (var k = 0; k < TotalColumns; k++)
            jbarKs[k] = smoothOnCorners(baseCorners, jks[k], 500, 0.001, false);

        // Aggregate across columns using weighted average
        var jbar = new double[baseCorners.Length];
        for (var i = 0; i < baseCorners.Length; i++)
        {
            double num = 0, den = 0;
            for (var k = 0; k < TotalColumns; k++)
            {
                var v = jbarKs[k][i];
                if (v < 0) v = 0;
                var w = 1.0 / dks[k][i];
                num += v * v * v * v * v * w;
                den += w;
            }

            jbar[i] = Math.Pow(num / Math.Max(1e-9, den), 1.0 / 5.0);
        }

        deltaKs = dks;
        return jbar;
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

            coeffs[0] = coeffs[k] = outer;
        }
        else
        {
            coeffs[m] = coeffs[m + 1] = outer;
            for (var i = 1; m - i > 0; i++)
            {
                coeffs[m - i] = coeffs[m + 1 + i] = 0.15 + 0.10 * i;
            }

            coeffs[0] = coeffs[k] = outer;
        }

        return coeffs;
    }

    private static List<(int column, double head, double tail)> mergeSorted(
        List<(int column, double head, double tail)> a,
        List<(int column, double head, double tail)> b)
    {
        var result = new List<(int column, double head, double tail)>(a.Count + b.Count);
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            result.Add(a[i].head <= b[j].head ? a[i++] : b[j++]);
        }

        while (i < a.Count) result.Add(a[i++]);
        while (j < b.Count) result.Add(b[j++]);
        return result;
    }

    private double[] computeXbar()
    {
        var crossCoeff = generateCrossCoeffs(TotalColumns);

        var xks = new double[TotalColumns + 1][];
        var fastCross = new double[TotalColumns + 1][];

        for (var k = 0; k <= TotalColumns; k++)
        {
            xks[k] = new double[baseCorners.Length];
            fastCross[k] = new double[baseCorners.Length];
        }

        for (var k = 0; k <= TotalColumns; k++)
        {
            List<(int column, double head, double tail)> notesInPair;

            if (k == 0)
                notesInPair = noteSeqByColumn[0];
            else if (k == TotalColumns)
                notesInPair = noteSeqByColumn[TotalColumns - 1];
            else
                notesInPair = mergeSorted(noteSeqByColumn[k - 1], noteSeqByColumn[k]);

            for (var i = 1; i < notesInPair.Count; i++)
            {
                var start = notesInPair[i - 1].head;
                var end = notesInPair[i].head;

                var left = searchSortedLeft(baseCorners, start);
                var right = searchSortedLeft(baseCorners, end);
                if (left >= right) continue;

                var delta = 0.001 * (end - start);
                var val = 0.16 / (Math.Max(HitLeniencyX, delta) * Math.Max(HitLeniencyX, delta));

                // Python: ((k-1) not in active_columns[idx_start] and (k-1) not in active_columns[idx_end])
                //         or (k not in active_columns[idx_start] and k not in active_columns[idx_end])
                bool col1AbsentAtBoth;
                bool col2AbsentAtBoth;

                var maskLeft = activeColumnMask[left];
                var maskRight = activeColumnMask[right];

                if (k - 1 >= 0)
                    col1AbsentAtBoth = !maskLeft[k - 1] && !maskRight[k - 1];
                else
                    col1AbsentAtBoth = true;

                if (k < TotalColumns)
                    col2AbsentAtBoth = !maskLeft[k] && !maskRight[k];
                else
                    col2AbsentAtBoth = true;

                if (col1AbsentAtBoth || col2AbsentAtBoth)
                    val *= 1 - crossCoeff[k];

                var fastVal = Math.Max(0, 0.4 / (Math.Max(delta, Math.Max(0.06, 0.75 * HitLeniencyX))
                                                 * Math.Max(delta, Math.Max(0.06, 0.75 * HitLeniencyX))) - 80);

                for (var j = left; j < right; j++)
                {
                    xks[k][j] = val;
                    fastCross[k][j] = fastVal;
                }
            }
        }

        var xBase = new double[baseCorners.Length];
        for (var i = 0; i < baseCorners.Length; i++)
        {
            double sum = 0;
            for (var k = 0; k <= TotalColumns; k++)
                sum += xks[k][i] * crossCoeff[k];

            double sqrtSum = 0;
            for (var k = 0; k < TotalColumns; k++)
            {
                sqrtSum += Math.Sqrt(fastCross[k][i] * crossCoeff[k] * fastCross[k + 1][i] * crossCoeff[k + 1]);
            }

            xBase[i] = sum + sqrtSum;
        }

        return smoothOnCorners(baseCorners, xBase, 500, 0.001, false);
    }

    private double[] computePbar()
    {
        double streamBooster(double delta)
        {
            var r = 7.5 / delta;
            if (r > 160 && r < 360)
                return 1 + 1.7e-7 * (r - 160) * (r - 360) * (r - 360);

            return 1;
        }

        var (lnPoints, lnCumsum, lnValues) = lnBodiesCountSparseRepresentation();

        var pStep = new double[baseCorners.Length];

        for (var i = 0; i < noteSeq.Count - 1; i++)
        {
            var hl = noteSeq[i].head;
            var hr = noteSeq[i + 1].head;
            var deltaTime = hr - hl;

            if (deltaTime < 1e-9)
            {
                // Dirac delta case: when notes occur at the same time.
                // Add the spike exactly at the note head in the base grid.
                var spike = 1000 * Math.Pow(0.02 * (4.0 / HitLeniencyX - 24), 0.25);
                var left = searchSortedLeft(baseCorners, hl);
                var right = searchSortedRight(baseCorners, hl);
                for (var j = left; j < right; j++)
                    pStep[j] += spike;
                // Continue so that we add a spike for each additional simultaneous note.
                continue;
            }

            // For the regular case where delta_time > 0, identify the base grid indices in [h_l, h_r)
            var lIdx = searchSortedLeft(baseCorners, hl);
            var rIdx = searchSortedLeft(baseCorners, hr);
            if (lIdx >= rIdx) continue;

            var delta = 0.001 * deltaTime;
            var v = 1 + 6 * 0.001 * lnSum(hl, hr, lnPoints, lnCumsum, lnValues);
            var bVal = streamBooster(delta);

            double inc;
            var xf2 = 0.08 / HitLeniencyX;
            if (delta < 2 * HitLeniencyX / 3)
            {
                var dShift = delta - HitLeniencyX / 2;
                inc = 1.0 / delta * Math.Pow(xf2 * (1 - 24.0 / HitLeniencyX * dShift * dShift), 0.25) * Math.Max(bVal, v);
            }
            else
            {
                var x6 = HitLeniencyX / 6;
                inc = 1.0 / delta * Math.Pow(xf2 * (1 - 24.0 / HitLeniencyX * x6 * x6), 0.25) * Math.Max(bVal, v);
            }

            for (var j = lIdx; j < rIdx; j++)
                pStep[j] += Math.Min(inc * anchor[j], Math.Max(inc, inc * 2 - 10));
        }

        return smoothOnCorners(baseCorners, pStep, 500, 0.001, false);
    }

    private double[] computeAbar()
    {
        var dks = new double[TotalColumns][];
        for (var k = 0; k < TotalColumns; k++)
            dks[k] = new double[baseCorners.Length];

        for (var i = 0; i < baseCorners.Length; i++)
        {
            var mask = activeColumnMask[i];
            var prevActive = -1;
            for (var k = 0; k < TotalColumns; k++)
            {
                if (!mask[k]) continue;

                if (prevActive >= 0)
                {
                    // Use the delta_ks computed before on base_corners
                    dks[prevActive][i] = Math.Abs(deltaKs[prevActive][i] - deltaKs[k][i])
                                         + 0.4 * Math.Max(0, Math.Max(deltaKs[prevActive][i], deltaKs[k][i]) - 0.11);
                }

                prevActive = k;
            }
        }

        var aStep = new double[aCorners.Length];
        Array.Fill(aStep, 1.0);

        for (var i = 0; i < aCorners.Length; i++)
        {
            var idx = searchSortedLeft(baseCorners, aCorners[i]);
            if (idx >= baseCorners.Length) idx = baseCorners.Length - 1;

            var mask = activeColumnMask[idx];
            var prevActive = -1;
            for (var k = 0; k < TotalColumns; k++)
            {
                if (!mask[k]) continue;

                if (prevActive >= 0)
                {
                    var dVal = dks[prevActive][idx];

                    if (dVal < 0.02)
                        aStep[i] *= Math.Min(0.75 + 0.5 * Math.Max(deltaKs[prevActive][idx], deltaKs[k][idx]), 1);
                    else if (dVal < 0.07)
                        aStep[i] *= Math.Min(0.65 + 5 * dVal + 0.5 * Math.Max(deltaKs[prevActive][idx], deltaKs[k][idx]), 1);
                    // Otherwise leave A_step[i] unchanged.
                }

                prevActive = k;
            }
        }

        return smoothOnCorners(aCorners, aStep, 250, 1.0, true);
    }

    private double[] computeRbar()
    {
        var rStep = new double[baseCorners.Length];

        var timesByColumn = new Dictionary<int, double[]>();
        for (var k = 0; k < noteSeqByColumn.Length; k++)
            timesByColumn[k] = noteSeqByColumn[k].Select(n => n.head).ToArray();

        // Release Index
        var iList = new double[tailSeq.Count];
        for (var i = 0; i < tailSeq.Count; i++)
        {
            var (k, hi, ti) = tailSeq[i];
            var colTimes = timesByColumn[k];
            var idx = searchSortedLeft(colTimes, hi);
            var hNext = idx + 1 < colTimes.Length ? colTimes[idx + 1] : 1e9;

            var iH = 0.001 * Math.Abs(ti - hi - 80) / HitLeniencyX;
            var iT = 0.001 * Math.Abs(hNext - ti - 80) / HitLeniencyX;
            iList[i] = 2.0 / (2 + Math.Exp(-5 * (iH - 0.75)) + Math.Exp(-5 * (iT - 0.75)));
        }

        // For each interval between successive tail times, assign I and R.
        for (var i = 0; i < tailSeq.Count - 1; i++)
        {
            var tStart = tailSeq[i].tail;
            var tEnd = tailSeq[i + 1].tail;

            var left = searchSortedLeft(baseCorners, tStart);
            var right = searchSortedLeft(baseCorners, tEnd);
            if (left >= right) continue;

            var deltaR = 0.001 * (tEnd - tStart);
            var rVal = 0.08 * Math.Pow(deltaR, -0.5) / HitLeniencyX * (1 + 0.8 * (iList[i] + iList[i + 1]));

            for (var j = left; j < right; j++)
                rStep[j] = rVal;
        }

        return smoothOnCorners(baseCorners, rStep, 500, 0.001, false);
    }

    private void computeCAndKs(out double[] cArr, out double[] ksArr)
    {
        // C(s): count of notes within 500 ms
        var noteHitTimes = noteSeq.Select(n => n.head).OrderBy(t => t).ToArray();

        cArr = new double[baseCorners.Length];
        for (var i = 0; i < baseCorners.Length; i++)
        {
            var low = baseCorners[i] - 500;
            var high = baseCorners[i] + 500;
            // Use binary search on note_hit_times:
            var cnt = searchSortedLeft(noteHitTimes, high) - searchSortedLeft(noteHitTimes, low);
            cArr[i] = cnt;
        }

        // Ks: local key usage count (minimum 1)
        ksArr = new double[baseCorners.Length];
        for (var i = 0; i < baseCorners.Length; i++)
        {
            var count = 0;
            var mask = activeColumnMask[i];
            for (var k = 0; k < TotalColumns; k++)
            {
                if (mask[k]) count++;
            }

            ksArr[i] = Math.Max(count, 1);
        }
    }

    private double[] computeDifficulty(double[] abar, double[] jbar, double[] xbar, double[] pbar, double[] rbar, double[] cArr, double[] ksArr)
    {
        // Compute Difficulty D on all_corners:
        var d = new double[allCorners.Length];

        for (var i = 0; i < allCorners.Length; i++)
        {
            var ks = ksArr[i];
            var a = abar[i];
            var j = jbar[i];
            var jCap = Math.Min(j, 8 + 0.85 * j);
            var a3Ks = Math.Pow(a, 3.0 / ks);

            var s1 = Math.Pow(a3Ks * jCap, 1.5);
            var streamTerm = 0.8 * pbar[i] + rbar[i] * 35.0 / (cArr[i] + 8);
            var s2 = Math.Pow(Math.Pow(a, 2.0 / 3.0) * streamTerm, 1.5);

            var s = Math.Pow(0.4 * s1 + 0.6 * s2, 2.0 / 3.0);
            var t = a3Ks * xbar[i] / (xbar[i] + s + 1);
            d[i] = 2.7 * Math.Sqrt(s) * Math.Pow(t, 1.5) + s * 0.27;
        }

        return d;
    }

    private double computeStarRating(double[] d, double[] cArr)
    {
        var n = allCorners.Length;

        // Compute the gaps between consecutive times in a vectorised way.
        // For interior points, the effective gap is the average of the left and right gap.
        var gaps = new double[n];
        gaps[0] = (allCorners[1] - allCorners[0]) / 2.0;
        gaps[n - 1] = (allCorners[n - 1] - allCorners[n - 2]) / 2.0;
        for (var i = 1; i < n - 1; i++)
            gaps[i] = (allCorners[i + 1] - allCorners[i - 1]) / 2.0;

        // The effective weight for each corner is the product of its density and its gap.
        var effectiveWeights = new double[n];
        for (var i = 0; i < n; i++)
            effectiveWeights[i] = cArr[i] * gaps[i];

        // Sort indices by D value
        var indices = Enumerable.Range(0, n).ToArray();
        Array.Sort(indices, (a, b) => d[a].CompareTo(d[b]));

        // Build sorted D and weight arrays
        var dSorted = new double[n];
        var wSorted = new double[n];
        for (var i = 0; i < n; i++)
        {
            dSorted[i] = d[indices[i]];
            wSorted[i] = effectiveWeights[indices[i]];
        }

        // Compute the cumulative sum of the effective weights.
        var cumWeights = new double[n];
        cumWeights[0] = wSorted[0];
        for (var i = 1; i < n; i++)
            cumWeights[i] = cumWeights[i - 1] + wSorted[i];

        var totalWeight = cumWeights[n - 1];
        double[] targetPercentiles = [0.945, 0.935, 0.925, 0.915, 0.845, 0.835, 0.825, 0.815];

        double p93Sum = 0, p83Sum = 0;
        for (var i = 0; i < targetPercentiles.Length; i++)
        {
            var target = targetPercentiles[i] * totalWeight;
            var idx = searchSortedLeft(cumWeights, target);
            if (idx >= n) idx = n - 1;
            if (i < 4)
                p93Sum += dSorted[idx];
            else
                p83Sum += dSorted[idx];
        }

        var p93 = p93Sum / 4.0;
        var p83 = p83Sum / 4.0;

        double weightedSum5 = 0, wSum = 0;
        for (var i = 0; i < n; i++)
        {
            var dVal = dSorted[i];
            weightedSum5 += dVal * dVal * dVal * dVal * dVal * wSorted[i];
            wSum += wSorted[i];
        }

        var weightedMean = Math.Pow(weightedSum5 / wSum, 1.0 / 5.0);

        // Final SR calculation
        var sr = 0.88 * p93 * 0.25 + 0.94 * p83 * 0.2 + weightedMean * 0.55;
        sr = sr / 8.0 * 8.0;

        var totalNotes = noteSeq.Count + 0.5 * lnSeq.Sum(x => Math.Min(x.tail - x.head, 1000) / 200.0);
        sr *= totalNotes / (totalNotes + 60);

        sr = rescaleHigh(sr);
        sr *= 0.975;

        Result.Percentile93 = p93;
        Result.Percentile83 = p83;
        Result.WeightedMean = weightedMean;

        return sr;
    }

    private static double rescaleHigh(double sr)
    {
        if (sr <= 9) return sr;

        return 9 + (sr - 9) * (1.0 / 1.2);
    }

    // -----Start of Helper methods--------

    /// <summary>
    /// Given sorted positions x (length N) and function values f defined piecewise constant on [x[i], x[i+1]),
    /// return an array F of cumulative integrals such that F[0]=0 and for i&gt;=1:
    ///   F[i] = sum_{j=0}^{i-1} f[j]*(x[j+1]-x[j])
    /// </summary>
    private static double[] cumulativeSum(double[] x, double[] f)
    {
        var F = new double[x.Length];
        for (var i = 1; i < x.Length; i++)
            F[i] = F[i - 1] + f[i - 1] * (x[i] - x[i - 1]);
        return F;
    }

    /// <summary>
    /// Given cumulative data (x, F, f) as above, return the cumulative sum at an arbitrary point q.
    /// Here we assume that f is constant on each interval.
    /// </summary>
    private static double queryCumSum(double q, double[] x, double[] F, double[] f)
    {
        if (q <= x[0]) return 0;
        if (q >= x[^1]) return F[^1];

        // Find index i such that x[i] <= q < x[i+1]
        var i = searchSortedLeft(x, q) - 1;
        if (i < 0) i = 0;
        return F[i] + f[i] * (q - x[i]);
    }

    /// <summary>
    /// Given positions x (a sorted 1D array) and function values f (piecewise constant on intervals defined by x),
    /// return an array g defined at x by applying a symmetric sliding window:
    ///   if mode=='sum': g(s) = scale * ∫[s-window, s+window] f(t) dt
    ///   if mode=='avg': g(s) = (∫[s-window, s+window] f(t) dt) / (length of window actually used)
    /// This is computed exactly using the cumulative–sum technique.
    /// </summary>
    private static double[] smoothOnCorners(double[] x, double[] f, double window, double scale, bool averageMode)
    {
        var F = cumulativeSum(x, f);
        var g = new double[f.Length];

        for (var i = 0; i < x.Length; i++)
        {
            var s = x[i];
            var a = Math.Max(s - window, x[0]);
            var b = Math.Min(s + window, x[^1]);
            var val = queryCumSum(b, x, F, f) - queryCumSum(a, x, F, f);

            if (averageMode)
                g[i] = b - a > 0 ? val / (b - a) : 0;
            else
                g[i] = scale * val;
        }

        return g;
    }

    /// <summary>Return new_vals at positions new_x using linear interpolation from old_x, old_vals.</summary>
    private static double[] interpValues(double[] newX, double[] oldX, double[] oldVals)
    {
        var result = new double[newX.Length];
        for (var i = 0; i < newX.Length; i++)
        {
            var x = newX[i];
            if (x <= oldX[0])
            {
                result[i] = oldVals[0];
            }
            else if (x >= oldX[^1])
            {
                result[i] = oldVals[^1];
            }
            else
            {
                var idx = searchSortedLeft(oldX, x);
                var t = (x - oldX[idx - 1]) / (oldX[idx] - oldX[idx - 1]);
                result[i] = oldVals[idx - 1] + t * (oldVals[idx] - oldVals[idx - 1]);
            }
        }

        return result;
    }

    /// <summary>
    /// For each position in new_x, return the value of old_vals corresponding to the greatest old_x
    /// that is less than or equal to new_x. This implements a step–function (zero–order hold)
    /// interpolation.
    /// </summary>
    private static double[] stepInterp(double[] newX, double[] oldX, double[] oldVals)
    {
        var result = new double[newX.Length];
        for (var i = 0; i < newX.Length; i++)
        {
            var idx = searchSortedRight(oldX, newX[i]) - 1;
            if (idx < 0) idx = 0;
            if (idx >= oldVals.Length) idx = oldVals.Length - 1;
            result[i] = oldVals[idx];
        }

        return result;
    }

    // -----End of Helper methods--------

    // ---- LN body sparse representation ----
    // dictionary: index -> change in LN_bodies (before transformation)
    private (double[] points, double[] cumsum, double[] values) lnBodiesCountSparseRepresentation()
    {
        var diff = new Dictionary<double, double>();

        foreach (var (_, head, tail) in lnSeq)
        {
            var t0 = Math.Min(head + 60, tail);
            var t1 = Math.Min(head + 120, tail);
            diff[t0] = diff.GetValueOrDefault(t0, 0) + 1.3;
            diff[t1] = diff.GetValueOrDefault(t1, 0) + (-1.3 + 1); // net change at t1: -1.3 from first part, then +1
            diff[tail] = diff.GetValueOrDefault(tail, 0) - 1;
        }

        // The breakpoints are the times where changes occur.
        var pointsSet = new HashSet<double> { 0, TotalTimeT };
        foreach (var p in diff.Keys) pointsSet.Add(p);
        var points = pointsSet.OrderBy(p => p).ToArray();

        // Build piecewise constant values (after transformation) and a cumulative sum.
        var values = new List<double>();
        var cumsum = new List<double> { 0 }; // cumulative sum at the breakpoints
        double curr = 0;

        for (var i = 0; i < points.Length - 1; i++)
        {
            var t = points[i];
            // If there is a change at t, update the running value.
            if (diff.TryGetValue(t, out var change))
                curr += change;

            var v = Math.Min(curr, 2.5 + 0.5 * curr);
            values.Add(v);
            // Compute cumulative sum on the interval [points[i], points[i+1])
            var segLength = points[i + 1] - points[i];
            cumsum.Add(cumsum[^1] + segLength * v);
        }

        return (points, [.. cumsum], [.. values]);
    }

    private static double lnSum(double a, double b, double[] points, double[] cumsum, double[] values)
    {
        // Locate the segments that contain a and b using bisect_right semantics.
        var i = searchSortedRight(points, a) - 1;
        var j = searchSortedRight(points, b) - 1;

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

    private static int searchSortedLeft(double[] array, double value)
    {
        int lo = 0, hi = array.Length;
        while (lo < hi)
        {
            var mid = lo + hi >> 1;
            if (array[mid] < value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private static int searchSortedRight(double[] array, double value)
    {
        int lo = 0, hi = array.Length;
        while (lo < hi)
        {
            var mid = lo + hi >> 1;
            if (array[mid] <= value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}
