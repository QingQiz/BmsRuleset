#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsSongProgress
{
    private static int judgementLeafPreparations;

    [Test]
    [NonParallelizable]
    [Category("JudgementInstrumentation")]
    public void TestHitErrorMeterStableDrawBuffersSkipLeafPreparation()
    {
        var generateMethod = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var generate = generateMethod.CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        BmsHitErrorMeter meter = null!;
        Container judgements = null!;
        var manual = new ManualClock();
        var clock = new FramedClock(manual);
        var frame = 0UL;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("prepare twenty lines", () =>
        {
            Player.GameplayClockContainer.Stop();
            meter = Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
            meter.Clock = clock;
            meter.ProcessCustomClock = false;
            meter.Clear();
            var processor = (BmsScoreProcessor)Player.GameplayState.ScoreProcessor;
            for (var i = 0; i < 20; i++)
                processor.RegisterEmptyPoor(500 + i * 5, 1000, 0);
            clock.ProcessFrame();
            meter.UpdateSubTree();
            meter.UpdateSubTreeMasking();
            judgements = meter.ChildrenOfType<Container>().Single(child => child.Name == "judgements");
            Assert.That(judgements.Children.Count, Is.EqualTo(20));
        });
        AddStep("check independent buffer caches and invalidation", () =>
        {
            using var probe = new ScopedMethodProbe(generateMethod,
                typeof(TestSceneBmsSongProgress).GetMethod(nameof(countJudgementLeafPreparation), BindingFlags.Static | BindingFlags.NonPublic)!);
            // Each slot must first own a complete snapshot before it can reuse its child list.
            for (var slot = 0; slot < 3; slot++) generate(judgements, ++frame, slot, false);
            judgementLeafPreparations = 0;
            for (var repeat = 0; repeat < 32; repeat++)
                for (var slot = 0; slot < 3; slot++) generate(judgements, ++frame, slot, false);
            Assert.That(judgementLeafPreparations, Is.Zero);

            var line = (BmsHitErrorMeter.JudgementLine)judgements.Children.First();
            line.Alpha = 0.17f;
            verifyRefresh(20, "leaf alpha");
            line.Colour = Colour4.Cyan;
            verifyRefresh(20, "leaf colour");
            meter.JudgementLineThickness.Value = 6;
            verifyRefresh(20, "line width");
            meter.Position += new osuTK.Vector2(7, 11);
            verifyRefresh(20, "parent position");
            manual.CurrentTime = 8;
            clock.ProcessFrame();
            meter.UpdateSubTree();
            meter.UpdateSubTreeMasking();
            verifyRefresh(20, "fade update");
            ((BmsScoreProcessor)Player.GameplayState.ScoreProcessor).RegisterEmptyPoor(700, 1000, 0);
            meter.UpdateSubTree();
            meter.UpdateSubTreeMasking();
            verifyRefresh(21, "new judgement");
            meter.Clear();
            manual.CurrentTime = 16;
            clock.ProcessFrame();
            meter.UpdateSubTree();
            meter.UpdateSubTreeMasking();
            Assert.That(judgements.Children, Is.Empty);
            for (var slot = 0; slot < 3; slot++)
            {
                var node = generate(judgements, ++frame, slot, false);
                Assert.That(node, Is.Null);
            }
            TestContext.WriteLine("METER_DRAW_CACHE stablePreparations=0 slots=3 framesPerSlot=32");

            void verifyRefresh(int count, string reason)
            {
                judgementLeafPreparations = 0;
                for (var slot = 0; slot < 3; slot++)
                {
                    var node = (ICompositeDrawNode)generate(judgements, ++frame, slot, false)!;
                    Assert.That(node.Children, Has.Count.EqualTo(count), reason);
                }
                Assert.That(judgementLeafPreparations, Is.EqualTo(count * 3), reason);
                judgementLeafPreparations = 0;
                for (var slot = 0; slot < 3; slot++) generate(judgements, ++frame, slot, false);
                Assert.That(judgementLeafPreparations, Is.Zero, reason);
            }
        });
    }

    private static void countJudgementLeafPreparation(Drawable __instance)
    {
        if (__instance is BmsHitErrorMeter.JudgementLine)
            judgementLeafPreparations++;
    }
}
