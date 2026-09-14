using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Containers;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga;

[TestFixture]
public partial class BmsBgaPoorLayerTest
{
    private BmsBeatmap beatmap;
    private TestDrawableRuleset ruleset;
    private BmsScoreProcessor scoreProcessor;
    private BmsBgaDisplay display;

    [SetUp]
    public void SetUp()
    {
        beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            HitObjects =
            {
                new BmsNote { StartTime = 1000, Column = 1 },
                new BmsNote { StartTime = 2000, Column = 1 },
            },
            Bga = new BmsBgaTimeline(new Dictionary<ushort, string> { [0] = "poor.png" },
                new Dictionary<ushort, BmsBgaDefinition>(), [], [], BmsPoorBgaMode.Replace),
        };
        ruleset = new TestDrawableRuleset(beatmap);
        scoreProcessor = new BmsScoreProcessor { Clock = ruleset.FrameStableClock };
        scoreProcessor.ApplyBeatmap(beatmap);
        display = new BmsBgaDisplay
        {
            Clock = ruleset.FrameStableClock,
            RenderOutsideHudVisibility = false,
        };
        setDisplayProperty("drawableRuleset", ruleset);
        setDisplayProperty("resolvedScoreProcessor", scoreProcessor);
        setTimeline(beatmap.Bga);
        invokeDisplay("LoadComplete");
        invokeDisplay("applyLayerVisibility");
        advanceTo(1000);
    }

    [TearDown]
    public void TearDown()
    {
        display.Dispose();
        scoreProcessor.Dispose();
        ruleset.TestClock.Dispose();
        ruleset.Dispose();
    }

    [TestCase(HitResult.Perfect, false, false)]
    [TestCase(HitResult.Perfect, false, true)]
    [TestCase(HitResult.Great, false, false)]
    [TestCase(HitResult.Great, false, true)]
    [TestCase(HitResult.Good, false, false)]
    [TestCase(HitResult.Good, false, true)]
    [TestCase(HitResult.Ok, true, false)]
    [TestCase(HitResult.Ok, true, true)]
    [TestCase(HitResult.Meh, true, false)]
    [TestCase(HitResult.Meh, true, true)]
    public void TestJudgementUsesComboAfterScoring(HitResult result, bool expectedVisible, bool hasCombo)
    {
        if (hasCombo)
            applyResult(0, HitResult.Perfect);

        // Column display events arrive before the host applies the score result.
        ruleset.GameplayEvents.RaiseJudgementDisplayed(result);
        Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.Zero);

        applyResult(1, result);

        Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.EqualTo(expectedVisible ? 1 : 0));
    }

    [Test]
    public void TestEmptyPoorOnlyTriggersAtZeroCombo([Values] bool hasCombo)
    {
        if (hasCombo)
            applyResult(0, HitResult.Perfect);

        scoreProcessor.RegisterEmptyPoor(1000, 2000, 1);

        Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.EqualTo(hasCombo ? 0 : 1));
    }

    [TestCase(BmsPoorBgaMode.Replace, 1, 0)]
    [TestCase(BmsPoorBgaMode.Add, 1, 1)]
    [TestCase(BmsPoorBgaMode.Off, 0, 1)]
    public void TestPoorBgaMode(BmsPoorBgaMode mode, float poorAlpha, float normalAlpha)
    {
        setTimeline(beatmap.Bga with { PoorMode = mode });
        applyResult(0, HitResult.Ok);

        Assert.Multiple(() =>
        {
            Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.EqualTo(poorAlpha));
            Assert.That(layer(BmsBgaLayer.Base).Alpha, Is.EqualTo(normalAlpha));
            Assert.That(layer(BmsBgaLayer.Layer1).Alpha, Is.EqualTo(normalAlpha));
            Assert.That(layer(BmsBgaLayer.Layer2).Alpha, Is.EqualTo(normalAlpha));
        });
    }

    [Test]
    public void TestNoPoorResourceKeepsNormalLayersVisible()
    {
        setTimeline(beatmap.Bga with { BitmapDefinitions = new Dictionary<ushort, string>() });
        applyResult(0, HitResult.Meh);

        Assert.Multiple(() =>
        {
            Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.Zero);
            Assert.That(layer(BmsBgaLayer.Base).Alpha, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestTimelinePoorEventTakesPrecedenceOverBitmapZero()
    {
        var poorEvent = new BmsBgaEvent(1000, 0, 1, BmsBgaLayer.Poor, 1);
        var activeEvents = getDisplayField<Dictionary<BmsBgaLayer, BmsBgaEvent>>("activeEvents");
        activeEvents[BmsBgaLayer.Poor] = poorEvent;

        applyResult(0, HitResult.Meh);

        Assert.Multiple(() =>
        {
            Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.EqualTo(1));
            Assert.That(activeEvents[BmsBgaLayer.Poor], Is.SameAs(poorEvent));
        });
    }

    [Test]
    public void TestRepeatedEmptyPoorRefreshesDuration()
    {
        applyResult(0, HitResult.Ok);
        advanceTo(1400);
        scoreProcessor.RegisterEmptyPoor(1400, 2000, 1);
        advanceTo(1501);
        Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.EqualTo(1));

        advanceTo(1901);
        Assert.Multiple(() =>
        {
            Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.Zero);
            Assert.That(layer(BmsBgaLayer.Base).Alpha, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestEmptyPoorWithComboDoesNotExtendVisiblePoorLayer()
    {
        applyResult(0, HitResult.Meh);
        advanceTo(1200);
        applyResult(1, HitResult.Perfect);
        advanceTo(1400);
        scoreProcessor.RegisterEmptyPoor(1400, 2000, 1);
        advanceTo(1501);

        Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.Zero);
    }

    [Test]
    public void TestSyntheticLongNotePoorTriggersLayer([Values] BmsLongNoteEndpointKind kind)
    {
        applyResult(0, HitResult.Perfect);
        var longNote = new BmsLongNote { StartTime = 2000, Duration = 500, Column = 1, Beatmap = beatmap };

        scoreProcessor.ApplySyntheticLongNoteEndpoint(new BmsLongNoteEndpointResult(longNote, kind, 2000, 1, HitResult.Meh));

        Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.EqualTo(1));
    }

    private void applyResult(int index, HitResult type)
    {
        var note = beatmap.HitObjects[index];
        scoreProcessor.ApplyResult(new JudgementResult(note, note.CreateJudgement()) { Type = type });
    }

    private Container layer(BmsBgaLayer layer) => getDisplayField<Dictionary<BmsBgaLayer, Container>>("layerHosts")[layer];

    private void setTimeline(BmsBgaTimeline timeline) =>
        typeof(BmsBgaDisplay).GetField("bga", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(display, timeline);

    private void setDisplayProperty(string name, object value) =>
        typeof(BmsBgaDisplay).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(display, value);

    private T getDisplayField<T>(string name) =>
        (T)typeof(BmsBgaDisplay).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;

    private void invokeDisplay(string name) =>
        typeof(BmsBgaDisplay).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(display, []);

    private void advanceTo(double time)
    {
        var clockType = typeof(FrameStabilityContainer);
        var manualClock = (ManualClock)clockType.GetField("manualClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ruleset.TestClock)!;
        var framedClock = (FramedClock)clockType.GetField("framedClock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ruleset.TestClock)!;
        manualClock.CurrentTime = time;
        framedClock.ProcessFrame();
        invokeDisplay("Update");
    }

    private sealed partial class TestDrawableRuleset(BmsBeatmap beatmap) : BmsDrawableRuleset(new BmsRuleset(), beatmap)
    {
        public FrameStabilityContainer TestClock { get; } = new();

        public override IFrameStableClock FrameStableClock => TestClock;
    }
}
