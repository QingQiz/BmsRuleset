#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.StateChanges;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
public partial class TestSceneBmsCoreSchedulingGeneration : BmsPlayerTestScene
{
    private const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private bool replay;
    private bool mixed;
    private BmsCoreSchedulingRecorder? recorder;
    private BmsInputManager inputManager = null!;
    private static FrameStabilityContainer? budgetHost;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(replay ? _ => replayFrames() : null);

    private static IList<ReplayFrame> replayFrames() =>
    [
        new BmsReplayFrame(0),
        new BmsReplayFrame(3000, BmsAction.Scratch, BmsAction.Key1),
        new BmsReplayFrame(3001, BmsAction.Scratch),
        new BmsReplayFrame(3002, BmsAction.Scratch, BmsAction.Key1),
        new BmsReplayFrame(3201, BmsAction.Scratch),
        new BmsReplayFrame(3202, BmsAction.Scratch, BmsAction.Key1),
        new BmsReplayFrame(3990, BmsAction.Scratch, BmsAction.ScratchReverse, BmsAction.Key1),
        new BmsReplayFrame(3991, BmsAction.ScratchReverse, BmsAction.Key1),
        new BmsReplayFrame(4001, BmsAction.ScratchReverse),
        new BmsReplayFrame(4002), new BmsReplayFrame(60000),
    ];

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 24,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects =
            [
                new BmsLongNote { StartTime = 3000, Duration = 1101, Column = 0, SampleKey = 11, TailSampleKey = 12 },
                new BmsNote { StartTime = 3000, Column = 1, SampleKey = 13 },
                new BmsLongNote { StartTime = 3002, Duration = 1000, Column = 1, SampleKey = 14, TailSampleKey = 15 },
                new BmsNote { StartTime = 3030, Column = 1, SampleKey = 16 },
                new BmsNote { StartTime = 3400, Column = 1, SampleKey = 17 },
                new BmsNote { StartTime = 3400, Column = 1, SampleKey = 18 },
                new BmsNote { StartTime = 9000, Column = 1 },
                new BmsNote { StartTime = 30000, Column = 2 },
            ],
        };
        if (mixed) beatmap.HitObjects.Add(new BmsLandmine { StartTime = 3201, Column = 1, LandmineDamagePercent = 10 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(n => n.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void OrdinaryKeyboardGeneratesCoreCheckpoints(bool mixedStages, bool deferred)
    {
        load(false, mixedStages);
        AddStep("capture ordinary keyboard core scheduling", () => withClock((advance, keyboard) =>
        {
            startRecording();
            if (deferred) Playfield.BeginGameplayFrame();
            step(2999);
            step(3000, [new(Key.LShift, true), new(Key.Z, true)]);
            step(3001, [new(Key.Z, false)]);
            step(3002, [new(Key.Z, true)]);
            step(3200);
            step(3201, [new(Key.Z, false)]);
            step(3202, [new(Key.Z, true)]);
            step(3990, [new(Key.LControl, true)]);
            step(3991, [new(Key.LShift, false)]);
            step(4001, [new(Key.Z, false)]);
            step(4002, [new(Key.LControl, false)]);
            if (deferred) Playfield.EndGameplayFrame();
            recorder!.Mark("burst.complete");
            Assert.That(inputManager.ReplayInputHandler, Is.Null);
            Assert.That(keyboard.Polls, Is.GreaterThanOrEqualTo(11));
            Assert.That(recorder.Rows.Count(r => r.Split('|')[4].EndsWith("/input.queue>")), Is.EqualTo(10));
            Assert.That(recorder.Rows.Any(r => r.Contains("reverseScratch=True")), Is.True);
            Assert.That(recorder.Rows.Any(r => r.Contains("/body.tick>")), Is.True);
            finish($"keyboard-{mixedStages}-{deferred}");

            void step(double time, params ButtonInputEntry<Key>[] keys)
            {
                advance(time);
                if (keys.Length != 0) keyboard.Inputs.Add(new KeyboardKeyInput(keys));
                inputManager.UpdateSubTree();
            }
        }));
    }

    [Test]
    public void OrdinaryKeyboardLifecycleAndSameTimestampBatches()
    {
        load(false, false);
        AddStep("capture same-time inputs and real lifecycle", () => withClock((advance, keyboard) =>
        {
            startRecording();
            advance(3000);
            keyboard.Inputs.Add(new KeyboardKeyInput([new(Key.LShift, true), new(Key.Z, true)]));
            inputManager.UpdateSubTree();
            var note = (DrawableBmsHitObject)Playfield.Stage.Columns[0].HitObjectContainer.AliveEntries.Values.Single();
            var oldGeneration = note.BindingGeneration;
            recorder!.Mark("defaults.before");
            note.HitObject.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            recorder.Mark("defaults.after");
            Assert.That(note.BindingGeneration, Is.EqualTo(oldGeneration + 1));
            var visuals = (System.Collections.IDictionary)typeof(BmsColumnHitObjectContainer).GetField("visualObservations", instance)!
                .GetValue(Playfield.Stage.Columns[0].HitObjectContainer)!;
            Assert.That(visuals.Count, Is.EqualTo(1), "Reapply replaces the visual registration.");
            var binding = visuals[note]!;
            Assert.That(binding.GetType().GetProperty("Generation")!.GetValue(binding), Is.EqualTo(note.BindingGeneration));
            // The handler only creates physical transitions. The input manager and playfield choose all phases.
            foreach (var pressed in new[] { false, true, false })
            {
                keyboard.Inputs.Add(new KeyboardKeyInput([new(Key.Z, pressed)]));
                inputManager.UpdateSubTree();
            }
            var sameTime = recorder.Rows.Select(r => r.Split('|')).Where(r => r[5] == "3000").ToArray();
            Assert.That(sameTime.Count(r => r[4].EndsWith("/input.queue>")), Is.EqualTo(5));
            Assert.That(sameTime.Count(r => r[4].EndsWith("/long.finish>") && r[8] == "0"), Is.EqualTo(2),
                "RA-1 reapply resets the per-binding post-result checkpoint; subsequent same-time batches do not repeat it.");
            advance(6000);
            inputManager.UpdateSubTree();
            recorder.Mark("removed");
            Assert.That(visuals.Count, Is.Zero, "Pool return releases visual observations.");
            advance(2798);
            inputManager.UpdateSubTree();
            recorder.Mark("rewound");
            Assert.That(visuals.Count, Is.EqualTo(1), "Reactivation registers the current representation.");
            advance(3000);
            keyboard.Inputs.Add(new KeyboardKeyInput([new(Key.Z, true)]));
            inputManager.UpdateSubTree();
            recorder.Mark("reactivated");
            Assert.That(recorder.Rows.Any(r => r.Contains("/representation.killed>")), Is.True);
            Assert.That(recorder.Rows.Any(r => r.Contains("/result.revert<")), Is.True);
            finish("keyboard-lifecycle");
        }));
    }

    [Test]
    public void ParentKeyboardDispatchFlushesBeforeTheSimulationClockAdvances()
    {
        load(false, false);
        AddStep("observe pending physical input at the parent-frame boundary", () => withClock((advance, keyboard) =>
        {
            startRecording();
            advance(3000);
            // This is the same state-change dispatch that InputManager.Update performs before visiting children.
            new KeyboardKeyInput([new(Key.Z, true)]).Apply(inputManager.CurrentState, inputManager);
            Playfield.BeginGameplayFrame();
            advance(3001);
            keyboard.Inputs.Add(new KeyboardKeyInput([new(Key.Z, false)]));
            inputManager.UpdateSubTree();
            keyboard.Inputs.Add(new KeyboardKeyInput([new(Key.Z, true)]));
            inputManager.UpdateSubTree();
            Playfield.EndGameplayFrame();
            var rows = recorder!.Rows.Select(r => r.Split('|')).ToArray();
            Assert.That(rows.Single(r => r[4] == "frame.begin/batch>")[5], Is.EqualTo("3000"));
            Assert.That(rows.Count(r => r[4] == "simulation/batch>" && r[5] == "3001"), Is.EqualTo(2));
            finish("parent-keyboard");
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OrdinaryReplayUsesFrameStabilityCatchupAndRewind(bool mixedStages)
    {
        load(true, mixedStages);
        AddStep("record actual replay catchup and rewind synchronously", () =>
        {
            budgetHost = Player.DrawableRuleset.ChildrenOfType<FrameStabilityContainer>().Single();
            using var budget = new ScopedMethodProbe(typeof(FrameStabilityContainer).GetMethod("updateClock", instance)!,
                typeof(TestSceneBmsCoreSchedulingGeneration).GetMethod(nameof(fixWallClockBudget), BindingFlags.Static | BindingFlags.NonPublic)!);
            startRecording();
            Player.GameplayClockContainer.Seek(4003);
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(4003).Within(.001));
            Assert.That(inputManager.ReplayInputHandler, Is.InstanceOf<BmsFramedReplayInputHandler>());
            var inputRows = recorder!.Rows.Select(r => r.Split('|')).Where(r => r[4].EndsWith("/input.queue>")).ToArray();
            Assert.That(inputRows.Select(r => double.Parse(r[5], System.Globalization.CultureInfo.InvariantCulture)),
                Is.EqualTo(new[] { 3000d, 3000, 3001, 3002, 3201, 3202, 3990, 3991, 4001, 4002 }));
            Assert.That(inputRows.Select(r => r[2]).Distinct().Count(), Is.GreaterThan(1));
            TestContext.WriteLine($"P3A07_REAL_REPLAY time={Player.DrawableRuleset.FrameStableClock.CurrentTime};inputTimes={string.Join(',', inputRows.Select(r => r[5]))};polls={recorder.ReplayPolls};stability={recorder.StabilityUpdates}");
            Assert.That(recorder.ReplayPolls, Is.GreaterThan(0));
            Assert.That(recorder.StabilityUpdates, Is.GreaterThan(0));
            recorder.Mark("forward.complete");
            Player.GameplayClockContainer.Seek(2998);
            Player.DrawableRuleset.UpdateSubTree();
            Assert.That(Player.DrawableRuleset.FrameStableClock.CurrentTime, Is.EqualTo(2998).Within(.001));
            recorder!.Recording = false;
            Assert.That(recorder.Rows.Any(r => r.Contains("/result.externalRevert=")), Is.True);
            finish($"replay-{mixedStages}");
            stopRecording();
            budgetHost = null;
        });
    }

    [Test]
    public void OrdinaryReplayWithRealWallClockBudgetPreservesEffects()
    {
        load(true, false);
        AddStep("observe unmodified catch-up budget", () => { startRecording(); Player.GameplayClockContainer.Seek(4003); });
        AddUntilStep("replay reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4003) < .001);
        AddStep("compare full ordinary replay effects", () =>
        {
            recorder!.Recording = false;
            var rows = recorder.Rows.TakeWhile(r => !r.Contains("/forward.complete=")).ToArray();
            using var stream = GetType().Assembly.GetManifestResourceStream("osu.Game.Rulesets.BmsRuleset.Tests.reference_fixtures.judgement_p3a07_core.json")!;
            using var fixture = JsonDocument.Parse(stream);
            var expected = fixture.RootElement.GetProperty("traces").GetProperty("replay-False").EnumerateArray().Select(r => r.GetString()!)
                .TakeWhile(r => !r.Contains("/forward.complete="));
            Assert.That(semanticRows(rows), Is.EqualTo(semanticRows(expected)));
            TestContext.WriteLine("P3A07_REPLAY_SMOKE " + JsonSerializer.Serialize(semanticRows(rows)));
            Assert.That(recorder.ReplayPolls, Is.GreaterThan(0));
            Assert.That(recorder.StabilityUpdates, Is.GreaterThan(0));
            stopRecording();
        });
    }

    private static void fixWallClockBudget(FrameStabilityContainer __instance)
    {
        // CPU speed may split the same replay seek across outer frames. Keep that test-host budget
        // deterministic while the real stability clock and replay handler still choose every slice.
        if (ReferenceEquals(__instance, budgetHost))
            ((Stopwatch)typeof(FrameStabilityContainer).GetField("stopwatch", instance)!.GetValue(__instance)!).Reset();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RepeatedGeometryAndSkinTraversalDoesNotGenerateBusinessOperations(bool afterRewind)
    {
        load(false, false);
        AddStep("hold through a body checkpoint then refresh only visuals", () => withClock((advance, keyboard) =>
        {
            advance(3000);
            keyboard.Inputs.Add(new KeyboardKeyInput([new(Key.LShift, true)]));
            inputManager.UpdateSubTree();
            advance(3201);
            inputManager.UpdateSubTree();
            if (afterRewind)
            {
                advance(2999);
                inputManager.UpdateSubTree();
                Assert.That(Playfield.Time.Elapsed, Is.LessThan(0));
            }
            Playfield.EndGameplayFrame();
            startRecording();
            var score = (BmsScoreProcessor)Player.ScoreProcessor;
            var health = (BmsHealthProcessor)Player.HealthProcessor;
            var before = JsonSerializer.Serialize(new { score.JudgementEvents, health.GaugeHistory });
            var schedule = ((BmsColumn)Playfield.Stage.Columns[0]).Judgements.Schedule!;
            var identity = (schedule.Epoch, schedule.Step, schedule.Batch);
            for (var iteration = 0; iteration < 5; iteration++)
            {
                foreach (var column in Playfield.Stage.Columns)
                {
                    column.HitObjectContainer.UpdateSubTree();
                    foreach (var drawable in column.HitObjectContainer.AliveEntries.Values.OfType<DrawableBmsHitObject>())
                    {
                        drawable.ApplyNoteHeightScale(iteration % 2 == 0 ? 1.1f : 1);
                        drawable.UpdateVisualLayout(-100, new BmsColumnVisualFrame(0, 1, 0, 1000));
                        drawable.UpdateSubTree();
                        drawable.UpdateSubTreeMasking();
                    }
                }
                Playfield.EndGameplayFrame();
            }
            Assert.That(recorder!.Rows.Any(r => r.Split('|')[4].Split('/')[^1] is "note.passive>" or "long.passive>" or "long.finish>" or "body.sample>" or "body.tick>"
                or "history.rewind>" or "index.rebuild>" or "result.revert>"), Is.False);
            Assert.That((schedule.Epoch, schedule.Step, schedule.Batch), Is.EqualTo(identity));
            Assert.That(JsonSerializer.Serialize(new { score.JudgementEvents, health.GaugeHistory }), Is.EqualTo(before));
            TestContext.WriteLine("P3A07_VISUAL_ONLY " + JsonSerializer.Serialize(new { afterRewind, identity, operations = recorder.Rows.Count, unchanged = true }));
        }));
    }

    [Test]
    public void DeferredVisualObservationDoesNotRequireAnActiveJudgementHold()
    {
        load(false, false);
        AddStep("observe an approaching head during a deferred simulation slice", () => withClock((advance, _) =>
        {
            var column = (BmsColumn)Playfield.Stage.Columns[0];
            var note = column.HitObjectContainer.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single();
            Assert.That(column.Judgements.ActiveLongNotes, Is.Empty);
            Playfield.BeginGameplayFrame();
            startRecording();
            advance(2999);
            inputManager.UpdateSubTree();
            var visualState = note.GetType().GetField("visualState", instance)!.GetValue(note)!;
            Assert.That(visualState.GetType().GetField("previousHeadTime", instance)!.GetValue(visualState), Is.EqualTo(2999d));
            Assert.That(column.Judgements.ActiveLongNotes, Is.Empty);
            Assert.That(recorder!.Rows.Any(r => r.Contains("/column.layout>")), Is.False,
                "The head position is sampled without forcing a skin traversal or activating a judgement hold.");
        }));
    }

    [Test]
    public void DirectStartTimeChangePreservesActualSubtreeOrder()
    {
        load(false, false);
        AddStep("change sorting key without reapplying defaults", () => withClock((advance, _) =>
        {
            var container = Playfield.Stage.Columns[1].HitObjectContainer;
            var target = Playfield.Beatmap.HitObjects.OfType<BmsNote>().First(n => n.StartTime == 3400);
            target.StartTime = 3350;
            var expected = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Where(d => d.HitObject is BmsNote)
                .OrderBy(d => container.IndexOf(d)).Select(d => Playfield.Beatmap.HitObjects.IndexOf(d.HitObject).ToString()).ToArray();
            startRecording();
            advance(3000);
            inputManager.UpdateSubTree();
            var actual = recorder!.Rows.Select(r => r.Split('|')).Where(r => r[4].EndsWith("/note.passive>") && r[7] == "1")
                .Select(r => r[8]).ToArray();
            Assert.That(actual, Is.EqualTo(expected));
            TestContext.WriteLine("P3A07_DIRECT_SORT " + JsonSerializer.Serialize(new { expected, actual }));
        }));
    }

    [Test]
    public void NativeRemoveAndReaddOwnExactlyOneSortingSubscription()
    {
        load(false, false);
        AddStep("remove and re-add the native entry", () => withClock((_, _) =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var entry = container.AliveEntries.Keys.First(e => e.HitObject.StartTime == 3400);
            var subscriptions = (System.Collections.IDictionary)typeof(BmsColumnHitObjectContainer).GetField("coreStartTimes", instance)!.GetValue(container)!;
            var original = subscriptions.Count;
            for (var iteration = 0; iteration < 3; iteration++)
            {
                Assert.That(container.Remove(entry), Is.True);
                Assert.That(subscriptions.Count, Is.EqualTo(original - 1));
                container.Add(entry);
                container.UpdateSubTree();
                Assert.That(subscriptions.Count, Is.EqualTo(original));
            }
            TestContext.WriteLine("P3A07_SORT_SUBSCRIPTIONS " + original);
        }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RealResultCallbackCannotSkipTheNextObjectOrExecuteItsStaleBinding(bool reapplyNext)
    {
        load(false, false);
        AddStep("mutate native membership from a synchronous result callback", () => withClock((advance, _) =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var pair = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Where(d => d.HitObject is BmsNote && d.HitObject.StartTime == 3400)
                .OrderBy(d => container.IndexOf(d)).ToArray();
            var first = pair[0].HitObject;
            var second = pair[1].HitObject;
            var entry = pair[0].Entry;
            var oldGeneration = pair[1].BindingGeneration;
            var observed = new List<BmsHitObject>();
            void result(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject drawable, osu.Game.Rulesets.Judgements.JudgementResult judgement)
            {
                if (!ReferenceEquals(judgement.HitObject, first) && !ReferenceEquals(judgement.HitObject, second)) return;
                observed.Add((BmsHitObject)judgement.HitObject);
                if (!ReferenceEquals(judgement.HitObject, first)) return;
                if (reapplyNext) second.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
                else Assert.That(container.Remove(entry), Is.True);
            }
            Playfield.NewResult += result;
            try
            {
                advance(new BmsNoteJudgementWindows(first).NextPassiveJudgementTime(first) + 1);
                inputManager.UpdateSubTree();
                if (reapplyNext)
                {
                    Assert.That(observed, Is.EqualTo(new[] { first }));
                    Assert.That(pair[1].BindingGeneration, Is.EqualTo(oldGeneration + 1));
                    inputManager.UpdateSubTree();
                }
                Assert.That(observed, Is.EqualTo(new[] { first, second }));
                TestContext.WriteLine("P3A07_NATIVE_MUTATION " + JsonSerializer.Serialize(new { reapplyNext, count = observed.Count }));
            }
            finally { Playfield.NewResult -= result; }
        }));
    }

    [Test]
    public void BothResultObserversRetainIdentityAfterSynchronousPoolReturn()
    {
        load(false, false);
        AddStep("return a judged representation inside its result callback", () => withClock((advance, _) =>
        {
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var drawable = column.HitObjectContainer.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => d.HitObject.StartTime == 3000);
            var note = drawable.HitObject;
            var entry = drawable.Entry;
            void result(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject _, osu.Game.Rulesets.Judgements.JudgementResult judgement)
            {
                if (ReferenceEquals(judgement.HitObject, note)) Assert.That(column.HitObjectContainer.Remove(entry), Is.True);
            }
            Playfield.NewResult += result;
            try
            {
                advance(3000);
                Assert.That(column.Judgements.TryPressRegisteredNote(note, osu.Game.Rulesets.Scoring.HitResult.Perfect,
                    new BmsPressContext(3000, new BmsFrameContext(3000, Playfield.Clock.Rate))), Is.True);
                Assert.That(drawable.HitObject, Is.Null);
                Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count(e => e.Source == BmsJudgementSource.From(note)), Is.EqualTo(1));
                TestContext.WriteLine("P3A07_STABLE_RESULT_IDENTITY");
            }
            finally { Playfield.NewResult -= result; }
        }));
    }

    [Test]
    public void DifferentPooledRepresentationWithEqualLocalGenerationKeepsItsOwnBinding()
    {
        load(false, false);
        AddStep("replace a pooled representation with the same local generation", () => withClock((advance, _) =>
        {
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var container = column.HitObjectContainer;
            var pair = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Where(d => d.HitObject is BmsNote && d.HitObject.StartTime == 3400).ToArray();
            var original = pair[0];
            var note = original.HitObject;
            var entry = original.Entry!;
            var otherEntry = pair[1].Entry!;
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            var generation = original.BindingGeneration;
            Assert.That(column.JudgementOutput.TryGetScheduledObject(original, out var oldBinding), Is.True);
            Assert.That(container.Remove(entry), Is.True);
            Assert.That(container.Remove(otherEntry), Is.True);
            container.Add(entry);
            container.UpdateSubTree();
            var replacement = (DrawableBmsHitObject)container.AliveEntries[entry];
            Assert.That(replacement, Is.SameAs(pair[1]));
            Assert.That(replacement, Is.Not.SameAs(original));
            Assert.That(replacement.BindingGeneration, Is.EqualTo(generation));
            Assert.That(column.JudgementOutput.TryGetScheduledObject(replacement, out var newBinding), Is.True);
            Assert.That(newBinding.BindingToken, Is.Not.EqualTo(oldBinding.BindingToken));
            var unaffected = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => d.HitObject.StartTime == 3030);
            Assert.That(column.JudgementOutput.TryGetScheduledObject(unaffected, out var unaffectedBinding), Is.True);
            var observed = new List<BmsHitObject>();
            void result(osu.Game.Rulesets.Objects.Drawables.DrawableHitObject _, osu.Game.Rulesets.Judgements.JudgementResult judgement)
                => observed.Add((BmsHitObject)judgement.HitObject);
            Playfield.NewResult += result;
            try
            {
                advance(5000);
                var schedule = column.Judgements.Schedule!;
                var frame = new BmsFrameContext(5000, Playfield.Clock.Rate);
                schedule.BeginBatch(0, 10, 10);
                schedule.BeginRemoval(oldBinding);
                schedule.CompleteRemoval(oldBinding, frame);
                schedule.RunSubtree([oldBinding, unaffectedBinding], frame);
                Assert.That(observed, Is.EqualTo(new[] { unaffected.HitObject }));
                column.JudgementOutput.UnbindNoteRepresentation(original, generation);
                Assert.That(column.JudgementOutput.TryGetScheduledObject(replacement, out var afterLateFree), Is.True);
                Assert.That(afterLateFree, Is.EqualTo(newBinding));
                schedule.Unbind(oldBinding);
                schedule.BeginBatch(0, 11, 11);
                schedule.RunSubtree([newBinding], frame);
                Assert.That(observed, Is.EqualTo(new[] { unaffected.HitObject, note }));
                TestContext.WriteLine("P3A07_POOLED_BINDING_COLLISION " + JsonSerializer.Serialize(new { generation, oldBinding.BindingToken, replacementToken = newBinding.BindingToken }));
            }
            finally { Playfield.NewResult -= result; }
        }));
    }

    private void load(bool useReplay, bool mixedStages)
    {
        AddStep("load no-automation chart", () => { stopRecording(); replay = useReplay; mixed = mixedStages; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("freeze clock source", () =>
        {
            inputManager = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
            if (replay) Player.GameplayClockContainer.Stop();
            else typeof(GameplayClockContainer).GetMethod("StopGameplayClock", instance)!.Invoke(Player.GameplayClockContainer, null);
            Player.GameplayClockContainer.Seek(2998);
        });
        AddUntilStep("simulation at start", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2998) < .001);
    }

    private void withClock(Action<Action<double>, KeyboardDevice> action)
    {
        var previous = Playfield.Clock;
        var inputClock = inputManager.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var process = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2998, IsRunning = true };
        var framed = new FramedClock(manual);
        framed.ProcessFrame();
        Playfield.Clock = framed;
        Playfield.ProcessCustomClock = false;
        inputManager.Clock = framed;
        inputManager.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = framed;
        Player.HealthProcessor.Clock = framed;
        var keyboard = new KeyboardDevice();
        inputManager.UseParentInput = false;
        typeof(CustomInputManager).GetMethod("AddHandler", instance)!.Invoke(inputManager, [keyboard]);
        try
        {
            Playfield.EndGameplayFrame();
            inputManager.UpdateSubTree();
            action(time => { manual.CurrentTime = time; framed.ProcessFrame(); }, keyboard);
        }
        finally
        {
            stopRecording();
            Playfield.EndGameplayFrame();
            Playfield.Clock = previous;
            Playfield.ProcessCustomClock = process;
            inputManager.Clock = inputClock;
            inputManager.ProcessCustomClock = true;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }

    private void startRecording() => recorder = new BmsCoreSchedulingRecorder(Playfield, (BmsScoreProcessor)Player.ScoreProcessor, (BmsHealthProcessor)Player.HealthProcessor) { Recording = true };

    private void finish(string scenario)
    {
        recorder!.Recording = false;
        var rows = recorder.Rows.ToArray();
        TestContext.WriteLine("P3A07_CORE_GENERATED " + JsonSerializer.Serialize(new { scenario, rows, raw = recorder.Rows }));
        using var stream = GetType().Assembly.GetManifestResourceStream("osu.Game.Rulesets.BmsRuleset.Tests.reference_fixtures.judgement_p3a07_core.json");
        Assert.That(stream, Is.Not.Null, "The frozen old-core fixture is required for generation comparison.");
        using var fixture = JsonDocument.Parse(stream!);
        var expected = fixture.RootElement.GetProperty("traces").GetProperty(scenario).EnumerateArray().Select(r => r.GetString()!).ToArray();
        var actualPlan = logicalRows(rows);
        var expectedPlan = logicalRows(expected);
        TestContext.WriteLine("P3A07_CORE_PLAN " + JsonSerializer.Serialize(new { scenario, expected = expectedPlan, actual = actualPlan }));
        Assert.That(actualPlan, Is.EqualTo(expectedPlan), "Generated operations match the frozen old-core observation, including intermediate state.");
    }

    private static string[] logicalRows(IEnumerable<string> rows)
    {
        var selected = new List<string>();
        foreach (var row in rows)
        {
            var parts = row.Split('|');
            var path = parts[4].Split('/');
            var checkpoint = path[^1];
            var name = checkpoint[..^1];
            if (name is not ("input.queue" or "batch" or "mines" or "bodies" or "body.sample" or "keysound" or "endpoint" or "body.tick"
                or "result.notify" or "result.externalRevert" or "result.drawableRevert" or "result.revert" or "history.rewind" or "index.rebuild"
                or "representation.apply" or "representation.free" or "representation.killed" or "representation.remove" or "representation.add"
                or "note.passive" or "long.passive" or "long.finish" or "defaults")) continue;
            var source = name switch
            {
                "note.passive" or "long.passive" => path[0] is "column.layout" or "core.layout" ? "layout"
                    : path[0] is "column.deferred" or "core.deferred" ? "deferred"
                    : path[0] is "representation.remove" or "representation.killed" or "core.removal" ? "removal" : "subtree",
                "long.finish" => path[0] is "column.deferred" or "core.deferred" ? "deferred" : "layout",
                "body.sample" => path[0] == "long.finish" ? "post" : "batch",
                "batch" => path[0],
                _ => "operation",
            };
            parts[4] = source + "/" + checkpoint;
            if (name == "long.finish") parts[11] = string.Join(",", parts[11].Split(',').Where(p => p.StartsWith("holding=")));
            selected.Add(string.Join("|", parts.Skip(1)));
        }
        return selected.ToArray();
    }

    private static string[] semanticRows(IEnumerable<string> rows) => rows.Select(r => r.Split('|'))
        .Where(r => r[4].EndsWith("/input.queue>") || r[4].EndsWith("/keysound>") || r[4].EndsWith("/result.notify=")
                    || r[4].EndsWith("/body.tick>") || r[4].EndsWith("/result.externalRevert=") || r[4].EndsWith("/result.drawableRevert="))
        .Select(r => string.Join("|", r[4][(r[4].LastIndexOf('/') + 1)..], r[5], r[7], r[8], r[11], r[12], r[13], r[14], r[15], r[16], r[17], r[19])).ToArray();

    [TearDown]
    public void StopCoreProbes() => stopRecording();

    private void stopRecording() { recorder?.Dispose(); recorder = null; budgetHost = null; }

    private sealed class KeyboardDevice : InputHandler
    {
        internal List<IInput> Inputs { get; } = [];
        internal int Polls { get; private set; }
        public override bool IsActive => true;
        public override void CollectPendingInputs(List<IInput> inputs) { Polls++; inputs.AddRange(Inputs); Inputs.Clear(); }
    }
}
