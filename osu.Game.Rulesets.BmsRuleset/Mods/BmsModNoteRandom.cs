using System;
using System.Collections.Generic;
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
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Mods;

public enum BmsNoteRandomMode
{
    // ReSharper disable once UnusedMember.Global
    S_Random,
    H_Random,
}

public class BmsModNoteRandom : Mod, IApplicableAfterBeatmapConversion, IHasSeed
{
    public override string Name => "Note Random";

    public override string Acronym => "NR";

    public override LocalisableString Description => "Per-note randomisation. \nS-RANDOM shuffles every note with a 40ms anti-jack window. \nH-RANDOM widens it to 100ms, aggressively breaking jacks at the cost of more chaotic patterns.";

    public override ModType Type => ModType.Conversion;

    public override IconUsage? Icon => OsuIcon.ModRandom;

    public override Type[] IncompatibleMods => [typeof(BmsModLaneRandom), typeof(BmsModMirror)];

    [SettingSource("Include Scratch", "Whether to include scratch lanes in the shuffle.")]
    public Bindable<bool> IncludeScratch { get; } = new();

    [SettingSource("Mode", "S-RANDOM: 40ms anti-jack — only prevents impossibly close jacks. H-RANDOM: 100ms anti-jack — actively breaks jacks into stair/trill patterns.")]
    public Bindable<BmsNoteRandomMode> Mode { get; } = new();

    [SettingSource("Seed", "Use a custom seed for deterministic randomisation (required for replay consistency).", SettingControlType = typeof(SettingsNumberBox))]
    public Bindable<int?> Seed { get; } = new();

    private const int s_random_threshold = 40;
    private const int h_random_threshold = 100;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is not BmsBeatmap bmsBeatmap)
            return;

        Seed.Value ??= RNG.Next();
        var rng = new Random((int)Seed.Value);

        var totalColumns = bmsBeatmap.TotalColumns;
        var variant = bmsBeatmap.LayoutVariant;
        var threshold = Mode.Value == BmsNoteRandomMode.H_Random ? h_random_threshold : s_random_threshold;

        // Available columns for shuffling.
        var shuffleColumns = Enumerable.Range(0, totalColumns)
            .Where(c => IncludeScratch.Value || !BmsLayout.IsScratchColumn(c, variant))
            .ToArray();

        // Track last note time per column (initialised so the first note always hits safe).
        var lastNoteTime = new Dictionary<int, double>();
        foreach (var c in shuffleColumns)
            lastNoteTime[c] = double.MinValue;

        // Track columns occupied by active long notes: column → endTime.
        var activeLnColumns = new Dictionary<int, double>();

        // Group hit objects by start time and process in chronological order.
        var timeGroups = bmsBeatmap.HitObjects
            .GroupBy(h => h.StartTime)
            .OrderBy(g => g.Key);

        foreach (var group in timeGroups)
        {
            var time = group.Key;
            var notes = group.ToArray();

            // Purge long notes that have ended before this time.
            removeExpiredLns(activeLnColumns, time);

            var notesToShuffle = IncludeScratch.Value
                ? notes
                : notes.Where(n => !BmsLayout.IsScratchColumn(n.Column, variant)).ToArray();

            if (notesToShuffle.Length == 0)
                continue;

            // Available columns = shuffle columns minus those occupied by active LNs.
            var available = shuffleColumns.Where(c => !activeLnColumns.ContainsKey(c)).ToArray();

            // Split available columns into safe and unsafe based on the threshold.
            var safe = new List<int>();
            var unsafeCols = new List<int>();

            foreach (var col in available)
            {
                if (time - lastNoteTime[col] > threshold)
                    safe.Add(col);
                else
                    unsafeCols.Add(col);
            }

            // Shuffle both pools so assignment within each is random.
            shuffle(rng, safe);
            shuffle(rng, unsafeCols);

            // Assign notes: safe columns first, then unsafe.
            var assigned = new List<int>();
            assigned.AddRange(safe);
            assigned.AddRange(unsafeCols);

            for (var i = 0; i < notesToShuffle.Length; i++)
            {
                var col = assigned[i];
                notesToShuffle[i].Column = col;

                if (lastNoteTime.ContainsKey(col))
                    lastNoteTime[col] = time;

                // If this note is a long note head, mark its column as occupied.
                if (notesToShuffle[i] is BmsLongNote)
                    activeLnColumns[col] = ((BmsLongNote)notesToShuffle[i]).EndTime;
            }
        }
    }

    private static void removeExpiredLns(Dictionary<int, double> activeLnColumns, double currentTime)
    {
        var expired = new List<int>();

        foreach (var (col, endTime) in activeLnColumns)
        {
            if (currentTime >= endTime)
                expired.Add(col);
        }

        foreach (var col in expired)
            activeLnColumns.Remove(col);
    }

    private static void shuffle<T>(Random rng, IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
