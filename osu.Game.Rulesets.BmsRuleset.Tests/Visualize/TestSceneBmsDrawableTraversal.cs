#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsDrawableTraversal : BmsPlayerTestScene
{
    private BmsTestSkins.SkinKind skin;
    private BmsLongNoteMode mode;
    private static readonly HashSet<DrawableBmsHitObject> observed = [];
    private static int frameworkResultChecks;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)], skin);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsLongNote { StartTime = 5000, Duration = 2000, Column = 1 },
                new BmsNote { StartTime = 5000, Column = 2 },
                new BmsLandmine { StartTime = 5000, Column = 3, LandmineDamagePercent = 5 },
                new BmsNote { StartTime = 30000, Column = 2 },
            ],
            InvisibleNotes = [new BmsInvisibleNote { StartTime = 5000, Column = 4 }],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.HellChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.HellChargeNote)]
    public void TraversalSkipsUnusedFrameworkResultChecks(BmsTestSkins.SkinKind skin, BmsLongNoteMode mode)
    {
        AddStep("load mixed objects", () =>
        {
            this.skin = skin;
            this.mode = mode;
            ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!)
                .SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("seek before heads", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddStep("keep core judgement and framework lifetime work", () =>
        {
            var drawables = soundFixtureObjects();
            Assert.That(drawables.All(d => d.IsPresent), Is.True);
            var graphics = drawables.SelectMany(d => d.ChildrenOfType<Drawable>().Prepend(d)).ToArray();
            var originalGeometry = graphics.Select(d => (d.Position, d.Size, d.Scale, d.Alpha)).ToArray();
            var results = Player.Results.Count;
            var health = Player.HealthProcessor.Health.Value;

            observed.Clear();
            observed.UnionWith(drawables);
            frameworkResultChecks = 0;
            using var probe = new ScopedMethodProbe(typeof(DrawableHitObject).GetMethod("UpdateResult", BindingFlags.Instance | BindingFlags.NonPublic)!,
                typeof(TestSceneBmsDrawableTraversal).GetMethod(nameof(countResultCheck), BindingFlags.Static | BindingFlags.NonPublic)!);
            try
            {
                for (var frame = 0; frame < 32; frame++)
                {
                    foreach (var drawable in drawables)
                        Assert.That(drawable.UpdateSubTree(), Is.True);
                }
                Assert.That(frameworkResultChecks, Is.Zero);
                Assert.That(graphics.Select(d => (d.Position, d.Size, d.Scale, d.Alpha)), Is.EqualTo(originalGeometry));
                Assert.That(Player.Results.Count, Is.EqualTo(results));
                Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(health));

                foreach (var drawable in drawables) drawable.OnKilled();
                Assert.That(frameworkResultChecks, Is.EqualTo(drawables.Length), "The framework sample-stop and lifetime boundary still runs.");
                TestContext.WriteLine($"DRAWABLE_RESULT_CHECKS skin={skin} mode={mode} updates={32 * drawables.Length} checks=0 removalChecks={frameworkResultChecks}");
            }
            finally
            {
                observed.Clear();
            }
        });
    }

    private static void countResultCheck(DrawableHitObject __instance)
    {
        if (__instance is DrawableBmsHitObject drawable && observed.Contains(drawable))
            frameworkResultChecks++;
    }
}
