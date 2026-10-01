#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsNoteExecution : BmsPlayerTestScene
{
    public enum Scenario
    {
        Duplicate,
        PmsHit,
        PmsPoor,
        Removal,
        Reactivation,
    }

    private Scenario scenario;
    private bool mixed;
    private static Action<string>? feedback;
    private static BmsColumn? measuredColumn;
    private static BmsPlayfield? measuredPlayfield;
    private static readonly BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var pms = scenario is Scenario.PmsHit or Scenario.PmsPoor;
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = pms ? BmsLayoutVariant.Pms9K : BmsLayoutVariant.Bme7K,
            TotalColumns = pms ? 9 : 8, Rank = 2, Total = 24,
            HitObjects = [new BmsNote { StartTime = 3000, Column = 1, SampleKey = 11, SampleVolume = 80 }],
        };
        if (scenario is Scenario.Duplicate or Scenario.Reactivation)
            beatmap.HitObjects.Add(new BmsNote { StartTime = 3000, Column = 1, SampleKey = 12, SampleVolume = 70 });
        if (mixed)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 20000, Column = 1 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 30000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void SamePlanMatchesFrameworkAndRecordingOutputs([Values] Scenario plan, [Values] bool nonBatchColumn, [Values] bool culled)
        => compare(plan, nonBatchColumn, culled, BmsJudgementAlgorithm.Combo);

    [TestCase(BmsJudgementAlgorithm.Duration)]
    [TestCase(BmsJudgementAlgorithm.Lowest)]
    [TestCase(BmsJudgementAlgorithm.Score)]
    [TestCase(null)]
    public void SameTimeSelectionPreservesEveryAlgorithm(BmsJudgementAlgorithm? algorithm)
        => compare(Scenario.Reactivation, true, false, algorithm);

    private void compare(Scenario plan, bool nonBatchColumn, bool culled, BmsJudgementAlgorithm? algorithm)
    {
        load(plan, nonBatchColumn);
        AddStep("compare production and recording outputs", () => runProduction(culled, algorithm));
    }

    private void load(Scenario plan, bool nonBatchColumn)
    {
        AddStep("load note output comparison chart", () => { scenario = plan; mixed = nonBatchColumn; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek before input window", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2798); });
        AddUntilStep("simulation reached start", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2798) < .001);
    }

    private void runProduction(bool culled, BmsJudgementAlgorithm? algorithm)
    {
        var previousClock = Playfield.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2798, IsRunning = true, Rate = 1.5 };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = clock;
        Player.HealthProcessor.Clock = clock;
        var probes = new List<ScopedMethodProbe>();
        var trace = new List<string>();
        DrawableBmsHitObject[] drawables = [];
        Action<DrawableHitObject, JudgementResult>? before = null;
        Action<DrawableHitObject, JudgementResult>? after = null;
        try
        {
            Playfield.EndGameplayFrame();
            Playfield.UpdateSubTree();
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var notes = Playfield.Beatmap.HitObjects.Where(n => n.Column == 1 && n is BmsNote).ToArray();
            drawables = notes.Select(n => container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => ReferenceEquals(d.HitObject, n))).ToArray();
            var results = drawables.Select(d => (BmsJudgementResult)d.Result).ToArray();
            var context = column.Judgements;
            Playfield.JudgementAlgorithm = algorithm;
            Assert.That(column.CanBatchUpdates, Is.EqualTo(!mixed));

            var output = new BmsRecordingJudgementOutput();
            var engine = new BmsJudgementEngine(notes, Playfield.LayoutVariant, 1, !mixed, output);
            var recorded = engine.GetColumn(1);
            var recordedTrace = new List<string>();
            foreach (var note in container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Select(d => d.HitObject).Where(notes.Contains))
                recorded.ActivateNote(note);
            output.Feedback = recordedTrace.Add;
            output.BeforeResultCheckpoint = (note, _) => recordedTrace.Add(snapshot("main.before", note, true));
            output.AfterResultCheckpoint = (note, _) => recordedTrace.Add(snapshot("main.after", note, true));
            before = (drawable, _) => { if (drawable is DrawableBmsHitObject d && notes.Contains(d.HitObject)) trace.Add(snapshot("main.before", d.HitObject, false)); };
            after = (drawable, _) => { if (drawable is DrawableBmsHitObject d && notes.Contains(d.HitObject)) trace.Add(snapshot("main.after", d.HitObject, false)); };
            Playfield.NewResult += before;
            foreach (var drawable in drawables)
                drawable.OnNewResult += after;

            measuredColumn = column;
            measuredPlayfield = Playfield;
            feedback = trace.Add;
            foreach (var (type, name) in new[] { (typeof(BmsColumn), "PlaySample"), (typeof(BmsColumn), "PlayKeySound"), (typeof(BmsPlayfield), "RegisterNonConsumingBad") })
                probes.Add(new ScopedMethodProbe(type.GetMethod(name, instance)!, typeof(TestSceneBmsNoteExecution).GetMethod(nameof(observeFeedback), BindingFlags.Static | BindingFlags.NonPublic)));
            // Regenerate callers so prior tests cannot hide a tiny feedback method through inlining.
            foreach (var name in new[] { "HandleNotePress" })
                probes.Add(new ScopedMethodProbe(typeof(BmsColumnJudgementContext).GetMethod(name, instance)!, typeof(TestSceneBmsNoteExecution).GetMethod(nameof(keepCallBoundary), BindingFlags.Static | BindingFlags.NonPublic)));

            if (scenario == Scenario.Reactivation)
            {
                var entries = drawables.Select(d => d.Entry!).ToArray();
                foreach (var drawable in drawables)
                    drawable.OnNewResult -= after;
                foreach (var entry in entries)
                {
                    Assert.That(container.Remove(entry), Is.True);
                    recorded.BeginNoteRemoval((BmsHitObject)entry.HitObject);
                    recorded.CheckRegisteredNotePassive((BmsHitObject)entry.HitObject, new BmsFrameContext(2798, 1.5));
                }
                foreach (var entry in entries)
                    container.Add(entry);
                container.UpdateSubTree();
                drawables = notes.Select(n => container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => ReferenceEquals(d.HitObject, n))).ToArray();
                foreach (var drawable in drawables)
                    drawable.OnNewResult += after;
                var order = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Select(d => d.HitObject).Where(notes.Contains).ToArray();
                // Registration order and alive-map enumeration are distinct inputs to the recorded plan.
                foreach (var note in notes)
                    recorded.ActivateNote(note);
                recorded.SetNoteActivationOrder(order);
            }

            if (culled)
            {
                foreach (var drawable in drawables)
                    drawable.UpdateVisualLayout(-1_000_000, new BmsColumnVisualFrame(0, 1, 0, 700));
                Assert.That(drawables.All(d => d.VisualsSuppressed && !d.IsPresent), Is.True);
            }

            mark("initial");
            var deadline = new BmsNoteJudgementWindows(notes[0]).NextPassiveJudgementTime(notes[0]);
            switch (scenario)
            {
                case Scenario.Duplicate:
                case Scenario.Reactivation:
                    press(3000, 2997);
                    press(3000, 3000);
                    passive(deadline + 1);
                    Assert.That(output.MainResults, Is.EqualTo(2));
                    Assert.That(results.All(r => r.Type == HitResult.Perfect && r.TimeOffset == 0 && r.GameplayRate == 1.5), Is.True);
                    break;

                case Scenario.PmsHit:
                case Scenario.PmsPoor:
                    press(2850, 2850);
                    press(2860, 2860);
                    Assert.That(results[0].HasResult, Is.False);
                    Assert.That(output.NonConsumingResults, Is.EqualTo(1));
                    Assert.That(recorded.IsNoteCandidate(notes[0]), Is.True);
                    if (scenario == Scenario.PmsHit)
                        press(3000, 3000);
                    passive(deadline);
                    if (scenario == Scenario.PmsPoor)
                        Assert.That(results[0].HasResult, Is.False, "the BAD deadline is inclusive");
                    passive(deadline + 1);
                    passive(deadline + 2);
                    Assert.That(output.MainResults, Is.EqualTo(1));
                    Assert.That(results[0].SuppressPenalty, Is.EqualTo(scenario == Scenario.PmsPoor));
                    break;

                case Scenario.Removal:
                    advance(deadline + 1);
                    var entry = drawables[0].Entry;
                    Assert.That(container.Remove(entry), Is.True);
                    recorded.BeginNoteRemoval(notes[0]);
                    recorded.CheckRegisteredNotePassive(notes[0], new BmsFrameContext(deadline + 1, 1.5));
                    mark("removed");
                    Assert.That(output.MainResults, Is.EqualTo(1));
                    Assert.That(results[0].Type, Is.EqualTo(HitResult.Meh));
                    Assert.That(recorded.GetNoteCandidates(), Is.Empty);
                    Assert.That(container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Any(d => notes.Contains(d.HitObject)), Is.False);
                    break;
            }

            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            var scoreTrace = JsonSerializer.Serialize(new { events = score.JudgementEvents, combo = score.Combo.Value, total = score.TotalScore.Value, health = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory });
            Assert.That(trace, Is.EqualTo(recordedTrace), "the same plan through the framework and a Drawable-free output");
            Assert.That(score.JudgementEvents, Has.Count.EqualTo(output.MainResults + output.NonConsumingResults));
            HitResult[] expectedResults = scenario switch
            {
                Scenario.Duplicate or Scenario.Reactivation => [HitResult.Perfect, HitResult.Perfect],
                Scenario.PmsHit => [HitResult.Ok, HitResult.Perfect],
                Scenario.PmsPoor => [HitResult.Ok, HitResult.Meh],
                _ => [HitResult.Meh],
            };
            Assert.That(score.JudgementEvents.Select(e => e.Result), Is.EqualTo(expectedResults));
            TestContext.WriteLine("P3A0_NOTE " + JsonSerializer.Serialize(new { scenario, mixed, culled, algorithm, rows = trace, score = scoreTrace }));

            string snapshot(string checkpoint, BmsHitObject? note, bool recording)
            {
                var candidates = recording ? recorded.GetNoteCandidates() : context.GetNoteCandidates();
                var first = recording ? recorded.FirstPendingNote : context.FirstPendingNote;
                return JsonSerializer.Serialize(new
                {
                    checkpoint, id = note == null ? -1 : Array.IndexOf(notes, note), first = first == null ? -1 : Array.IndexOf(notes, first),
                    candidates = candidates.Select(n => Array.IndexOf(notes, n)),
                    results = notes.Select((n, i) => recording ? output.GetMainResult(n) : state(results[i])),
                });
            }

            void mark(string name)
            {
                trace.Add(snapshot(name, null, false));
                recordedTrace.Add(snapshot(name, null, true));
            }

            void advance(double time) { manual.CurrentTime = time; clock.ProcessFrame(); }

            void press(double simulationTime, double inputTime)
            {
                advance(simulationTime);
                var actual = PressColumn(column, inputTime);
                var expected = recorded.HandleNotePress(new BmsPressContext(inputTime, new BmsFrameContext(simulationTime, 1.5)), algorithm);
                Assert.That(actual, Is.EqualTo(expected));
                ReleaseColumn(column, inputTime);
                recorded.HandleExecutionRelease(inputTime, new BmsFrameContext(simulationTime, 1.5));
                mark($"press:{inputTime}:{simulationTime}:{actual.Kind}");
            }

            void passive(double time)
            {
                advance(time);
                foreach (var drawable in drawables)
                {
                    context.CheckRegisteredNotePassive(drawable.HitObject, new BmsFrameContext(time, 1.5));
                    recorded.CheckRegisteredNotePassive(drawable.HitObject, new BmsFrameContext(time, 1.5));
                }
                mark($"passive:{time}");
            }
        }
        finally
        {
            feedback = null;
            measuredColumn = null;
            measuredPlayfield = null;
            for (var i = probes.Count - 1; i >= 0; i--)
                probes[i].Dispose();
            if (before != null) Playfield.NewResult -= before;
            if (after != null)
                foreach (var drawable in drawables) drawable.OnNewResult -= after;
            Playfield.Clock = previousClock;
            Playfield.ProcessCustomClock = processClock;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }

    private static BmsMainResultState state(BmsJudgementResult result)
        => new(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, result.SuppressPenalty);

    private static void keepCallBoundary()
    {
    }

    private static void observeFeedback(object __instance, MethodBase __originalMethod, object[] __args)
    {
        if (!ReferenceEquals(__instance, measuredColumn) && !ReferenceEquals(__instance, measuredPlayfield))
            return;
        feedback?.Invoke(__originalMethod.Name switch
        {
            "PlaySample" => $"sample:{__args[0]}:{__args[1]}",
            "RegisterNonConsumingBad" => $"bad:{__args[0]}:{__args[1]}",
            _ => "keysound",
        });
    }
}
