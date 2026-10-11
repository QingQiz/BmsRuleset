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
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsHellChargeNoteExecution : BmsPlayerTestScene
{
    public enum Scenario { HeadPoor, SuccessfulTail, LargeElapsed, ScratchTail }

    private Scenario scenario;
    private bool mixed;
    private bool multiple;
    private int columnIndex => scenario == Scenario.ScratchTail ? 0 : 1;
    private static readonly BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags statics = BindingFlags.NonPublic | BindingFlags.Static;
    private static Action<object, MethodBase, object[], bool>? observation;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            TotalColumns = 8, Rank = 2, Total = 24,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = columnIndex, SampleKey = 11, SampleVolume = 80, TailSampleKey = 21, TailSampleVolume = 60 }],
        };
        if (multiple) beatmap.HitObjects.Add(new BmsLongNote { StartTime = 3000, Duration = 1500, Column = columnIndex });
        if (mixed) beatmap.HitObjects.Add(new BmsLandmine { StartTime = 20000, Column = columnIndex });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 30000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void IndependentBodyMatchesFrameworkAndRecordingAcrossRealTraversal([Values] Scenario plan, [Values] bool nonBatchColumn, [Values] bool culled)
    {
        load(plan, nonBatchColumn);
        AddStep("compare HCN framework and recording outputs", () => run(culled));
    }

    private void load(Scenario plan, bool nonBatchColumn, bool multipleBodies = false)
    {
        AddStep("load HCN chart", () => { scenario = plan; mixed = nonBatchColumn; multiple = multipleBodies; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek before input window", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2798); });
        AddUntilStep("simulation ready", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2798) < .001);
    }

    private void run(bool culled)
    {
        var previousClock = Playfield.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var previousVisualOffset = Playfield.VisualOffset.Value;
        var previousMaximumVisualOffset = Playfield.VisualOffset.MaxValue;
        var manual = new ManualClock { CurrentTime = 2798, IsRunning = true, Rate = 1.25 };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = clock;
        Player.HealthProcessor.Clock = clock;
        var probes = new List<ScopedMethodProbe>();
        var trace = new List<string>();
        var recordedTrace = new List<string>();
        var visuals = new List<string>();
        var scores = new List<string>();
        var synthetic = new List<BmsLongNoteEndpointResult>();
        var ticks = new List<(bool Holding, double Scale, double Time)>();
        DrawableBmsHitObject? initialDrawable = null;
        Action<DrawableHitObject, JudgementResult>? before = null;
        Action<DrawableHitObject, JudgementResult>? after = null;
        try
        {
            Playfield.EndGameplayFrame();
            Playfield.UpdateSubTree();
            var column = (BmsColumn)Playfield.Stage.Columns[columnIndex];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var note = Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().Single();
            var registered = true;
            var submissions = (List<(double Time, BmsLongNoteJudgementResult Result)>)typeof(BmsPlayfield).GetField("syntheticResults", instance)!.GetValue(Playfield)!;
            initialDrawable = current();
            var result = (BmsLongNoteJudgementResult)initialDrawable.Result;
            var entry = container.AliveEntries.Single(e => ReferenceEquals(e.Value, initialDrawable)).Key;
            var context = column.Judgements;
            var output = new BmsRecordingJudgementOutput();
            var recorded = new BmsJudgementEngine([note], Playfield.LayoutVariant, columnIndex, !mixed, output).GetColumn(columnIndex);
            recorded.ActivateNote(note);
            output.Feedback = recordedTrace.Add;
            output.BeforeResultCheckpoint = (_, _) => recordedTrace.Add(snapshot("main.before", true));
            output.AfterResultCheckpoint = (_, _) => recordedTrace.Add(snapshot("main.after", true));
            var recordedHead = false;
            output.StatePublished = (_, state, _) =>
            {
                if (!recordedHead && state.HeadJudged && !state.TailJudged)
                {
                    recordedHead = true;
                    Assert.That(recorded.GetActiveBodies(), Is.Empty, "state publication precedes internal body membership");
                    recordedTrace.Add(snapshot("head.beforeIndexes", true));
                }
            };
            output.SyntheticEndpoint = (endpoint, exit) =>
            {
                Assert.That(recorded.GetLongNoteState(note).TailJudged, Is.False);
                Assert.That(recorded.GetActiveBodies(), Is.EqualTo(new[] { note }));
                Assert.That(recorded.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
                recordedTrace.Add(snapshot($"{endpoint.Kind}.{(exit ? "after" : "before")}", true));
            };
            output.BodyTick = (source, _, _, _) =>
            {
                Assert.That(source, Is.SameAs(note));
                Assert.That(recorded.GetActiveBodies(), Is.EqualTo(new[] { note }));
                recordedTrace.Add(snapshot("tick", true));
            };
            before = (d, _) => { if (ReferenceEquals(d, initialDrawable)) { trace.Add(snapshot("main.before", false)); scoreAt("main.before"); } };
            after = (d, _) => { if (ReferenceEquals(d, initialDrawable)) { trace.Add(snapshot("main.after", false)); scoreAt("main.after"); } };
            Playfield.NewResult += before;
            initialDrawable.OnNewResult += after;
            var actualHead = false;
            var visualState = initialDrawable.GetType().GetField("visualState", instance)!.GetValue(initialDrawable)!;
            observation = (target, method, args, exit) =>
            {
                if (ReferenceEquals(target, column) && !exit && method.Name == "PlaySample") trace.Add($"sample:{args[0]}:{args[1]}");
                if (ReferenceEquals(target, column) && !exit && method.Name == "PlayKeySound") trace.Add("keysound");
                if (ReferenceEquals(target, column.JudgementBridge) && exit && method.Name == "PublishState"
                    && args[1] is BmsLongNoteState state && state.HeadJudged && !state.TailJudged && !actualHead)
                {
                    actualHead = true;
                    Assert.That(context.GetActiveBodies(), Is.Empty);
                    trace.Add(snapshot("head.beforeIndexes", false));
                }
                if (ReferenceEquals(target, visualState) || ReferenceEquals(target, initialDrawable) && method.Name == "applyPresentation")
                    visuals.Add(JsonSerializer.Serialize(new { method = method.Name.Split('.').Last(), exit, initialDrawable.Alpha, initialDrawable.LifetimeEnd,
                        state = context.GetLongNoteState(note), type = result.Type }));
                if (ReferenceEquals(target, Playfield) && method.Name == "ApplySyntheticLongNoteEndpoint")
                {
                    var endpoint = (BmsLongNoteEndpointResult)args[0];
                    Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
                    assertRepresentation();
                    if (exit) synthetic.Add(endpoint);
                    trace.Add(snapshot($"{endpoint.Kind}.{(exit ? "after" : "before")}", false));
                    scoreAt($"{endpoint.Kind}.{(exit ? "after" : "before")}");
                }
                if (exit && method.Name == "ApplySyntheticLongNoteEndpoint" && target is BmsScoreProcessor or BmsHealthProcessor)
                {
                    var priorEndpoints = (result.HasResult ? 1 : 0) + synthetic.Count;
                    Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, Has.Count.EqualTo(priorEndpoints + 1));
                    var priorTicks = ticks.Count;
                    Assert.That(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory,
                        Has.Count.EqualTo(priorEndpoints + priorTicks + (target is BmsHealthProcessor ? 1 : 0)),
                        "each synthetic endpoint updates score before its gauge effect");
                    scoreAt(target is BmsScoreProcessor ? "endpoint.score" : "endpoint.gauge");
                }
                if (ReferenceEquals(target, Playfield) && method.Name == "ApplyHellChargeTick")
                {
                    assertRepresentation();
                    if (exit)
                    {
                        ticks.Add(((bool)args[0], (double)args[1], manual.CurrentTime));
                        trace.Add(snapshot("tick", false));
                    }
                    scoreAt(exit ? "tick.after" : "tick.before");
                }
            };
            foreach (var (type, name) in new[]
                     {
                         (typeof(BmsColumn), "PlaySample"), (typeof(BmsColumn), "PlayKeySound"),
                         (typeof(BmsLongNoteVisualState), "PrepareHeadPin"),
                         (typeof(BmsHealthProcessor), "ApplySyntheticLongNoteEndpoint"), (typeof(BmsScoreProcessor), "ApplySyntheticLongNoteEndpoint"),
                         (typeof(BmsPlayfield), "ApplySyntheticLongNoteEndpoint"), (typeof(BmsPlayfield), "ApplyHellChargeTick"),
                     })
                probes.Add(new ScopedMethodProbe(type.GetMethod(name, instance)!, GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            probes.Add(new ScopedMethodProbe(typeof(BmsJudgementBridge).GetMethod("PublishState", instance,
                [typeof(BmsLongNote), typeof(BmsLongNoteState).MakeByRefType(), typeof(BmsFrameContext).MakeByRefType()])!,
                GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            probes.Add(new ScopedMethodProbe(initialDrawable.GetType().GetMethod("applyPresentation", instance)!,
                GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            // Rebuild callers after leaf probes so earlier fixtures' JIT inlining cannot hide a synchronous observation.
            foreach (var type in new[] { typeof(BmsColumnJudgementRuntime), typeof(BmsJudgementExecutionContext), typeof(BmsHellChargeNoteJudgementRule), typeof(BmsHellChargeBodyTracker), typeof(BmsLongNotePresentation), typeof(BmsJudgementBridge) })
            {
                foreach (var method in type.GetMethods(instance | BindingFlags.Static).Where(m => m.DeclaringType == type && !m.IsSpecialName
                             && m.Name is not "PublishState" and not "onHeadJudged"))
                    probes.Add(new ScopedMethodProbe(method, GetType().GetMethod(nameof(keepCallBoundary), statics)));
            }

            if (culled)
            {
                Playfield.VisualOffset.MaxValue = 1_000_000;
                Playfield.VisualOffset.Value = 1_000_000;
                traverse(2799);
                Assert.That(current().VisualsSuppressed && !current().IsPresent, Is.True);
            }
            mark("initial");
            if (scenario == Scenario.HeadPoor)
            {
                var headPoorTime = new BmsNoteJudgementWindows(note).NextPassiveJudgementTime(note) + 1;
                traverse(headPoorTime);
                Assert.That(result.HasResult, Is.False);
                Assert.That(synthetic, Has.Count.EqualTo(2));
                Assert.That(synthetic.Select(e => e.Kind), Is.EqualTo(new[] { BmsLongNoteEndpointKind.Head, BmsLongNoteEndpointKind.Tail }));
                Assert.That(ticks, Is.Empty);
                assertBody();
                traverse(headPoorTime);
                Assert.That(ticks, Is.Empty);
                traverse(headPoorTime + 201);
                Assert.That(ticks.Single().Holding, Is.False);
                press(headPoorTime + 201, headPoorTime + 201);
                traverse(headPoorTime + 402);
                Assert.That(ticks.Select(t => t.Holding), Is.EqualTo(new[] { false }));
                traverse(headPoorTime + 403);
                Assert.That(ticks.Select(t => t.Holding), Is.EqualTo(new[] { false, true }));
            }
            else
            {
                press(3000, 3000);
                traverse(3000);
                Assert.That(ticks, Is.Empty);
                assertBody();
                if (scenario == Scenario.LargeElapsed)
                {
                    traverse(3977);
                    Assert.That(ticks, Has.Count.EqualTo(1));
                    traverse(3977);
                    Assert.That(ticks, Has.Count.EqualTo(1));
                    traverse(3978);
                    Assert.That(ticks, Has.Count.EqualTo(2));
                    traverse(3979);
                    Assert.That(ticks, Has.Count.EqualTo(3));
                    release(3980, 3980);
                }
                else
                {
                    traverse(3190);
                    traverse(3790);
                    var beforeTailTicks = ticks.Count;
                    if (scenario == Scenario.ScratchTail)
                    {
                        release(3997, 3990);
                        press(3997, 3990, true);
                        Assert.That(synthetic.Single().EventTime, Is.EqualTo(3990));
                        Assert.That(synthetic.Single().ApplicationTime, Is.EqualTo(3990));
                        Assert.That(submissions.Single().Time, Is.EqualTo(3997));
                        Assert.That(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory[^1].Time, Is.EqualTo(3990));
                        release(3997, 3997, true);
                    }
                    else
                        release(3980, 3980);
                    Assert.That(ticks, Has.Count.EqualTo(beforeTailTicks));
                }
                Assert.That(synthetic.Single().Result, Is.AnyOf(HitResult.Perfect, HitResult.Great, HitResult.Good));
                Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
                Assert.That(recorded.GetPendingLongNoteTails(), Is.Empty);
                Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
                assertBody();
                var tailTickCount = ticks.Count;
                traverse(3998);
                Assert.That(ticks.Count, Is.GreaterThan(tailTickCount), "successful early tail continues recovery after releasing physical input");
                Assert.That(ticks[^1].Holding, Is.True);
                assertBody();
            }
            var finalTicks = ticks.Count;
            traverse(4000);
            Assert.That(recorded.GetActiveBodies(), Is.Empty);
            Assert.That(context.GetActiveBodies(), Is.Empty);
            traverse(4400);
            Assert.That(container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, note)), Is.False);
            Assert.That(ticks, Has.Count.EqualTo(finalTicks));
            Assert.That(output.BodyTicks.Select(t => (t.Holding, t.Scale, t.Time)), Is.EqualTo(ticks));
            Assert.That(output.MainResults, Is.EqualTo(scenario == Scenario.HeadPoor ? 0 : 1));
            Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(scenario == Scenario.HeadPoor ? 2 : 1));
            Assert.That(scores.Count(s => s.Contains("\"checkpoint\":\"endpoint.score\"")), Is.EqualTo(synthetic.Count));
            Assert.That(scores.Count(s => s.Contains("\"checkpoint\":\"endpoint.gauge\"")), Is.EqualTo(synthetic.Count));
            TestContext.WriteLine("P3A0_HCN " + JsonSerializer.Serialize(new { scenario, mixed, culled, rows = trace, recordedRows = recordedTrace, visuals, scores }));
            Assert.That(trace, Is.EqualTo(recordedTrace), "supplied execution plan preserves body membership and synchronous outputs without a Drawable");
            DrawableBmsHitObject current() => container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => ReferenceEquals(d.HitObject, note));
            void assertRepresentation()
            {
                Assert.That(current(), Is.SameAs(initialDrawable));
                if (culled) Assert.That(current().VisualsSuppressed, Is.True);
            }
            void assertBody()
            {
                assertRepresentation();
                Assert.That(entry.LifetimeEnd, Is.GreaterThan(note.EndTime));
                Assert.That(initialDrawable.LifetimeEnd, Is.GreaterThan(note.EndTime));
                Assert.That(recorded.GetActiveBodies(), Is.EqualTo(new[] { note }));
                Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { note }));
            }
            string snapshot(string checkpoint, bool recording)
            {
                return JsonSerializer.Serialize(new
                {
                    checkpoint,
                    body = recording ? recorded.GetActiveBodies().Any() : context.GetActiveBodies().Any(),
                    holding = recording ? recorded.GetHoldingNotes().Any() : context.GetHoldingNotes().Any(),
                    first = recording ? recorded.FirstPendingNote != null : context.FirstPendingNote != null,
                    candidate = recording ? recorded.IsNoteCandidate(note) : context.IsNoteCandidate(note),
                    result = recording ? output.GetMainResult(note) : new BmsMainResultState(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, result.SuppressPenalty),
                    state = recording ? recorded.GetLongNoteState(note) : context.GetLongNoteState(note),
                    endpoints = (recording ? output.SyntheticEndpoints : synthetic).Select(e => new { e.Kind, e.EventTime, e.ApplicationTime, e.Result }),
                    applicationTimes = recording ? output.SyntheticApplicationTimes.ToArray() : submissions.Select(s => s.Time).ToArray(),
                    ticks = (recording ? output.BodyTicks.Select(t => (t.Holding, t.Scale, t.Time)) : ticks).Select(t => new { t.Holding, t.Scale, t.Time }),
                });
            }
            void scoreAt(string checkpoint) => scores.Add(JsonSerializer.Serialize(new { checkpoint,
                events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, combo = Player.ScoreProcessor.Combo.Value,
                total = Player.ScoreProcessor.TotalScore.Value, health = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory }));
            void mark(string checkpoint) { trace.Add(snapshot(checkpoint, false)); recordedTrace.Add(snapshot(checkpoint, true)); scoreAt(checkpoint); }
            void advance(double time) { manual.CurrentTime = time; clock.ProcessFrame(); }
            BmsFrameContext frame() => new(manual.CurrentTime, manual.Rate);
            void press(double simulation, double input, bool reverse = false)
            {
                advance(simulation);
                Assert.That(PressColumn(column, input, reverse), Is.EqualTo(recorded.HandleNotePress(new BmsPressContext(input, frame(), reverse))));
                mark($"press:{input}:{simulation}");
            }
            void release(double simulation, double input, bool reverse = false)
            {
                advance(simulation);
                ReleaseColumn(column, input, reverse);
                recorded.HandleExecutionRelease(input, frame(), reverse);
                mark($"release:{input}:{simulation}");
            }
            void traverse(double time)
            {
                var elapsed = time - manual.CurrentTime;
                advance(time);
                Playfield.EndGameplayFrame();
                Playfield.UpdateSubTree();
                recorded.UpdateRegisteredBodies(frame(), elapsed, recorded.PressedDirections != 0);
                if (container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, note)))
                {
                    if (culled) Assert.That(current().VisualsSuppressed, Is.True);
                    recorded.CheckRegisteredNotePassive(note, frame());
                    recorded.UpdateRegisteredLongNote(note, frame(), elapsed, recorded.PressedDirections != 0);
                }
                else if (registered)
                {
                    recorded.BeginNoteRemoval(note);
                    recorded.CheckRegisteredNotePassive(note, frame());
                    registered = false;
                }
                mark($"traverse:{time}");
            }
        }
        finally
        {
            observation = null;
            for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose();
            if (before != null) Playfield.NewResult -= before;
            if (after != null && initialDrawable != null) initialDrawable.OnNewResult -= after;
            Playfield.Clock = previousClock;
            Playfield.ProcessCustomClock = processClock;
            Playfield.VisualOffset.Value = previousVisualOffset;
            Playfield.VisualOffset.MaxValue = previousMaximumVisualOffset;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SameColumnBodiesKeepIndependentStateAndProductionVisitOrder(bool nonBatchColumn)
    {
        load(Scenario.SuccessfulTail, nonBatchColumn, true);
        AddStep("compare same-column bodies across both outputs", runMultiple);
    }

    private void runMultiple()
    {
        var previousClock = Playfield.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2798, IsRunning = true };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        var probes = new List<ScopedMethodProbe>();
        try
        {
            Playfield.EndGameplayFrame();
            Playfield.UpdateSubTree();
            var column = (BmsColumn)Playfield.Stage.Columns[columnIndex];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var notes = Playfield.Beatmap.HitObjects.OfType<BmsLongNote>().ToArray();
            var drawables = notes.Select(n => container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => ReferenceEquals(d.HitObject, n))).ToArray();
            var aliveOrder = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Select(d => Array.IndexOf(notes, d.HitObject)).Where(i => i >= 0).ToArray();
            var context = column.Judgements;
            var output = new BmsRecordingJudgementOutput();
            var recorded = new BmsJudgementEngine(notes, Playfield.LayoutVariant, columnIndex, !mixed, output).GetColumn(columnIndex);
            foreach (var note in notes) recorded.ActivateNote(note);
            recorded.SetNoteActivationOrder(aliveOrder.Select(i => notes[i]));
            context.PressedDirections = recorded.PressedDirections = 1;
            var ticks = new List<(int Object, double Time)>();
            var bodyObject = -1;
            observation = (target, method, args, exit) =>
            {
                if (!exit && ReferenceEquals(target, column.JudgementBridge) && method.Name == "ApplyBodyTick")
                    bodyObject = Array.IndexOf(notes, (BmsLongNote)args[0]);
                if (ReferenceEquals(target, Playfield) && exit && method.Name == "ApplyHellChargeTick")
                {
                    Assert.That(bodyObject, Is.GreaterThanOrEqualTo(0));
                    Assert.That(container.AliveEntries.Values, Does.Contain(drawables[bodyObject]));
                    ticks.Add((bodyObject, manual.CurrentTime));
                }
            };
            foreach (var (type, name) in new[] { (typeof(BmsPlayfield), "ApplyHellChargeTick"), (typeof(BmsJudgementBridge), "ApplyBodyTick") })
                probes.Add(new ScopedMethodProbe(type.GetMethod(name, instance)!, GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            foreach (var type in new[] { typeof(BmsHellChargeBodyTracker), typeof(BmsJudgementBridge), typeof(BmsJudgementExecutionContext), typeof(BmsColumnJudgementRuntime) })
            {
                foreach (var method in type.GetMethods(instance).Where(m => m.DeclaringType == type && !m.IsSpecialName))
                    probes.Add(new ScopedMethodProbe(method, GetType().GetMethod(nameof(keepCallBoundary), statics)));
            }

            press(1, 3000);
            traverse(3100);
            press(0, 3100);
            traverse(3201);
            Assert.That(ticks, Is.EqualTo(new[] { (1, 3201d) }), "the later head starts with a separate accumulator");
            traverse(3301);
            Assert.That(ticks, Is.EqualTo(new[] { (1, 3201d), (0, 3301d) }));
            traverse(3502);
            Assert.That(ticks.Where(t => t.Time == 3502).Select(t => t.Object), Is.EqualTo(mixed ? aliveOrder : new[] { 1, 0 }));
            traverse(4000);
            Assert.That(ticks.Where(t => t.Time == 4000).Select(t => t.Object), Is.EqualTo(new[] { 1 }), "ending the first chart object cannot skip its neighbour");
            Assert.That(recorded.GetActiveBodies(), Is.EqualTo(new[] { notes[1] }));
            Assert.That(context.GetActiveBodies(), Is.EqualTo(new[] { notes[1] }));
            Assert.That(output.BodyTicks.Select(t => (Array.IndexOf(notes, t.Note), t.Time)), Is.EqualTo(ticks));
            var trace = ticks.Select(t => $"{t.Object}:{t.Time}").ToArray();
            TestContext.WriteLine("P3A0_HCN_MULTI " + JsonSerializer.Serialize(new { mixed, aliveOrder, ticks = trace }));

            void advance(double time) { manual.CurrentTime = time; clock.ProcessFrame(); }
            BmsFrameContext frame() => new(manual.CurrentTime, manual.Rate);
            void press(int i, double time)
            {
                advance(time);
                Assert.That(context.TryPressRegisteredNote(notes[i], HitResult.Perfect, new BmsPressContext(time, frame())), Is.True);
                Assert.That(recorded.TryPressRegisteredNote(notes[i], HitResult.Perfect, new BmsPressContext(time, frame())), Is.True);
                Assert.That(context.GetLongNoteState(notes[i]).HeadTime, Is.EqualTo(time));
                Assert.That(context.GetLongNoteState(notes[1]).HeadTime, Is.EqualTo(3000), "the other body's head history is independent");
            }
            void traverse(double time)
            {
                var elapsed = time - manual.CurrentTime;
                advance(time);
                Playfield.EndGameplayFrame();
                Playfield.UpdateSubTree();
                recorded.UpdateRegisteredBodies(frame(), elapsed, true);
                foreach (var note in notes)
                {
                    recorded.CheckRegisteredNotePassive(note, frame());
                    recorded.UpdateRegisteredLongNote(note, frame(), elapsed, true);
                    Assert.That(recorded.GetLongNoteState(note), Is.EqualTo(context.GetLongNoteState(note)));
                }
            }
        }
        finally
        {
            observation = null;
            for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose();
            Playfield.Clock = previousClock;
            Playfield.ProcessCustomClock = processClock;
        }
    }

    private static void observeEnter(object __instance, MethodBase __originalMethod, object[] __args) => observation?.Invoke(__instance, __originalMethod, __args, false);
    private static void observeExit(object __instance, MethodBase __originalMethod, object[] __args) => observation?.Invoke(__instance, __originalMethod, __args, true);
    private static void keepCallBoundary() { }
}
