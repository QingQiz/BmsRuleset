#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
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
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.LnHelper;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsChargeNoteExecution : BmsPlayerTestScene
{
    public enum Scenario
    {
        Release,
        EarlyRelease,
        PassiveTail,
        PassiveHeadPoor,
        PmsExpiry,
        PmsRescue,
        ScratchReverse,
    }

    private Scenario scenario;
    private bool mixed;
    private int columnIndex => scenario == Scenario.ScratchReverse ? 0 : 1;
    private static readonly BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly BindingFlags statics = BindingFlags.NonPublic | BindingFlags.Static;
    private static Action<object, MethodBase, object[], bool>? observation;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var pms = scenario is Scenario.PmsExpiry or Scenario.PmsRescue;
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = pms ? BmsLayoutVariant.Pms9K : BmsLayoutVariant.Bme7K,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote, TotalColumns = pms ? 9 : 8, Rank = 2, Total = 24,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = columnIndex, SampleKey = 11, SampleVolume = 80, TailSampleKey = 21, TailSampleVolume = 60 }],
        };
        if (mixed)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 20000, Column = columnIndex });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 30000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void HeadResultRetainsTailAcrossRealTraversal([Values] Scenario plan, [Values] bool nonBatchColumn, [Values] bool culled)
    {
        load(plan, nonBatchColumn);
        AddStep("compare CN framework and recording outputs", () => run(culled));
    }

    private void load(Scenario plan, bool nonBatchColumn)
    {
        AddStep("load CN chart", () => { scenario = plan; mixed = nonBatchColumn; LoadPlayer(); });
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
            var actualSubmissions = (List<(double Time, BmsLongNoteJudgementResult Result)>)typeof(BmsPlayfield).GetField("syntheticResults", instance)!.GetValue(Playfield)!;
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
            var recordedHeadPublished = false;
            output.StatePublished = (_, state, _) =>
            {
                if (!recordedHeadPublished && state.HeadJudged && !state.TailJudged)
                {
                    recordedHeadPublished = true;
                    recordedTrace.Add(snapshot("head.beforeIndexes", true));
                }
            };
            output.SyntheticEndpoint = (_, exit) =>
            {
                Assert.That(recorded.GetLongNoteState(note).TailJudged, Is.False, "both sides of the synchronous endpoint callback see the old tail state");
                recordedTrace.Add(snapshot(exit ? "tail.after" : "tail.before", true));
            };
            before = (d, _) => { if (ReferenceEquals(d, initialDrawable)) { trace.Add(snapshot("main.before", false)); scoreAt("main.before"); } };
            after = (d, _) => { if (ReferenceEquals(d, initialDrawable)) { trace.Add(snapshot("main.after", false)); scoreAt("main.after"); } };
            Playfield.NewResult += before;
            initialDrawable.OnNewResult += after;
            var actualHeadPublished = false;
            var visualState = initialDrawable.GetType().GetField("visualState", instance)!.GetValue(initialDrawable)!;
            observation = (target, method, args, exit) =>
            {
                if (ReferenceEquals(target, column) && !exit && method.Name == "PlaySample")
                    trace.Add($"sample:{args[0]}:{args[1]}");
                if (ReferenceEquals(target, column) && !exit && method.Name == "PlayKeySound") trace.Add("keysound");
                if (ReferenceEquals(target, column.JudgementBridge) && exit && method.Name == "PublishState"
                    && args[1] is BmsLongNoteState state && state.HeadJudged && !state.TailJudged && !actualHeadPublished)
                {
                    actualHeadPublished = true;
                    trace.Add(snapshot("head.beforeIndexes", false));
                }
                if (ReferenceEquals(target, visualState) || ReferenceEquals(target, initialDrawable) && method.Name == "applyPresentation")
                    visuals.Add(JsonSerializer.Serialize(new { method = method.Name.Split('.').Last(), exit, initialDrawable.Alpha, initialDrawable.LifetimeEnd,
                        state = context.GetLongNoteState(note), type = result.Type }));
                if (ReferenceEquals(target, Playfield) && method.Name == "ApplySyntheticLongNoteEndpoint")
                {
                    Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
                    Assert.That(current(), Is.SameAs(initialDrawable), "synthetic feedback still borrows a live framework representation");
                    if (culled) Assert.That(current().VisualsSuppressed, Is.True, "the endpoint settles while actually culled");
                    if (exit) synthetic.Add((BmsLongNoteEndpointResult)args[0]);
                    trace.Add(snapshot(exit ? "tail.after" : "tail.before", false));
                    scoreAt(exit ? "tail.after" : "tail.before");
                }
                if (exit && method.Name == "ApplySyntheticLongNoteEndpoint" && target is BmsScoreProcessor or BmsHealthProcessor)
                {
                    Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, Has.Count.EqualTo(2));
                    Assert.That(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory, Has.Count.EqualTo(target is BmsScoreProcessor ? 1 : 2),
                        "the tail score changes before its gauge effect");
                    scoreAt(target is BmsScoreProcessor ? "tail.score" : "tail.gauge");
                }
            };
            foreach (var (type, name) in new[]
                     {
                         (typeof(BmsColumn), "PlaySample"), (typeof(BmsColumn), "PlayKeySound"),
                         (typeof(BmsLongNoteVisualState), "PrepareHeadPin"),
                         (typeof(BmsHealthProcessor), "ApplySyntheticLongNoteEndpoint"),
                         (typeof(BmsScoreProcessor), "ApplySyntheticLongNoteEndpoint"), (typeof(BmsPlayfield), "ApplySyntheticLongNoteEndpoint"),
                     })
                probes.Add(new ScopedMethodProbe(type.GetMethod(name, instance)!, GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            probes.Add(new ScopedMethodProbe(typeof(BmsJudgementBridge).GetMethod("PublishState", instance,
                [typeof(BmsLongNote), typeof(BmsLongNoteState).MakeByRefType(), typeof(BmsFrameContext).MakeByRefType()])!,
                GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            probes.Add(new ScopedMethodProbe(initialDrawable.GetType().GetMethod("applyPresentation", instance)!,
                GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            foreach (var type in new[] { typeof(BmsColumnJudgementRuntime), typeof(BmsChargeNoteJudgementRule), typeof(BmsJudgementExecutionContext) })
                foreach (var method in type.GetMethods(instance).Where(m => m.Name is "HandleNotePress" or "HandleExecutionRelease" or "PublishState" or "TryPress" or "CheckPassive" or "TryRelease"))
                    probes.Add(new ScopedMethodProbe(method, GetType().GetMethod(nameof(keepCallBoundary), statics)));

            if (culled)
            {
                // Expand only this fixture's bound configuration, so the actual layout stays outside the viewport.
                Playfield.VisualOffset.MaxValue = 1_000_000;
                Playfield.VisualOffset.Value = 1_000_000;
                advance(2799);
                Playfield.UpdateSubTree();
                Assert.That(current().VisualsSuppressed && !current().IsPresent, Is.True);
            }
            mark("initial");
            if (scenario == Scenario.PassiveHeadPoor)
            {
                var deadline = new BmsNoteJudgementWindows(note).NextPassiveJudgementTime(note);
                traverse(deadline);
                Assert.That(result.HasResult, Is.False);
                traverse(deadline + 1);
                Assert.That(result.Type, Is.EqualTo(HitResult.Meh));
                Assert.That(visuals.Any(v => v.Contains("PrepareHeadPin")), Is.False);
            }
            else
            {
                press(3003, 3000);
                Assert.That(result.HasResult, Is.True);
                Assert.That(result.EndpointResults, Has.Count.EqualTo(1));
                Assert.That(result.EndpointResults[0].Kind, Is.EqualTo(BmsLongNoteEndpointKind.Head));
                Assert.That(raw(result), Is.EqualTo(3003));
                assertPending();
                traverse(3004);
                traverse(3200);
                Assert.That(current(), Is.SameAs(initialDrawable), "the actual container retained the judged head's representation across traversal");
                Assert.That(entry.LifetimeEnd, Is.GreaterThan(note.EndTime));
                Assert.That(initialDrawable.LifetimeEnd, Is.GreaterThan(note.EndTime));
                assertPending();
                switch (scenario)
                {
                    case Scenario.Release:
                        release(3997, 3990, 1.5);
                        assertTail(3990, 3997, 1.5);
                        break;
                    case Scenario.EarlyRelease:
                        release(3407, 3400, 1.5);
                        assertTail(3400, 3407, 1.5);
                        Assert.That(synthetic.Single().Result, Is.EqualTo(HitResult.Meh));
                        break;
                    case Scenario.PassiveTail:
                        traverse(4000);
                        assertPending();
                        var deadline = note.EndTime + BmsJudgementProfileProvider.GetTable(note.Beatmap.LayoutVariant, note.Column, note.EffectiveJudgementRate, false).SlowWindowFor(HitResult.Ok);
                        traverse(deadline);
                        assertPending();
                        traverse(deadline + 1, .75);
                        assertTail(deadline + 1, deadline + 1, .75);
                        break;
                    case Scenario.PmsExpiry:
                    case Scenario.PmsRescue:
                        release(3407, 3400, 1.5);
                        Assert.That(context.GetLongNoteState(note).PendingReleaseTime, Is.EqualTo(3407));
                        traverse(3606, .75);
                        assertPending();
                        if (scenario == Scenario.PmsRescue)
                        {
                            press(3606, 3600);
                            Assert.That(context.GetLongNoteState(note).PendingReleaseTime, Is.Null);
                            traverse(3607);
                            assertPending();
                            release(4000, 4000, .75);
                            assertTail(4000, 4000, .75);
                        }
                        else
                        {
                            traverse(3607);
                            assertTail(3407, 3607, .75);
                        }
                        break;
                    case Scenario.ScratchReverse:
                        release(3997, 3990, 1.5);
                        assertPending();
                        press(3997, 3990, true);
                        // The old repress path stamps the endpoint with input time; the host still applies it at 3997.
                        assertTail(3990, 3990, 1.5);
                        break;
                }
            }
            Assert.That(synthetic, Has.Count.EqualTo(1));
            Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
            Assert.That(output.MainResults, Is.EqualTo(1));
            Assert.That(recorded.GetPendingLongNoteTails(), Is.Empty);
            Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
            // Revisit completion before actual pooling, then let the container remove the representation.
            traverse(manual.CurrentTime);
            traverse(4400);
            Assert.That(container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, note)), Is.False);
            Assert.That(synthetic, Has.Count.EqualTo(1));
            Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
            Assert.That(result.EndpointResults, Has.Count.EqualTo(1), "the synthetic tail never rewrites the framework head result");
            Assert.That(scores.Count(s => s.Contains("\"checkpoint\":\"tail.score\"")), Is.EqualTo(1));
            Assert.That(scores.Count(s => s.Contains("\"checkpoint\":\"tail.gauge\"")), Is.EqualTo(1));
            if (scenario == Scenario.ScratchReverse)
            {
                Assert.That(actualSubmissions.Single().Time, Is.EqualTo(3997));
                Assert.That(output.SyntheticApplicationTimes.Single(), Is.EqualTo(3997));
                Assert.That(((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory[^1].Time, Is.EqualTo(3990));
                rewindSubmission(3997);
                Assert.That(actualSubmissions, Has.Count.EqualTo(1));
                Assert.That(output.SyntheticEndpoints, Has.Count.EqualTo(1));
                rewindSubmission(3996);
                Assert.That(actualSubmissions, Is.Empty);
                Assert.That(output.SyntheticEndpoints, Is.Empty);
                Assert.That(result.HasResult, Is.True, "reverting the tail leaves the earlier framework head result intact");
            }
            TestContext.WriteLine("P3A0_CN " + JsonSerializer.Serialize(new { scenario, mixed, culled, rows = trace, recordedRows = recordedTrace, visuals, scores }));
            Assert.That(trace, Is.EqualTo(recordedTrace), "the supplied plan preserves synchronous head/tail observations with a data-only output");
            DrawableBmsHitObject current() => container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => ReferenceEquals(d.HitObject, note));
            string snapshot(string checkpoint, bool recording)
            {
                var holds = recording ? recorded.GetHoldingNotes().Any() : context.GetHoldingNotes().Any();
                return JsonSerializer.Serialize(new
                {
                    checkpoint, holding = holds,
                    candidate = recording ? recorded.IsNoteCandidate(note) : context.IsNoteCandidate(note),
                    first = recording ? recorded.FirstPendingNote != null : context.FirstPendingNote != null,
                    result = recording ? output.GetMainResult(note) : new BmsMainResultState(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, result.SuppressPenalty),
                    rawTime = recording ? output.GetRawTime(note) : raw(result),
                    state = recording ? recorded.GetLongNoteState(note) : context.GetLongNoteState(note),
                    mainEndpoints = (recording ? output.GetEndpoints(note) : result.EndpointResults).Select(endpoint),
                    syntheticEndpoints = (recording ? output.SyntheticEndpoints : synthetic).Select(endpoint),
                    applicationTimes = recording ? output.SyntheticApplicationTimes.ToArray() : actualSubmissions.Select(s => s.Time).ToArray(),
                });
            }
            void scoreAt(string checkpoint)
            {
                var score = (BmsScoreProcessor)Player.ScoreProcessor;
                scores.Add(JsonSerializer.Serialize(new { checkpoint, events = score.JudgementEvents, combo = score.Combo.Value, total = score.TotalScore.Value,
                    health = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory }));
            }
            void mark(string checkpoint) { trace.Add(snapshot(checkpoint, false)); recordedTrace.Add(snapshot(checkpoint, true)); scoreAt(checkpoint); }
            void advance(double time, double? rate = null) { if (rate.HasValue) manual.Rate = rate.Value; manual.CurrentTime = time; clock.ProcessFrame(); }
            BmsFrameContext frame() => new(manual.CurrentTime, manual.Rate);
            void press(double simulation, double input, bool reverse = false)
            {
                advance(simulation);
                Assert.That(PressColumn(column, input, reverse), Is.EqualTo(recorded.HandleNotePress(new BmsPressContext(input, frame(), reverse))));
                mark($"press:{input}:{simulation}");
            }
            void release(double simulation, double input, double rate)
            {
                advance(simulation, rate);
                ReleaseColumn(column, input);
                recorded.HandleExecutionRelease(input, frame());
                mark($"release:{input}:{simulation}");
            }
            void traverse(double time, double? rate = null)
            {
                advance(time, rate);
                Playfield.EndGameplayFrame();
                Playfield.UpdateSubTree();
                if (container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, note)))
                {
                    if (culled) Assert.That(current().VisualsSuppressed, Is.True);
                    recorded.CheckRegisteredNotePassive(note, frame());
                    recorded.UpdateRegisteredLongNote(note, frame(), 0, false);
                }
                else if (registered)
                {
                    // Replay the observed removal boundary, including its last passive check, not a post-result update after pooling.
                    Assert.That(recorded.GetLongNoteState(note).TailJudged, Is.True);
                    recorded.BeginNoteRemoval(note);
                    recorded.CheckRegisteredNotePassive(note, frame());
                    registered = false;
                }
                mark($"traverse:{time}");
            }
            void rewindSubmission(double time)
            {
                advance(time);
                // Exercise the real supplementary stack consumer without claiming .6's full lifecycle restoration.
                typeof(BmsPlayfield).GetMethod("Update", instance)!.Invoke(Playfield, null);
                output.RevertSyntheticAfter(time);
                if (actualSubmissions.Count == 0) synthetic.Clear();
                mark($"rewindSubmission:{time}");
            }
            void assertPending()
            {
                Assert.That(current().Judged, Is.True);
                Assert.That(context.GetLongNoteState(note).HasPendingTail, Is.True);
                Assert.That(recorded.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
                Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
                Assert.That(synthetic, Is.Empty);
            }
            void assertTail(double eventTime, double applicationTime, double rate)
            {
                var tail = synthetic.Single();
                Assert.That(tail.Kind, Is.EqualTo(BmsLongNoteEndpointKind.Tail));
                Assert.That(tail.EventTime, Is.EqualTo(eventTime));
                Assert.That(tail.ApplicationTime, Is.EqualTo(applicationTime));
                Assert.That(tail.ExpectedTime, Is.EqualTo(4000));
                Assert.That(tail.TimeOffset, Is.EqualTo(eventTime - 4000));
                Assert.That(tail.GameplayRate, Is.EqualTo(rate));
                Assert.That(raw(result), Is.EqualTo(3003));
                Assert.That(result.GameplayRate, Is.EqualTo(1.25));
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

    private static object endpoint(BmsLongNoteEndpointResult e) => new { e.Kind, e.EventTime, e.ApplicationTime, e.ExpectedTime, e.TimeOffset, e.GameplayRate, e.Result };
    private static double? raw(JudgementResult result) => (double?)typeof(JudgementResult).GetProperty("RawTime", instance)!.GetValue(result);
    private static void observeEnter(object __instance, MethodBase __originalMethod, object[] __args) => observation?.Invoke(__instance, __originalMethod, __args, false);
    private static void observeExit(object __instance, MethodBase __originalMethod, object[] __args) => observation?.Invoke(__instance, __originalMethod, __args, true);
    private static void keepCallBoundary() { }
}
