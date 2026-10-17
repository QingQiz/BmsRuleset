using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestMeasureLineContainerClipsLinesToStageBounds()
    {
        Assert.That(new BmsMeasureLineContainer().Masking, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestMeasureLineHidesAfterTimeDuringStopAndReturnsOnRewind(bool constantScroll)
    {
        var map = new BmsTimingMap(192, [new BmsMeasureInfo(0, 0, 192, 1)],
            [new BmsBpmEvent(0, 120, 1000)], [new BmsStopEvent(0, 2000, 192, 120, 0)]);
        var scroll = new BmsGameplayScrollController(map) { ConstantScrollActive = constantScroll };
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TimingMap = map,
        }));
        var stage = playfield.Stage;
        stage.RelativeSizeAxes = Axes.None;
        stage.Height = 600;
        // Only geometry is needed to exercise visibility without a game host or skin loader.
        typeof(Drawable).GetField("loadState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(stage, LoadState.Loaded);
        using var parent = new Container();
        var line = new BmsMeasureLine();
        parent.Add(line);
        typeof(Drawable).GetProperty(nameof(Drawable.Parent))!.SetValue(line, parent);
        line.Apply(new BmsMeasureLineContainer.MeasureLineInfo(0, 1000), scroll, stage);
        var update = typeof(BmsMeasureLine).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!;

        foreach (var time in new[] { 999d, 1000, 1001, 1500, 1000, 999 })
        {
            scroll.Update(time);
            update.Invoke(line, null);
            Assert.That(line.Alpha, Is.EqualTo(time <= 1000 ? 1 : 0), $"Display time: {time}");
        }
    }
}
