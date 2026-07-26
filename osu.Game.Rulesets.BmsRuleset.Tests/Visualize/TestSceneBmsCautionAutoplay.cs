#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
///     Regression test for autoplay missing a streak of notes after a #STOP in
///     99_outlaw_caution.bms (reported as "miss一大段 at 465 combo").
/// </summary>
/// <remarks>
///     A #STOP freezes the scroll. For notes that follow the stop, the scroll-based lifetime
///     calculation computed a visible window starting exactly at the note's StartTime, so the
///     drawable only became alive at its hit time. The autoplay press is delivered at StartTime
///     and is processed before the lifetime update that adds the drawable to the column's alive
///     entries — so OnPressed found zero candidates, the press was lost, and the note auto-missed.
///     The fix is a non-zero <c>minimum_future_lifetime</c> floor so notes are alive before their
///     StartTime. This test plays the chart with autoplay and asserts every judgement is Perfect.
/// </remarks>
[TestFixture]
public partial class TestSceneBmsCautionAutoplay : BmsPlayerTestScene
{
    private const double region_start = 65000;
    private const double region_end = 73500;

    protected override bool HasCustomSteps => true;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(beatmap => new BmsAutoGenerator(beatmap).Generate().Frames.Cast<ReplayFrame>().ToList());

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = loadCautionBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 145);
        return beatmap;
    }

    private static BmsBeatmap loadCautionBeatmap()
    {
        var allResources = Assembly.GetExecutingAssembly().GetManifestResourceNames();
        var resourceName = allResources.First(n => n.EndsWith("99_outlaw_caution.bms", StringComparison.OrdinalIgnoreCase));

        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)!;
        using var reader = new LineBufferedReader(stream);

        var decoded = new BmsBeatmapDecoder(referenceBpmMode: BmsReferenceBpmMode.StartBpm).Decode(reader);
        var src = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset())
        {
            ReferenceBpmMode = BmsReferenceBpmMode.StartBpm,
        }.Convert();

        // Re-host on a fresh BmsBeatmap so the test WorkingBeatmap doesn't try to load the
        // chart's real audio (unavailable headlessly). Preserves the stop-projected StartTimes
        // and the TimingMap needed for scroll/lifetime.
        var fresh = new BmsBeatmap
        {
            LayoutVariant = src.LayoutVariant,
            TotalColumns = src.TotalColumns,
            Rank = src.Rank,
            Total = src.Total,
            TimingMap = src.TimingMap,
            LockedLongNoteMode = src.LockedLongNoteMode,
        };

        foreach (var ho in src.HitObjects)
            fresh.HitObjects.Add(ho);

        fresh.Metadata.Title = src.Metadata.Title;
        return fresh;
    }

    [Test]
    public void AutoplayScoresAllPerfectAroundStopSection()
    {
        AddStep("load player", () => LoadPlayer(Array.Empty<Mod>()));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddStep($"seek to {region_start}", () => Player.GameplayClockContainer.Seek(region_start));
        AddUntilStep($"reached {region_end}", () => Player.GameplayClockContainer.CurrentTime >= region_end);

        // The note run immediately after the 34 ms LN section (T≈71264–73229) used to auto-miss
        // as Meh because the press at StartTime found no alive candidate. With the lifetime fix
        // every judgement in this window must be Perfect.
        AddAssert("all judgements are Perfect", () =>
        {
            var stats = Player.ScoreProcessor.Statistics;
            int nonPerfect = stats.Where(kv => kv.Key != HitResult.Perfect).Sum(kv => kv.Value);
            return nonPerfect == 0;
        });
    }
}
