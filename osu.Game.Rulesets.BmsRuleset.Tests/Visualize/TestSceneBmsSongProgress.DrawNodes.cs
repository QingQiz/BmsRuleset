#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsSongProgress
{
    [TestCase(false)]
    [TestCase(true)]
    public void TestHitErrorMeterDenseDrawBuffersAreIndependentAndReuseStorage(bool gradient)
    {
        var generate = typeof(Drawable).GetMethod("GenerateDrawNodeSubtree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Drawable, ulong, int, bool, DrawNode?>>();
        BmsHitErrorMeter meter = null!;
        var manual = new ManualClock();
        var clock = new FramedClock(manual);
        var frame = 0UL;

        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("prepare ten thousand lines", () =>
        {
            Player.GameplayClockContainer.Stop();
            meter = Player.HUDOverlay.ChildrenOfType<BmsHitErrorMeter>().Single();
            meter.Colour = gradient
                ? osu.Framework.Graphics.Colour.ColourInfo.GradientHorizontal(Colour4.Cyan, Colour4.White)
                : Colour4.White;
            meter.Clock = clock;
            meter.ProcessCustomClock = false;
            meter.Clear();
            for (var i = 0; i < 10000; i++)
                meter.Judgements.AddJudgement((i % 1000) / 1000f, Colour4.Cyan, null);
            clock.ProcessFrame();
            meter.UpdateSubTree();
            meter.UpdateSubTreeMasking();
            Assert.That(meter.Judgements.Lines, Has.Count.EqualTo(10000));
        });
        AddStep("check bounded allocations and buffer isolation", () =>
        {
            for (var slot = 0; slot < 3; slot++) snapshot(slot);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            {
                manual.CurrentTime += 8;
                clock.ProcessFrame();
                meter.Judgements.UpdateSubTree();
                meter.Judgements.UpdateSubTreeMasking();
                for (var slot = 0; slot < 3; slot++) snapshot(slot);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.LessThan(32000));
            var previous = snapshot(0);
            var storedLines = previous.Lines.ToArray();
            var storedColours = Enumerable.Range(0, previous.Lines.Count).Select(previous.GetLineColour).ToArray();
            meter.Colour = Colour4.Red;
            snapshot(1);
            for (var i = 0; i < storedColours.Length; i++)
                Assert.That(previous.GetLineColour(i), Is.EqualTo(storedColours[i]), "A colour change must not mutate an earlier draw buffer.");
            meter.Clear();
            Assert.That(snapshot(1).Lines, Is.Empty);
            Assert.That(previous.Lines, Is.EqualTo(storedLines), "Clearing the live list must not mutate an earlier draw buffer.");
            Assert.That(snapshot(2).Lines, Is.Empty);
            Assert.That(snapshot(0).Lines, Is.Empty);
        });

        BmsHitErrorMeter.JudgementsLayer.JudgementsDrawNode snapshot(int slot)
            => (BmsHitErrorMeter.JudgementsLayer.JudgementsDrawNode)generate(meter.Judgements, ++frame, slot, false)!;
    }
}
