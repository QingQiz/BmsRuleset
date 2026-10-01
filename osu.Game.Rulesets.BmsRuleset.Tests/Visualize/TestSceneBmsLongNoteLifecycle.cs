#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsLongNoteLifecycle : BmsPlayerTestScene
{
    private static readonly JsonDocument baseline = readBaseline();
    private BmsLongNoteMode mode;
    private bool includeMine;
    private bool duplicateHeads;
    private DrawableBmsHitObject first = null!;
    private BmsScoreProcessor score => (BmsScoreProcessor)Player.ScoreProcessor;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 24,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsLongNote { StartTime = 5000, Duration = 2000, Column = 1 },
                new BmsLongNote { StartTime = duplicateHeads ? 5000 : 9000, Duration = 1000, Column = 1 },
                new BmsNote { StartTime = 30000, Column = 2 },
            ],
        };
        if (includeMine)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 5500, Column = 1, LandmineDamagePercent = 10 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(h => h.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsLongNoteMode.LongNote, false)]
    [TestCase(BmsLongNoteMode.ChargeNote, false)]
    [TestCase(BmsLongNoteMode.HellChargeNote, false)]
    [TestCase(BmsLongNoteMode.LongNote, true)]
    [TestCase(BmsLongNoteMode.ChargeNote, true)]
    [TestCase(BmsLongNoteMode.HellChargeNote, true)]
    public void RewindIntoPooledBodyPreservesHeadAndReplaysTail(BmsLongNoteMode longNoteMode, bool mine)
    {
        BmsLongNote note = null!;
        BmsLongNoteJudgementController originalController = null!;
        AddStep("load long notes", () => { mode = longNoteMode; includeMine = mine; duplicateHeads = false; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        seek(5000);
        AddStep("observe synchronous head and tail submission", () =>
        {
            first = (DrawableBmsHitObject)Playfield.GetAliveObjectAtTime(5000)!;
            note = (BmsLongNote)first.HitObject;
            originalController = first.LongNoteJudgement!;
            first.OnNewResult += (_, _) => Assert.That(((ILongNoteHolder)first).IsHoldingLongNote, Is.True,
                "The head is pinned, and the normal-LN tail is not marked judged, when the main result is submitted.");
            Playfield.Stage.Columns[1].HandlePress(5000);
        });
        capture("head");
        seek(5501);
        capture("body and mine");
        seek(6900);
        AddStep("release before tail", () => Playfield.Stage.Columns[1].HandleRelease(6900));
        capture("early tail");
        seek(6999);
        capture("body after successful tail");
        seek(7400);
        AddAssert("first long note pooled", () => !Playfield.AllColumnAliveObjects().Any(d => d.HitObject.StartTime == 5000));
        AddStep("pool return releases both sides of the binding", () =>
        {
            Assert.That(first.LongNoteJudgement, Is.Not.SameAs(originalController));
            Assert.Throws<InvalidOperationException>(() => ((IBmsLongNoteJudgementSink)((BmsColumn)Playfield.Stage.Columns[1]).Judgements).Retire(note));
        });
        seek(9000);
        AddStep("judge another pooled representation", () => Playfield.Stage.Columns[1].HandlePress(9000));
        capture("second head");
        seek(6000);
        AddAssert("first head restored inside body", () =>
            Playfield.GetAliveObjectAtTime(5000) is ILongNoteHolder { IsHoldingLongNote: true });
        AddAssert("engine restores the same controller", () =>
            ReferenceEquals(((DrawableBmsHitObject)Playfield.GetAliveObjectAtTime(5000)!).LongNoteJudgement, originalController));
        capture("rewound body");
        seek(6900);
        AddStep("release restored tail", () => Playfield.Stage.Columns[1].HandleRelease(6900));
        capture("replayed tail");
        seek(7400);
        capture("retired again");
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void IdenticalHeadTimeAndColumnHaveIndependentControllers(BmsLongNoteMode longNoteMode)
    {
        AddStep("load overlapping heads", () => { mode = longNoteMode; includeMine = false; duplicateHeads = true; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        seek(4900);
        AddStep("state belongs to chart identity", () =>
        {
            var notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().Where(d => d.HitObject is BmsLongNote).ToArray();
            Assert.That(notes, Has.Length.EqualTo(2));
            Assert.That(notes[0].LongNoteJudgement, Is.Not.SameAs(notes[1].LongNoteJudgement));
            notes[0].TryHit(HitResult.Perfect);
            Assert.That(notes[0].LongNoteJudgement!.HeadJudged, Is.True);
            Assert.That(notes[1].LongNoteJudgement!.HeadJudged, Is.False);
        });
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(time); });
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private void capture(string checkpoint)
    {
        var scenario = TestContext.CurrentContext.Test.Name;
        AddStep(checkpoint, () =>
        {
            var objects = Player.GameplayState.Beatmap.HitObjects.ToArray();
            var trace = JsonSerializer.Serialize(new
            {
                checkpoint,
                events = score.JudgementEvents,
                results = Player.Results.Select(r => new { id = Array.IndexOf(objects, r.HitObject), r.Type, r.TimeAbsolute, r.GameplayRate }),
                progression = BmsExScore.CreateProgression(score.JudgementEvents),
                combo = score.Combo.Value,
                total = score.TotalScore.Value,
                health = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory,
            });
            TestContext.WriteLine("P3_TRACE " + trace);
            Assert.That(trace, Is.EqualTo(JsonSerializer.Serialize(baseline.RootElement.GetProperty("traces").GetProperty(scenario).GetProperty(checkpoint))),
                "Ordered trace recorded on P2 before migrating long-note ownership.");
        });
    }

    private static JsonDocument readBaseline()
    {
        using var stream = typeof(TestSceneBmsLongNoteLifecycle).Assembly.GetManifestResourceStream(
            "osu.Game.Rulesets.BmsRuleset.Tests.reference_fixtures.long_note_p2_trace.json")
                           ?? throw new InvalidDataException("Missing P2 long-note trace.");
        return JsonDocument.Parse(stream);
    }
}
