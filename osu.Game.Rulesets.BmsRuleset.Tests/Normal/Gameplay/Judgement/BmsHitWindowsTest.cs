using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsHitWindowsTest
{
    [Test]
    public void TestHitWindowDefaultRankIsNormalSevenKeysBeatoraja()
    {
        var windows = createWindows();
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(15).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(45).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(112.5).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(210).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(210).Within(0.001));
    }

    [Test]
    [TestCase(0, 5, 15, 37.5, 70)]
    [TestCase(1, 10, 30, 75, 140)]
    [TestCase(2, 15, 45, 112.5, 210)]
    [TestCase(3, 20, 60, 150, 280)]
    [TestCase(4, 25, 75, 187.5, 350)]
    public void TestHitWindowRank(int rank, double expectedPerfect, double expectedGreat, double expectedGood, double expectedBad)
    {
        var windows = createWindows(rank);
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(expectedPerfect).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(expectedGreat).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(expectedGood).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(expectedBad).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Meh), Is.EqualTo(expectedBad).Within(0.001));
    }

    [Test]
    public void TestHitWindowCanUseExRankPercentage()
    {
        var windows = createWindows(exRank: 200);
        windows.SetDifficulty(5);

        Assert.That(windows.WindowFor(HitResult.Perfect), Is.EqualTo(30).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Great), Is.EqualTo(90).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Good), Is.EqualTo(225).Within(0.001));
        Assert.That(windows.WindowFor(HitResult.Ok), Is.EqualTo(420).Within(0.001));
    }

    [Test]
    public void TestHitWindowsIncludeMiss()
    {
        var windows = createWindows();

        Assert.That(windows.IsHitResultAllowed(HitResult.Miss), Is.True);
        Assert.That(windows.GetAllAvailableWindows(), Has.Some.Matches<(HitResult result, double length)>(window => window.result == HitResult.Miss));
    }

    [Test]
    public void TestHitWindowUsesLayoutAndColumnProfile()
    {
        var fiveKey = createWindows(rank: 3, BmsLayoutVariant.Bms5K, column: 1);
        var scratch = createWindows(rank: 3, column: 0);

        fiveKey.SetDifficulty(5);
        scratch.SetDifficulty(5);

        Assert.That(fiveKey.WindowFor(HitResult.Good), Is.EqualTo(100).Within(0.001));
        Assert.That(scratch.WindowFor(HitResult.Perfect), Is.EqualTo(30).Within(0.001));
        Assert.That(scratch.WindowFor(HitResult.Great), Is.EqualTo(70).Within(0.001));
        Assert.That(scratch.WindowFor(HitResult.Ok), Is.EqualTo(290).Within(0.001));
    }
    private static HitWindows createWindows(int rank = 2, BmsLayoutVariant layout = BmsLayoutVariant.Bme7K,
                                            int column = 1, double? exRank = null)
    {
        var beatmap = new BmsBeatmap { Rank = rank, LayoutVariant = layout, TotalColumns = BmsLayout.GetTotalColumns(layout) };
        var note = new BmsNote { Beatmap = beatmap, Column = column, ExRank = exRank };
        beatmap.HitObjects.Add(note);
        note.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        using var score = new BmsScoreProcessor();
        score.ApplyBeatmap(beatmap);
        return note.HitWindows;
    }

}
