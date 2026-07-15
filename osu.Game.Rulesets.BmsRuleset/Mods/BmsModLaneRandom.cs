using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModLaneRandom : Mod, IApplicableAfterBeatmapConversion, IHasSeed
{
    public override string Name => "Lane Random";

    public override string Acronym => "LR";

    public override LocalisableString Description => BmsStrings.ModLaneRandom;

    public override ModType Type => ModType.Conversion;

    public override IconUsage? Icon => OsuIcon.ModRandom;

    public override Type[] IncompatibleMods => [typeof(BmsModNoteRandom), typeof(BmsModMirror)];

    [SettingSource("Include Scratch", "Whether to include scratch lanes in the shuffle.")]
    public Bindable<bool> IncludeScratch { get; } = new();

    [SettingSource("Seed", "Use a custom seed for deterministic randomisation.", SettingControlType = typeof(SettingsNumberBox))]
    public Bindable<int?> Seed { get; } = new();

    [SettingSource("Lane Order", "Comma-separated direct lane mapping (e.g. \"0,3,1,4,2,5\").")]
    public Bindable<string> LaneOrder { get; } = new(string.Empty);


    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap bmsBeatmap)
            return;

        var totalColumns = bmsBeatmap.TotalColumns;
        var variant = bmsBeatmap.LayoutVariant;

        // Lane Order bypasses IncludeScratch — it's a direct explicit mapping for all columns.
        if (!string.IsNullOrWhiteSpace(LaneOrder.Value))
        {
            var mapping = parseLaneOrder(LaneOrder.Value, totalColumns);
            foreach (var hitObject in bmsBeatmap.HitObjects)
                hitObject.Column = mapping[hitObject.Column];
            return;
        }

        // Columns that participate in the random shuffle (all or non-scratch).
        var shuffleCols = Enumerable.Range(0, totalColumns)
            .Where(c => IncludeScratch.Value || !BmsLayout.IsScratchColumn(c, variant))
            .ToArray();

        Seed.Value ??= RNG.Next();
        var subMapping = Enumerable.Range(0, shuffleCols.Length).ToArray();
        shuffle(new Random((int)Seed.Value), subMapping);

        foreach (var hitObject in bmsBeatmap.HitObjects)
        {
            var srcIdx = Array.IndexOf(shuffleCols, hitObject.Column);
            if (srcIdx >= 0)
                hitObject.Column = shuffleCols[subMapping[srcIdx]];
        }
    }

    /// <summary>
    ///     Parses a comma-separated lane order string into an array mapping each source column
    ///     to a target column. Duplicate targets are allowed (e.g. <c>"1,1,1,1,2,2,2,2"</c>)
    ///     to collapse multiple input lanes onto the same output lane.
    /// </summary>
    private static int[] parseLaneOrder(string order, int count)
    {
        var parts = order.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new int[count];

        for (var i = 0; i < count; i++)
            result[i] = i < parts.Length && int.TryParse(parts[i], out var v) ? Math.Clamp(v, 0, count - 1) : i;

        return result;
    }

    private static void shuffle(Random rng, int[] array)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
