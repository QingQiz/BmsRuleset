using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeSettingsStoreTest
{
    [Test]
    public void StoresOnlyFourSettingsAndSeparatesChartsByHash()
    {
        using var storage = new TemporaryNativeStorage($"bms-practice-{Guid.NewGuid():N}");
        var store = new BmsPracticeSettingsStore(storage);
        var expected = new BmsPracticeSettings { StartTime = 1000, EndTime = 2000, SkipResults = true, CountIn = 2 };
        store.Save("first", expected);
        Assert.That(store.Load("first"), Is.EqualTo(expected));
        Assert.That(store.Load("second"), Is.Null);
        using var stream = storage.GetStream(storage.GetFiles("bms/practice").Single(), FileAccess.Read, FileMode.Open);
        using var json = JsonDocument.Parse(stream);
        Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(new[] { "StartTime", "EndTime", "SkipResults", "CountIn" }));
    }

    [Test]
    public void InvalidOrOutdatedRangesFallBackToFullChart()
    {
        using var storage = new TemporaryNativeStorage($"bms-practice-{Guid.NewGuid():N}");
        var store = new BmsPracticeSettingsStore(storage);
        var chart = new BmsBeatmap { HitObjects = [new BmsNote { StartTime = 1000 }] };
        store.Save("chart", new BmsPracticeSettings { StartTime = 5000, EndTime = 6000 });
        Assert.That(store.Load("chart", chart), Is.EqualTo(BmsPracticeSettings.CreateDefault(chart)));
        store.Save("invalid", new BmsPracticeSettings { StartTime = 0, EndTime = double.NaN });
        Assert.That(store.Load("invalid"), Is.Null);
    }
}
