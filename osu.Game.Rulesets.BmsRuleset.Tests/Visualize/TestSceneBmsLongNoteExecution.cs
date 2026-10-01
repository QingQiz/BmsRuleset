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
using osu.Game.Rulesets.BmsRuleset.Configuration;
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
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsLongNoteExecution : BmsPlayerTestScene
{
    public enum Scenario
    {
        Release,
        Automatic,
        PmsExpiry,
        PmsRescue,
        PassivePoor,
        OverlapNote,
        OverlapLongNote,
    }

    private Scenario scenario;
    private bool mixed;
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
            LockedLongNoteMode = BmsLongNoteMode.LongNote,
            TotalColumns = pms ? 9 : 8, Rank = 2, Total = 24,
            HitObjects = [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1, SampleKey = 11, SampleVolume = 80, TailSampleKey = 21, TailSampleVolume = 60 }],
        };
        if (scenario == Scenario.OverlapNote)
            beatmap.HitObjects.Add(new BmsNote { StartTime = 3050, Column = 1, SampleKey = 12 });
        if (scenario == Scenario.OverlapLongNote)
            beatmap.HitObjects.Add(new BmsLongNote { StartTime = 3050, Duration = 1100, Column = 1, SampleKey = 12 });
        if (mixed)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 20000, Column = 1 });
        beatmap.HitObjects.Add(new BmsNote { StartTime = 30000, Column = 2 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void SamePlanMatchesFrameworkAndRecordingOutputs([Values] Scenario plan, [Values] bool nonBatchColumn, [Values] bool culled)
        => compare(plan, nonBatchColumn, culled, BmsJudgementAlgorithm.Combo);

    [TestCase(Scenario.OverlapNote, BmsJudgementAlgorithm.Lowest)]
    [TestCase(Scenario.OverlapNote, null)]
    [TestCase(Scenario.OverlapLongNote, BmsJudgementAlgorithm.Lowest)]
    [TestCase(Scenario.OverlapLongNote, null)]
    public void UncommittedHeadKeepsOverlapSelection(Scenario plan, BmsJudgementAlgorithm? algorithm)
        => compare(plan, false, false, algorithm);

    private void compare(Scenario plan, bool nonBatchColumn, bool culled, BmsJudgementAlgorithm? algorithm)
    {
        load(plan, nonBatchColumn);
        AddStep("compare LN framework and recording outputs", () => run(culled, algorithm));
    }

    private void load(Scenario plan, bool nonBatchColumn)
    {
        AddStep("load LN chart", () => { scenario = plan; mixed = nonBatchColumn; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek before input window", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2798); });
        AddUntilStep("simulation ready", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2798) < .001);
    }

    private void run(bool culled, BmsJudgementAlgorithm? algorithm)
    {
        var previousClock = Playfield.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2798, IsRunning = true, Rate = 1.25 };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = clock;
        Player.HealthProcessor.Clock = clock;
        var probes = new List<ScopedMethodProbe>();
        var trace = new List<string>();
        var visuals = new List<string>();
        DrawableBmsHitObject[] drawables = [];
        Action<DrawableHitObject, JudgementResult>? before = null;
        Action<DrawableHitObject, JudgementResult>? after = null;
        Action<JudgementResult>? reverting = null;
        Action<DrawableHitObject, JudgementResult>? drawableReverting = null;
        try
        {
            Playfield.EndGameplayFrame();
            Playfield.UpdateSubTree();
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var notes = Playfield.Beatmap.HitObjects.Where(n => n.Column == 1 && n is BmsNote or BmsLongNote).ToArray();
            var longNote = (BmsLongNote)notes[0];
            drawables = notes.Select(n => container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => ReferenceEquals(d.HitObject, n))).ToArray();
            var results = drawables.Select(d => (BmsJudgementResult)d.Result).ToArray();
            var context = column.Judgements;
            Playfield.JudgementAlgorithm = algorithm;
            Assert.That(column.CanBatchUpdates, Is.EqualTo(!mixed));

            var output = new BmsRecordingJudgementOutput();
            var recorded = new BmsJudgementEngine(notes, Playfield.LayoutVariant, 1, !mixed, output).GetColumn(1);
            var recordedTrace = new List<string>();
            foreach (var note in container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Select(d => d.HitObject).Where(notes.Contains))
                recorded.ActivateNote(note);
            output.Feedback = recordedTrace.Add;
            output.BeforeResultCheckpoint = (note, _) => recordedTrace.Add(snapshot("main.before", note, true));
            output.AfterResultCheckpoint = (note, _) => recordedTrace.Add(snapshot("main.after", note, true));
            var publishedHeads = new HashSet<BmsLongNote>();
            output.StatePublished = (note, state, _) =>
            {
                if (state.HeadJudged && !state.TailJudged && publishedHeads.Add(note))
                    recordedTrace.Add(snapshot("head.beforeIndexes", note, true));
            };
            output.Reverting = note =>
            {
                recordedTrace.Add(snapshot("revert.external", note, true));
                recordedTrace.Add(snapshot("revert.drawable", note, true));
            };
            output.AfterReset = note => recordedTrace.Add(snapshot("revert.reset", note, true));
            before = (drawable, _) => { if (drawable is DrawableBmsHitObject d && notes.Contains(d.HitObject)) trace.Add(snapshot("main.before", d.HitObject, false)); };
            after = (drawable, _) => { if (drawable is DrawableBmsHitObject d && notes.Contains(d.HitObject)) trace.Add(snapshot("main.after", d.HitObject, false)); };
            reverting = result => { var index = Array.IndexOf(results, result); if (index >= 0) trace.Add(snapshot("revert.external", notes[index], false)); };
            drawableReverting = (drawable, _) => { if (drawable is DrawableBmsHitObject d && notes.Contains(d.HitObject)) trace.Add(snapshot("revert.drawable", d.HitObject, false)); };
            Playfield.NewResult += before;
            Playfield.RevertResult += reverting;
            foreach (var drawable in drawables)
            {
                drawable.OnNewResult += after;
                drawable.OnRevertResult += drawableReverting;
            }

            var actualPublishedHeads = new HashSet<BmsLongNote>();
            var visualOwners = drawables.Where(d => d.HitObject is BmsLongNote)
                .ToDictionary(d => d.GetType().GetField("visualState", instance)!.GetValue(d)!, d => d);
            observation = (target, method, args, exit) =>
            {
                if (ReferenceEquals(target, column) && !exit && method.Name == "PlaySample")
                    trace.Add($"sample:{args[0]}:{args[1]}");
                if (ReferenceEquals(target, column) && !exit && method.Name == "PlayKeySound")
                    trace.Add("keysound");
                if (ReferenceEquals(target, column.JudgementBridge) && exit && method.Name == "PublishState"
                    && args[0] is BmsLongNote note && notes.Contains(note) && args[1] is BmsLongNoteState state
                    && state.HeadJudged && !state.TailJudged && actualPublishedHeads.Add(note))
                    trace.Add(snapshot("head.beforeIndexes", note, false));
                var visual = target as DrawableBmsHitObject ?? visualOwners.GetValueOrDefault(target);
                if (visual != null && drawables.Contains(visual) && visual.HitObject is BmsLongNote visualNote)
                    visuals.Add(JsonSerializer.Serialize(new { method = method.Name, exit, visual.Alpha, visual.LifetimeEnd,
                        state = context.GetLongNoteState(visualNote), type = visual.Result.Type }));
                if (exit && method.Name == "revertResult" && args[0] is HitObjectLifetimeEntry entry && notes.Contains((BmsHitObject)entry.HitObject))
                    trace.Add(snapshot("revert.reset", (BmsHitObject)entry.HitObject, false));
            };
            foreach (var (type, name) in new[] { (typeof(BmsColumn), "PlaySample"), (typeof(BmsColumn), "PlayKeySound"),
                         (typeof(BmsLongNoteVisualState), "PrepareHeadPin"), (typeof(Playfield), "revertResult") })
                probes.Add(new ScopedMethodProbe(type.GetMethod(name, instance)!, GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            probes.Add(new ScopedMethodProbe(typeof(BmsJudgementBridge).GetMethod("PublishState", instance,
                [typeof(BmsLongNote), typeof(BmsLongNoteState).MakeByRefType(), typeof(BmsFrameContext).MakeByRefType()])!,
                GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            foreach (var type in drawables.Where(d => d.HitObject is BmsLongNote).Select(d => d.GetType()).Distinct())
                probes.Add(new ScopedMethodProbe(type.GetMethod("applyPresentation", instance)!, GetType().GetMethod(nameof(observeEnter), statics), GetType().GetMethod(nameof(observeExit), statics)));
            // Keep synchronous feedback visible after earlier fixtures warm the production rule callers.
            foreach (var type in new[] { typeof(BmsColumnJudgementRuntime), typeof(BmsLongNoteJudgementRule), typeof(BmsJudgementExecutionContext) })
                foreach (var method in type.GetMethods(instance).Where(m => m.Name is "HandleNotePress" or "HandleExecutionRelease" or "PublishState" or "TryPress" or "CheckPassive" or "TryRelease"))
                    probes.Add(new ScopedMethodProbe(method, GetType().GetMethod(nameof(keepCallBoundary), statics)));

            if (culled)
            {
                foreach (var drawable in drawables)
                    drawable.UpdateVisualLayout(-1_000_000, new BmsColumnVisualFrame(-1_000_000, 1, 0, 700));
                Assert.That(drawables.All(d => d.VisualsSuppressed && !d.IsPresent), Is.True);
            }

            mark("initial");
            if (scenario == Scenario.PassivePoor)
            {
                var deadline = new BmsNoteJudgementWindows(longNote).NextPassiveJudgementTime(longNote);
                passive(deadline);
                Assert.That(results[0].HasResult, Is.False);
                passive(deadline + 1);
                Assert.That(results[0].Type, Is.EqualTo(HitResult.Meh));
                Assert.That(visuals.Any(v => v.Contains("PrepareHeadPin")), Is.False);
            }
            else
            {
                press(3003, 3000);
                Assert.That(results[0].HasResult, Is.False);
                Assert.That(context.GetLongNoteState(longNote).IsHolding, Is.True);
                Assert.That(recorded.GetHoldingNotes(), Is.EqualTo(new[] { longNote }));
                Assert.That(recorded.GetNoteCandidates(), Does.Contain(longNote));
                Assert.That(context.FirstPendingNote, Is.Null);
                Assert.That(visuals.Count(v => v.Contains("PrepareHeadPin") && v.Contains("\"exit\":true")), Is.EqualTo(1));
                switch (scenario)
                {
                    case Scenario.Release:
                        release(3997, 3990, 1.5);
                        assertTimes(3990, 3997, 1.5);
                        break;
                    case Scenario.Automatic:
                        passive(4000);
                        assertTimes(4003, 4000, 1.25);
                        break;
                    case Scenario.PmsExpiry:
                    case Scenario.PmsRescue:
                        release(3407, 3400, 1.5);
                        Assert.That(context.GetLongNoteState(longNote).PendingReleaseTime, Is.EqualTo(3407));
                        passive(3606, .75);
                        Assert.That(results[0].HasResult, Is.False);
                        if (scenario == Scenario.PmsRescue)
                        {
                            press(3606, 3600);
                            Assert.That(context.GetLongNoteState(longNote).PendingReleaseTime, Is.Null);
                            passive(3607);
                            Assert.That(results[0].HasResult, Is.False);
                            release(4000, 4000, .75);
                            assertTimes(4000, 4000, .75);
                        }
                        else
                        {
                            passive(3607, .75);
                            assertTimes(3407, 3607, .75);
                            Assert.That(results[0].Type, Is.EqualTo(HitResult.Ok));
                            rewind(3607);
                            Assert.That(results[0].HasResult, Is.True, "the exact application time retains the result");
                            rewind(3606);
                            Assert.That(results[0].HasResult, Is.False);
                            Assert.That(raw(results[0]), Is.Null);
                            Assert.That(results[0].GameplayRate, Is.EqualTo(.75));
                            Assert.That(((BmsLongNoteJudgementResult)results[0]).EndpointResults, Has.Count.EqualTo(2));
                            Assert.That(context.GetLongNoteState(longNote).PendingReleaseTime, Is.EqualTo(3407));
                            Assert.That(recorded.GetHoldingNotes(), Is.EqualTo(new[] { longNote }));
                            passive(3607, .75);
                            assertTimes(3407, 3607, .75);
                            Assert.That(drawables[0].Alpha, Is.Zero, "a replayed early result still clears the body immediately");
                        }
                        break;
                    case Scenario.OverlapNote:
                    case Scenario.OverlapLongNote:
                        press(3050, 3050);
                        Assert.That(results.All(r => !r.HasResult), Is.True, "the held LN consumes repress before overlapping heads");
                        Assert.That(recorded.FirstPendingNote, Is.Null, "an uncommitted hold disables the exact-time head shortcut");
                        release(3100, 3100, 1.5);
                        press(3101, 3050);
                        Assert.That(scenario == Scenario.OverlapNote ? results[1].HasResult : context.GetLongNoteState((BmsLongNote)notes[1]).HeadJudged, Is.True);
                        break;
                }
            }

            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            var scoreTrace = JsonSerializer.Serialize(new { events = score.JudgementEvents, combo = score.Combo.Value, total = score.TotalScore.Value,
                health = ((BmsHealthProcessor)Player.HealthProcessor).GaugeHistory });
            TestContext.WriteLine("P3A0_LN " + JsonSerializer.Serialize(new { scenario, mixed, culled, algorithm, rows = trace, recordedRows = recordedTrace, visuals, score = scoreTrace }));
            Assert.That(trace, Is.EqualTo(recordedTrace), "framework output and Drawable-free output share the plan and synchronous observations");
            string snapshot(string checkpoint, BmsHitObject? note, bool recording)
            {
                var candidates = recording ? recorded.GetNoteCandidates() : context.GetNoteCandidates();
                var first = recording ? recorded.FirstPendingNote : context.FirstPendingNote;
                var holds = recording ? recorded.GetHoldingNotes() : context.GetHoldingNotes();
                return JsonSerializer.Serialize(new
                {
                    checkpoint, id = note == null ? -1 : Array.IndexOf(notes, note), first = first == null ? -1 : Array.IndexOf(notes, first),
                    candidates = candidates.Select(n => Array.IndexOf(notes, n)), holding = holds.Select(n => Array.IndexOf(notes, n)),
                    results = notes.Select((n, i) => recording ? output.GetMainResult(n) : main(results[i])),
                    rawTimes = notes.Select((n, i) => recording ? output.GetRawTime(n) : raw(results[i])),
                    states = notes.OfType<BmsLongNote>().Select(n => recording ? recorded.GetLongNoteState(n) : context.GetLongNoteState(n)),
                    endpoints = notes.OfType<BmsLongNote>().Select(n => (recording ? output.GetEndpoints(n) : ((BmsLongNoteJudgementResult)results[Array.IndexOf(notes, n)]).EndpointResults)
                        .Select(e => new { e.Kind, e.EventTime, e.ApplicationTime, e.ExpectedTime, e.TimeOffset, e.GameplayRate, e.Result })),
                });
            }

            void mark(string name) { trace.Add(snapshot(name, null, false)); recordedTrace.Add(snapshot(name, null, true)); }
            void advance(double time, double? rate = null) { if (rate.HasValue) manual.Rate = rate.Value; manual.CurrentTime = time; clock.ProcessFrame(); }
            BmsFrameContext frame() => new(manual.CurrentTime, manual.Rate);
            void press(double simulation, double input)
            {
                advance(simulation);
                var actual = PressColumn(column, input);
                var expected = recorded.HandleNotePress(new BmsPressContext(input, frame()), algorithm);
                Assert.That(actual, Is.EqualTo(expected));
                mark($"press:{input}:{simulation}:{actual.Kind}");
            }
            void release(double simulation, double input, double rate)
            {
                advance(simulation, rate);
                ReleaseColumn(column, input);
                recorded.HandleExecutionRelease(input, frame());
                mark($"release:{input}:{simulation}");
            }
            void passive(double time, double? rate = null)
            {
                advance(time, rate);
                foreach (var drawable in drawables)
                {
                    // The supplied plan fixes the production passive entry without adding a visual traversal.
                    context.CheckRegisteredNotePassive(drawable.HitObject, frame());
                    recorded.CheckRegisteredNotePassive(drawable.HitObject, frame());
                }
                mark($"passive:{time}");
            }
            void rewind(double time)
            {
                advance(time);
                // This is the real framework stack consumer; full traversal/lifecycle generation belongs to .6/.7.
                typeof(BmsColumn).GetMethod("Update", instance)!.Invoke(column, null);
                output.RevertAfter(time);
                context.RewindLongNotes(time);
                recorded.RewindLongNotes(time);
                foreach (var drawable in drawables) drawable.RestoreRewoundVisuals();
                context.RestoreExecutionState(frame());
                recorded.RewindPressHistory(time);
                recorded.RestoreExecutionState(frame());
                mark($"rewind:{time}");
            }
            void assertTimes(double eventTime, double applicationTime, double rate)
            {
                var endpoints = ((BmsLongNoteJudgementResult)results[0]).EndpointResults;
                Assert.That(endpoints, Has.Count.EqualTo(2));
                Assert.That(endpoints[0].EventTime, Is.EqualTo(3003));
                Assert.That(endpoints[0].GameplayRate, Is.EqualTo(1.25));
                Assert.That(endpoints[1].EventTime, Is.EqualTo(eventTime));
                Assert.That(endpoints[1].ApplicationTime, Is.EqualTo(applicationTime));
                Assert.That(endpoints[1].ExpectedTime, Is.EqualTo(4000));
                Assert.That(endpoints[1].TimeOffset, Is.EqualTo(eventTime - 4000));
                Assert.That(endpoints[1].GameplayRate, Is.EqualTo(rate));
                Assert.That(raw(results[0]), Is.EqualTo(applicationTime));
                Assert.That(results[0].TimeOffset, Is.EqualTo(applicationTime - 4000));
                Assert.That(results[0].GameplayRate, Is.EqualTo(rate));
            }
        }
        finally
        {
            observation = null;
            for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose();
            if (before != null) Playfield.NewResult -= before;
            if (reverting != null) Playfield.RevertResult -= reverting;
            foreach (var drawable in drawables)
            {
                if (after != null) drawable.OnNewResult -= after;
                if (drawableReverting != null) drawable.OnRevertResult -= drawableReverting;
            }
            Playfield.Clock = previousClock;
            Playfield.ProcessCustomClock = processClock;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }

    private static BmsMainResultState main(BmsJudgementResult result)
        => new(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, result.SuppressPenalty);
    private static double? raw(JudgementResult result) => (double?)typeof(JudgementResult).GetProperty("RawTime", instance)!.GetValue(result);
    private static void observeEnter(object __instance, MethodBase __originalMethod, object[] __args) => observation?.Invoke(__instance, __originalMethod, __args, false);
    private static void observeExit(object __instance, MethodBase __originalMethod, object[] __args) => observation?.Invoke(__instance, __originalMethod, __args, true);
    private static void keepCallBoundary() { }
}
