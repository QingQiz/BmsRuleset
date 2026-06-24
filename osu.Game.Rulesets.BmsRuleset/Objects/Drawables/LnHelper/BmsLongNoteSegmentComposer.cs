using System;
using System.Collections.Generic;

namespace osu.Game.Rulesets.BmsRuleset.Objects.Drawables.LnHelper;

/// <summary>
/// Converts source long-note body slices into drawable parts for a target body length.
/// </summary>
/// <remarks>
/// This class contains only the spatial segmentation rules. It does not know about textures,
/// skin lookup, animation frames, or drawable pooling. Keeping it pure makes the fragile BMS
/// body[0] rules testable without loading osu! framework graphics resources.
/// </remarks>
public static class BmsLongNoteSegmentComposer
{
    /// <summary>
    /// Describes a single source slice instance that should be drawn inside the masked body container.
    /// </summary>
    /// <param name="SegmentIndex">Index in the source slice array.</param>
    /// <param name="Y">Top-left Y position in body-container coordinates.</param>
    /// <param name="Height">Natural draw height. The caller should mask overflow rather than rescale.</param>
    /// <param name="FlipY">
    /// Whether the texture should be flipped vertically. When the tail is below the head, every
    /// spatial slice is flipped so the whole body behaves like one vertically mirrored texture strip.
    /// </param>
    public readonly record struct Part(int SegmentIndex, float Y, float Height, bool FlipY);

    /// <summary>
    /// Compose drawable body parts.
    /// </summary>
    /// <param name="naturalHeights">Natural rendered heights for each source slice.</param>
    /// <param name="targetHeight">Masked body container height.</param>
    /// <param name="tailAtTop">Whether the LN tail is above the LN head in drawable coordinates.</param>
    public static IReadOnlyList<Part> Compose(IReadOnlyList<float> naturalHeights, float targetHeight, bool tailAtTop)
    {
        var parts = new List<(int SegmentIndex, float Height)>();
        composeParts(naturalHeights, targetHeight, parts);
        return positionParts(parts, targetHeight, tailAtTop);
    }

    public static IReadOnlyList<Part> ComposeInto(IReadOnlyList<float> naturalHeights, float targetHeight, bool tailAtTop,
                                                  List<(int SegmentIndex, float Height)> reusableParts, List<Part> reusableResult)
    {
        reusableParts.Clear();
        composeParts(naturalHeights, targetHeight, reusableParts);
        positionPartsInto(reusableParts, targetHeight, tailAtTop, reusableResult);
        return reusableResult;
    }

    private static void composeParts(IReadOnlyList<float> naturalHeights, float targetHeight, List<(int SegmentIndex, float Height)> parts)
    {
        if (naturalHeights.Count == 0 || targetHeight <= 0)
            return;

        if (naturalHeights.Count == 1)
            addRepeatedSingleSlice(parts, naturalHeights[0], targetHeight);
        else
            addMultiSliceBody(parts, naturalHeights, targetHeight);
    }

    private static void addRepeatedSingleSlice(List<(int SegmentIndex, float Height)> parts, float sourceHeight, float targetHeight)
    {
        var height = Math.Max(1, sourceHeight);
        var covered = 0f;

        while (covered < targetHeight)
        {
            parts.Add((0, height));
            covered += height;
        }
    }

    private static void addMultiSliceBody(List<(int SegmentIndex, float Height)> parts, IReadOnlyList<float> naturalHeights, float targetHeight)
    {
        var tailHeight = Math.Max(1, naturalHeights[0]);
        var covered = tailHeight;

        parts.Add((0, tailHeight));

        for (var i = 1; i < naturalHeights.Count && covered < targetHeight; i++)
        {
            var height = Math.Max(1, naturalHeights[i]);
            parts.Add((i, height));
            covered += height;
        }

        while (covered < targetHeight)
        {
            var before = covered;

            for (var i = 1; i < naturalHeights.Count && covered < targetHeight; i++)
            {
                var height = Math.Max(1, naturalHeights[i]);
                parts.Add((i, height));
                covered += height;
            }

            if (covered <= before)
                break;
        }
    }

    private static IReadOnlyList<Part> positionParts(List<(int SegmentIndex, float Height)> parts, float targetHeight, bool tailAtTop)
    {
        var result = new List<Part>(parts.Count);
        var prefix = 0f;

        foreach (var part in parts)
        {
            var y = tailAtTop ? prefix : targetHeight - prefix - part.Height;
            var flipY = !tailAtTop;
            result.Add(new Part(part.SegmentIndex, flipY ? y + part.Height : y, part.Height, flipY));
            prefix += part.Height;
        }

        return result;
    }

    private static void positionPartsInto(List<(int SegmentIndex, float Height)> parts, float targetHeight, bool tailAtTop, List<Part> result)
    {
        result.Clear();
        var prefix = 0f;

        foreach (var part in parts)
        {
            var y = tailAtTop ? prefix : targetHeight - prefix - part.Height;
            var flipY = !tailAtTop;
            result.Add(new Part(part.SegmentIndex, flipY ? y + part.Height : y, part.Height, flipY));
            prefix += part.Height;
        }
    }
}
