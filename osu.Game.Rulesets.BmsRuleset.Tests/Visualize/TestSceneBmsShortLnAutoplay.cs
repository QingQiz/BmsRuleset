#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
///     Supplementary autoplay check: short and degenerate (sub-tick) long notes — in both regular
///     and scratch columns — must all be judged Perfect by autoplay. This rules out the LN drawable
///     pipeline as the cause of the caution-chart miss streak (which was traced to #STOP lifetime,
///     see <see cref="TestSceneBmsCautionAutoplay" />).
/// </summary>
[TestFixture]
public partial class TestSceneBmsShortLnAutoplay : BmsPlayerTestScene
{
    private const double start = 3000;
    private const double ln_spacing = 103.45;
    private const double ln_dur = 34.48; // matches the caution chart's short LN run
    private static readonly int[] ln_cols = { 7, 5, 4, 3, 1, 2, 6, 0, 7, 5, 4, 3, 1, 2, 6, 0 };

    protected override bool HasCustomSteps => true;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(b => new BmsAutoGenerator(b).Generate().Frames.ToList());

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 3,
            Total = 300,
        };

        for (var i = 0; i < ln_cols.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsLongNote
            {
                StartTime = start + i * ln_spacing,
                Duration = ln_dur,
                Column = ln_cols[i],
            });
        }

        var noteCols = new[] { 7, 4, 6, 5, 3, 2, 1 };
        for (var i = 0; i < noteCols.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = start + ln_cols.Length * ln_spacing + 200 + i * 100,
                Column = noteCols[i],
            });
        }

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 145);
        return beatmap;
    }

    [Test]
    public void AutoplayJudgesShortLnsAndFollowingNotesPerfectly()
    {
        AddStep("load player", () => LoadPlayer(Array.Empty<Mod>()));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);

        AddStep("seek to start", () => Player.GameplayClockContainer.Seek(start - 500));
        AddUntilStep("reached end", () => Player.GameplayClockContainer.CurrentTime >= start + ln_cols.Length * ln_spacing + 1200);

        AddAssert("all judgements are Perfect", () =>
        {
            int nonPerfect = Player.ScoreProcessor.Statistics.Where(kv => kv.Key != HitResult.Perfect).Sum(kv => kv.Value);
            return nonPerfect == 0;
        });
    }
}
