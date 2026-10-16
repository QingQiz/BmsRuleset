#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Tests.Visual;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsInvisibleNote : BmsPlayerTestScene
{
    private BmsRulesetConfigManager config => (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!;

    private BmsInvisibleNoteLayer? layer => ((BmsColumn)Playfield.Stage.Columns[1]).InvisibleNoteLayer;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 5000, Column = 1 }],
            HitObjects = [new BmsNote { StartTime = 30000, Column = 2 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestVisibilityInputExpiryAndRewind(bool initiallyVisible)
    {
        AddStep("load with default setting", () =>
        {
            config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, initiallyVisible);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Playfield.Stage.IsLoaded);
        seek(4800);
        AddAssert("invisible notes retained", () => Playfield.Beatmap.InvisibleNotes.Count == 1);
        if (!initiallyVisible)
        {
            AddAssert("hidden markers have no entries", () => !Playfield.Stage.Columns[1].HitObjectContainer.Entries
                .Any(e => e.HitObject is BmsInvisibleNote));
            AddAssert("hidden markers have no layer", () => layer == null);
            AddAssert("hidden input gives no judgement", () => PressColumn(Playfield.Stage.Columns[1], 4800).Equals(PressOutcome.Empty));
            AddStep("release hidden column", () => ReleaseColumn(Playfield.Stage.Columns[1], 4800));
        }
        AddStep("enable display", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true));
        AddUntilStep("outline visible", () => layer is { IsPresent: true } && layer.Markers.Count == 1);
        AddAssert("markers never register hit objects", () => !Playfield.Stage.Columns[1].HitObjectContainer.Entries
            .Any(e => e.HitObject is BmsInvisibleNote));
        AddAssert("yellow hollow rectangle", () => layer!.Colour.Equals((osu.Framework.Graphics.Colour.ColourInfo)Color4.Yellow)
            && layer.DrawWidth > 0 && layer.MarkerHeight == 8);
        AddAssert("press gives no judgement", () => PressColumn(Playfield.Stage.Columns[1], 4800).Equals(PressOutcome.Empty));
        AddStep("release column", () => ReleaseColumn(Playfield.Stage.Columns[1], 4800));
        AddAssert("no score events", () => Player.Results.Count == 0);
        AddStep("disable display live", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
        AddUntilStep("hidden markers removed", () => layer?.IsPresent == false);
        AddStep("enable again", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true));
        seek(5300);
        AddUntilStep("expired without judgement", () => layer?.Markers.Count == 0);
        AddAssert("passing does not score", () => Player.Results.Count == 0);
        seek(4800);
        AddUntilStep("visible again after rewind", () => layer is { IsPresent: true } && layer.Markers.Count == 1);
        AddStep("restore default", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
    }

    [Test]
    public void BatchSnapshotsAreIsolatedAndStableUpdatesDoNotAllocate()
    {
        AddStep("enable markers", () => { config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, true); LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        seek(4800);
        AddUntilStep("marker loaded", () => layer is { IsLoaded: true } && layer.Markers.Count == 1);
        AddStep("validate geometry and buffer isolation", () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", flags)!
                .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
            var drawable = layer!;
            ulong frame = 0;
            for (var slot = 0; slot < 3; slot++)
            {
                Playfield.Stage.SetNoteHeightScale(1);
                drawable.UpdateSubTree();
                var node = generate(drawable, ++frame, slot, true)!;
                var field = node.GetType().GetField("markers", flags)!;
                var before = ((IEnumerable<BmsInvisibleNoteLayer.Marker>)field.GetValue(node)!).ToArray();
                Assert.That(before, Has.Length.EqualTo(1));
                var scroll = Playfield.ScrollController;
                var column = (BmsColumn)Playfield.Stage.Columns[1];
                var position = scroll.GetVisualScrollPosition(5000, Playfield.Beatmap.InvisibleNotes[0].ScrollPositionAtStartTime);
                var expectedY = drawable.DrawHeight - column.HitTargetPosition
                    - (float)((position - scroll.CurrentScrollPosition) * scroll.ScrollCoordinateScale(drawable.DrawHeight, column.HitTargetPosition));
                Assert.That(before[0].Y, Is.EqualTo(expectedY).Within(.001));
                Assert.That(before[0].Alpha, Is.EqualTo(1));
                Playfield.Stage.SetNoteHeightScale(1.37f);
                drawable.UpdateSubTree();
                _ = generate(drawable, ++frame, (slot + 1) % 3, true);
                Assert.That((IEnumerable<BmsInvisibleNoteLayer.Marker>)field.GetValue(node)!, Is.EqualTo(before));
                Assert.That(drawable.MarkerHeight, Is.EqualTo(10.96f).Within(.001));
            }
            for (var i = 0; i < 100; i++) drawable.UpdateSubTree();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) drawable.UpdateSubTree();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.That(allocated, Is.Zero);
            Assert.That(Player.Results, Is.Empty);
            Playfield.Stage.SetNoteHeightScale(1);
        });
        AddStep("restore default", () => config.SetValue(BmsRulesetSetting.ShowInvisibleNotes, false));
    }

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("clock caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }
}
