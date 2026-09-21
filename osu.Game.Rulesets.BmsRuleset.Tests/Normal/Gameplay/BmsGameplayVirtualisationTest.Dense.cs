using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    private static int visibilityQueries;

    [TestCase(500)]
    [TestCase(240000)]
    [TestCase(3600000)]
    [NonParallelizable]
    public void MonotonicLifetimeSearchDoesNotScanTheEntireSong(double time)
    {
        var map = new BmsTimingMap(192, [], [new BmsBpmEvent(0, 120, 0)], [], 120);
        var note = new BmsNote { StartTime = time, Column = 1, ScrollPositionAtStartTime = time };
        var beatmap = attachBeatmap(new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            TimingMap = map,
            HitObjects = [note],
        });
        var entry = new BmsHitObjectLifetimeEntry(beatmap.HitObjects[0], new BmsGameplayScrollController(map), () => 0);
        using var probe = new ScopedMethodProbe(typeof(BmsTimingMap).GetMethod(nameof(BmsTimingMap.GetScrollPositionAtTime)),
            typeof(BmsGameplayVirtualisationTest).GetMethod(nameof(countVisibilityQuery), BindingFlags.Static | BindingFlags.NonPublic));
        visibilityQueries = 0;
        entry.RefreshLifetime();

        Assert.That(entry.LifetimeStart, Is.EqualTo(time - BmsDrawableRuleset.ComputeScrollTime(8)).Within(1));
        Assert.That(visibilityQueries, Is.LessThan(100), "Visibility search should depend on the visible interval, not song length.");
    }

    private static void countVisibilityQuery() => visibilityQueries++;
}
