#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
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
    private ScoreInfo simulationScore = null!;
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
        double? originalHeadTime = null;
        AddStep("load long notes", () => { mode = longNoteMode; includeMine = mine; duplicateHeads = false; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddStep("capture score mods", () =>
        {
            simulationScore = new ScoreInfo { Mods = Player.ScoreProcessor.Mods.Value.ToArray() };
        });
        seek(5000);
        AddStep("observe synchronous head and tail submission", () =>
        {
            first = (DrawableBmsHitObject)Playfield.GetAliveObjectAtTime(5000)!;
            note = (BmsLongNote)first.HitObject;
            first.OnNewResult += (source, _) => Assert.That(((BmsColumn)Playfield.Stage.Columns[1]).Judgements.GetLongNoteState((BmsLongNote)source.HitObject).IsHolding, Is.True,
                "The head is pinned, and the normal-LN tail is not marked judged, when the main result is submitted.");
            PressColumn(Playfield.Stage.Columns[1], 5000);
            originalHeadTime = ((BmsColumn)Playfield.Stage.Columns[1]).Judgements.GetLongNoteState(note).HeadTime;
        });
        capture("head");
        seek(5501);
        capture("body and mine");
        seek(6900);
        AddStep("release before tail", () => ReleaseColumn(Playfield.Stage.Columns[1], 6900));
        capture("early tail");
        seek(6999);
        capture("body after successful tail");
        seek(7400);
        AddAssert("first long note pooled", () => !Playfield.AllColumnAliveObjects().Any(d => d.HitObject.StartTime == 5000));
        AddStep("pool return releases both sides of the binding", () =>
        {
            var context = ((BmsColumn)Playfield.Stage.Columns[1]).Judgements;
            Assert.Throws<InvalidOperationException>(() => ((BmsColumn)Playfield.Stage.Columns[1]).JudgementBridge.GetRepresentation(note));
            Assert.That(context.GetLongNoteState(note).HeadTime, Is.EqualTo(originalHeadTime));
        });
        seek(9000);
        AddStep("judge another pooled representation", () => PressColumn(Playfield.Stage.Columns[1], 9000));
        capture("second head");
        seek(6000);
        AddAssert("first head restored inside body", () =>
            Playfield.GetAliveObjectAtTime(5000) != null && ((BmsColumn)Playfield.Stage.Columns[1]).Judgements.GetLongNoteState(note).IsHolding);
        AddAssert("engine restores the stored head", () =>
            ((BmsColumn)Playfield.Stage.Columns[1]).Judgements.GetLongNoteState(note).HeadTime == originalHeadTime);
        capture("rewound body");
        seek(6900);
        AddStep("release restored tail", () => ReleaseColumn(Playfield.Stage.Columns[1], 6900));
        capture("replayed tail");
        seek(7400);
        capture("retired again");
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void IdenticalHeadTimeAndColumnHaveIndependentState(BmsLongNoteMode longNoteMode)
    {
        AddStep("load overlapping heads", () => { mode = longNoteMode; includeMine = false; duplicateHeads = true; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        seek(4900);
        AddStep("state belongs to chart identity", () =>
        {
            var notes = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().Where(d => d.HitObject is BmsLongNote).ToArray();
            Assert.That(notes, Has.Length.EqualTo(2));
            var context = ((BmsColumn)Playfield.Stage.Columns[1]).Judgements;
            context.TryPressRegisteredNote(notes[0].HitObject, HitResult.Perfect,
                new BmsPressContext(Playfield.Time.Current, new BmsFrameContext(Playfield.Time.Current, 1)));
            Assert.That(context.GetLongNoteState((BmsLongNote)notes[0].HitObject).HeadJudged, Is.True);
            Assert.That(context.GetLongNoteState((BmsLongNote)notes[1].HitObject).HeadJudged, Is.False);
        });
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void DefaultsReplaceTheBindingAndPreserveThePendingTail(BmsLongNoteMode longNoteMode)
    {
        AddStep("load long notes", () => { mode = longNoteMode; includeMine = false; duplicateHeads = false; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        seek(5003);
        AddStep("hold head", () => PressColumn(Playfield.Stage.Columns[1], 5000));
        seek(5204);
        AddStep("reapply active defaults twice", () =>
        {
            var context = ((BmsColumn)Playfield.Stage.Columns[1]).Judgements;
            var drawable = Playfield.GetAliveObjectAtTime(5000)!;
            var note = (BmsLongNote)drawable.HitObject;
            var state = context.GetLongNoteState(note);
            var result = drawable.Result;
            var events = JsonSerializer.Serialize(score.JudgementEvents);
            var order = context.GetNoteCandidates().ToArray();
            var generation = drawable.BindingGeneration;
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            Assert.That(drawable.BindingGeneration, Is.GreaterThan(generation));
            Assert.That(((BmsColumn)Playfield.Stage.Columns[1]).JudgementBridge.GetRepresentation(note), Is.SameAs(drawable));
            Assert.That(context.GetLongNoteState(note), Is.EqualTo(state));
            Assert.That(context.GetNoteCandidates(), Is.EqualTo(order));
            Assert.That(context.GetPendingLongNoteTails().Count(n => ReferenceEquals(n, note)), Is.EqualTo(1));
            Assert.That(drawable.Result, Is.SameAs(result));
            Assert.That(result.TimeOffset, Is.Zero, "Preserve the framework's existing ApplyDefaults timestamp semantics.");
            ((BmsColumn)Playfield.Stage.Columns[1]).JudgementBridge.UnbindNoteRepresentation(drawable, generation);
            Assert.That(((BmsColumn)Playfield.Stage.Columns[1]).JudgementBridge.GetRepresentation(note), Is.SameAs(drawable));
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(JsonSerializer.Serialize(score.JudgementEvents), Is.EqualTo(events));
        });
        seek(6900);
        AddStep("release the rebound tail", () => ReleaseColumn(Playfield.Stage.Columns[1], 6900));
        AddAssert("tail completed through new binding", () =>
        {
            var note = Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().First();
            return ((BmsColumn)Playfield.Stage.Columns[1]).Judgements.GetLongNoteState(note).TailJudged;
        });
    }

    [Test]
    public void DefaultsPreserveLandmineCrossingAndItsResult()
    {
        AddStep("load mine", () => { mode = BmsLongNoteMode.HellChargeNote; includeMine = true; duplicateHeads = false; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        seek(5000);
        AddStep("hold mine column", () => PressColumn(Playfield.Stage.Columns[1], 5000));
        seek(5501);
        AddStep("reapply detonated mine", () =>
        {
            var context = ((BmsColumn)Playfield.Stage.Columns[1]).Judgements;
            var drawable = Playfield.GetAliveObjectAtTime(5500)!;
            var mine = (BmsLandmine)drawable.HitObject;
            var state = context.GetLandmineState(mine);
            var result = drawable.Result;
            var count = Player.Results.Count;
            var generation = drawable.BindingGeneration;
            Assert.That(state.Detonated, Is.True);
            mine.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            Assert.That(drawable.BindingGeneration, Is.GreaterThan(generation));
            Assert.That(((BmsColumn)Playfield.Stage.Columns[1]).JudgementBridge.GetRepresentation(mine), Is.SameAs(drawable));
            Assert.That(context.GetLandmineState(mine), Is.EqualTo(state));
            Assert.That(drawable.Result, Is.SameAs(result));
            context.UpdateLandmines(new BmsFrameContext(Playfield.Time.Current, 1), true);
            Assert.That(Player.Results.Count, Is.EqualTo(count));
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
            var time = Playfield.Time.Current;
            List<BmsReplayFrame> frames = [new(0), new(5000, BmsAction.Key1)];
            if (time >= 6900) frames.Add(new(6900));
            if (time >= 9000) frames.Add(new(9000, BmsAction.Key1));
            // A completed replay settles held tails, so compare it at checkpoints after releasing.
            if (frames[^1].Actions.Count == 0)
            {
                frames.Add(new(time));
                var replay = new Replay { Frames = frames.Cast<ReplayFrame>().ToList(), HasReceivedAllFrames = true };
                var expected = BmsReplaySimulation.Run(Playfield.Beatmap, replay, simulationScore);
                Assert.That(JsonSerializer.Serialize(score.JudgementEvents), Is.EqualTo(JsonSerializer.Serialize(expected.Judgements)));
                Assert.That(JsonSerializer.Serialize(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory), Is.EqualTo(JsonSerializer.Serialize(expected.Gauges)));
            }

            // Keep the pre-refactor score expectations while allowing intentional logical timestamp changes.
            var previous = baseline.RootElement.GetProperty("traces").GetProperty(scenario).GetProperty(checkpoint);
            Assert.That(JsonSerializer.Serialize(BmsExScore.CreateProgression(score.JudgementEvents)), Is.EqualTo(JsonSerializer.Serialize(previous.GetProperty("progression"))));
            Assert.That(score.Combo.Value, Is.EqualTo(previous.GetProperty("combo").GetInt32()));
            Assert.That(score.TotalScore.Value, Is.EqualTo(previous.GetProperty("total").GetInt64()));
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
