using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace osu.Game.Rulesets.BmsRuleset.BmsParser;

internal static partial class BmsChartParser
{
    private static List<RawCell> sortCells(List<RawCell> cells)
    {
        var span = CollectionsMarshal.AsSpan(cells);
        var ordered = true;
        for (var i = 1; i < span.Length; i++)
        {
            if (compareCells(span[i - 1], span[i]) <= 0)
                continue;

            ordered = false;
            break;
        }

        if (ordered)
            return cells;

        // Source sequences can collide on unusually long channel lines. Stable merging preserves
        // the old OrderBy/ThenBy tie order without allocating its keys, indices and cell copies.
        var scratch = ArrayPool<RawCell>.Shared.Rent(span.Length / 2);
        try
        {
            stableSortCells(span, scratch);
        }
        finally
        {
            ArrayPool<RawCell>.Shared.Return(scratch);
        }

        return cells;
    }

    private static void stableSortCells(Span<RawCell> cells, Span<RawCell> scratch)
    {
        if (cells.Length <= 16)
        {
            for (var i = 1; i < cells.Length; i++)
            {
                var value = cells[i];
                var j = i;
                while (j > 0 && compareCells(cells[j - 1], value) > 0)
                {
                    cells[j] = cells[j - 1];
                    j--;
                }

                cells[j] = value;
            }

            return;
        }

        var middle = cells.Length / 2;
        stableSortCells(cells[..middle], scratch);
        stableSortCells(cells[middle..], scratch);
        if (compareCells(cells[middle - 1], cells[middle]) <= 0)
            return;

        cells[..middle].CopyTo(scratch);
        var left = 0;
        var right = middle;
        var output = 0;
        while (left < middle && right < cells.Length)
            cells[output++] = compareCells(scratch[left], cells[right]) <= 0 ? scratch[left++] : cells[right++];
        scratch[left..middle].CopyTo(cells[output..]);
    }

    private static int compareCells(RawCell a, RawCell b)
    {
        var comparison = a.Tick.CompareTo(b.Tick);
        return comparison != 0 ? comparison : a.Sequence.CompareTo(b.Sequence);
    }
}
