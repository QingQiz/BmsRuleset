#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsResultComparison : OsuTestScene
{
    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.CacheAs(Realm);
        return dependencies;
    }

    [Test]
    public void TestNewRecordComparesAgainstPreviousBest()
    {
        var current = score(100);
        var best = score(90);
        var lower = score(80);
        lower.TotalScore = 1_000_000;
        assertBest([current.DeepClone(), lower, best], current, best);
    }

    [Test]
    public void TestExcludesImportedCopiesOfCurrentScore()
    {
        var current = score(100);
        current.OnlineID = 123;
        current.LegacyOnlineID = 456;
        var onlineCopy = score(100);
        onlineCopy.OnlineID = 123;
        var legacyCopy = score(100);
        legacyCopy.LegacyOnlineID = 456;
        var best = score(90);
        assertBest([onlineCopy, legacyCopy, best], current, best);
    }

    [Test]
    public void TestIgnoresOtherMapsRulesetsUsersDeletedAndAutoplayScores()
    {
        var current = score(70);
        var wrongMap = score(100);
        wrongMap.BeatmapHash = "other";
        var wrongRuleset = score(100);
        wrongRuleset.Ruleset.ShortName = "other";
        var wrongUser = score(100);
        wrongUser.User = new APIUser { Id = 99 };
        var deleted = score(100);
        deleted.DeletePending = true;
        var autoplay = score(100);
        autoplay.Mods = [new BmsModAutoplay()];
        var best = score(80);
        assertBest([wrongMap, wrongRuleset, wrongUser, deleted, autoplay, best], current, best);
    }

    [Test]
    public void TestIncludesOfflineScores()
    {
        var current = score(70);
        var best = score(90);
        best.User = new APIUser { Id = 1 };
        assertBest([best], current, best);
    }

    [Test]
    public void TestRespectsExistingModMatching()
    {
        var current = score(70);
        var hiddenScratch = score(100);
        hiddenScratch.Mods = [new BmsModHideScratch()];
        var normal = score(80);
        assertBest([hiddenScratch, normal], current, normal);
        current.Mods = [new BmsModHideScratch()];
        assertBest([hiddenScratch, normal], current, hiddenScratch);
    }

    [Test]
    public void TestMissingStatisticsUseAccuracyAndTiesChooseEarlierScore()
    {
        var current = score(100);
        var earlier = score(0);
        earlier.Statistics = new Dictionary<HitResult, int>();
        earlier.Accuracy = 0.9;
        earlier.Date = DateTimeOffset.UtcNow.AddDays(-2);
        var later = score(90);
        later.Date = earlier.Date.AddDays(1);
        assertBest([later, earlier], current, earlier);
    }

    [Test]
    public void TestNoOtherScoreAndUnknownBeatmap()
    {
        var current = score(100);
        assertBest([current], current, null);
        var best = score(90);
        current.BeatmapHash = best.BeatmapHash = string.Empty;
        assertBest([best], current, null);
    }

    private void assertBest(ScoreInfo[] scores, ScoreInfo current, ScoreInfo? expected)
    {
        // Snapshot before scheduling so later cases can change mods or hashes without changing this case.
        var candidates = scores.Select(candidate => candidate.DeepClone()).ToArray();
        var currentSnapshot = current.DeepClone();
        currentSnapshot.Ruleset = current.Ruleset.Clone();
        var expectedId = expected?.ID;
        TestComparisonPopover popover = null!;

        AddStep("persist comparison candidates", () => Realm.Write(r =>
        {
            foreach (var candidate in candidates)
            {
                candidate.Ruleset = candidate.Ruleset.Clone();
                candidate.StatisticsJson = JsonConvert.SerializeObject(candidate.Statistics);
                candidate.MaximumStatisticsJson = JsonConvert.SerializeObject(candidate.MaximumStatistics);
                r.Add(candidate, true);
            }
        }));
        AddStep("load comparison", () => Child = popover = new TestComparisonPopover(currentSnapshot));
        AddUntilStep("best score query completes", () => popover.BestScoreTask?.IsCompleted == true);
        AddAssert("expected historical best selected", () => popover.BestScoreTask!.GetAwaiter().GetResult()?.ID, () => Is.EqualTo(expectedId));
        AddAssert("result is detached from database", () => popover.BestScoreTask!.GetAwaiter().GetResult()?.IsManaged != true);
        AddStep("remove comparison candidates", () => Realm.Write(r =>
        {
            foreach (var candidate in candidates)
                r.Remove(r.Find<ScoreInfo>(candidate.ID)!);
        }));
    }

    private partial class TestComparisonPopover(ScoreInfo current) : BmsResultComparisonPopover(current)
    {
        public Task<ScoreInfo?>? BestScoreTask { get; private set; }

        protected override Task<ScoreInfo?> LoadBestScoreAsync() => BestScoreTask = base.LoadBestScoreAsync();
    }

    private static ScoreInfo score(int perfect) => new()
    {
        BeatmapHash = "comparison-test",
        Ruleset = new BmsRuleset().RulesetInfo,
        User = new APIUser { Id = 42 },
        Statistics = new Dictionary<HitResult, int> { [HitResult.Perfect] = perfect },
        MaximumStatistics = new Dictionary<HitResult, int> { [HitResult.Perfect] = 100 },
    };
}
