using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public class BmsModRotationRandom : Mod, IApplicableAfterBeatmapConversion, IHasSeed
{
    public override string Name => "Rotation Random";

    public override string Acronym => "RR";

    public override LocalisableString Description => "R-RANDOM: randomly rotates columns, with 50% chance of also mirroring.";

    public override ModType Type => ModType.Conversion;

    public override IconUsage? Icon => OsuIcon.ModRandom;

    public override Type[] IncompatibleMods => [typeof(BmsModLaneRandom), typeof(BmsModNoteRandom), typeof(BmsModMirror)];

    [SettingSource("Include Scratch", "Whether to include scratch lanes in the rotation.")]
    public Bindable<bool> IncludeScratch { get; } = new();

    [SettingSource("Seed", "Use a custom seed for deterministic randomisation.", SettingControlType = typeof(SettingsNumberBox))]
    public Bindable<int?> Seed { get; } = new();

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap bmsBeatmap)
            return;

        Seed.Value ??= RNG.Next();
        var rng = new Random((int)Seed.Value);

        var totalColumns = bmsBeatmap.TotalColumns;
        var variant = bmsBeatmap.LayoutVariant;

        // Columns that participate.
        var shuffleCols = Enumerable.Range(0, totalColumns)
            .Where(c => IncludeScratch.Value || !BmsLayout.IsScratchColumn(c, variant))
            .ToArray();

        var count = shuffleCols.Length;

        if (count <= 1)
            return;

        // Step 1: choose base — identity or mirror.
        var baseArr = new int[count];
        if (rng.Next(2) == 0)
        {
            for (var i = 0; i < count; i++)
                baseArr[i] = shuffleCols[i];
        }
        else
        {
            for (var i = 0; i < count; i++)
                baseArr[i] = shuffleCols[count - 1 - i];
        }

        // Step 2: rotate by a random offset (1 to count-1, never 0).
        var offset = rng.Next(1, count);
        var mapping = new int[count];
        for (var i = 0; i < count; i++)
            mapping[i] = baseArr[(i - offset + count) % count];

        foreach (var hitObject in bmsBeatmap.HitObjects)
        {
            var srcIdx = Array.IndexOf(shuffleCols, hitObject.Column);
            if (srcIdx >= 0)
                hitObject.Column = mapping[srcIdx];
        }
    }
}
