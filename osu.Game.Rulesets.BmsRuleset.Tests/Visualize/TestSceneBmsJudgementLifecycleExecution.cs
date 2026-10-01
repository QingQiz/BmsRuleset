#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using System.Collections;
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
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsJudgementLifecycleExecution : BmsPlayerTestScene
{
    public enum Kind { Note, LN, CN, HCN }
    private Kind kind;
    private bool scratch;
    private bool mixed;
    private bool duplicate;
    private bool pauseFixture;
    private bool pmsFixture;
    private int columnIndex => scratch ? 0 : 1;
    private static readonly BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Action<object, MethodBase, bool>? observation;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => pauseFixture ? new TestPlayer(allowPause: true, showResults: false) : CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = pmsFixture ? BmsLayoutVariant.Pms9K : BmsLayoutVariant.Bme7K, TotalColumns = pmsFixture ? 9 : 8, Rank = 2, Total = 24,
            LockedLongNoteMode = kind switch { Kind.CN => BmsLongNoteMode.ChargeNote, Kind.HCN => BmsLongNoteMode.HellChargeNote, _ => BmsLongNoteMode.LongNote },
            HitObjects = [make(3000), make(9000), new BmsNote { StartTime = 30000, Column = 2 }],
        };
        if (duplicate) beatmap.HitObjects.Add(make(3000));
        if (mixed) beatmap.HitObjects.Add(new BmsLandmine { StartTime = 20000, Column = columnIndex });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(n => n.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;

        BmsHitObject make(double time) => kind == Kind.Note ? new BmsNote { StartTime = time, Column = columnIndex }
            : new BmsLongNote { StartTime = time, Duration = 1000, Column = columnIndex };
    }

    private void load(Kind objectKind, bool isScratch = false, bool nonBatch = false, bool sameTime = false, bool allowPause = false, bool pms = false)
    {
        AddStep("load lifecycle chart", () => { kind = objectKind; scratch = isScratch; mixed = nonBatch; duplicate = sameTime; pauseFixture = allowPause; pmsFixture = pms; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("seek before head", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2798); if (allowPause) Player.GameplayClockContainer.Start(); });
        AddUntilStep("simulation ready", () => allowPause ? Player.DrawableRuleset.FrameStableClock.CurrentTime >= 2798 : Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2798) < .001);
        if (allowPause) AddStep("stop before manual simulation", () => Player.GameplayClockContainer.Stop());
    }

    [Test]
    public void DefaultsReplaceOnlyTheRepresentationGeneration([Values] Kind objectKind, [Values] bool nonBatch)
    {
        load(objectKind, nonBatch: nonBatch, sameTime: true);
        AddStep("verify real defaults", () => withClock(run));

        void run(Session session)
        {
            var drawable = session.Drawable;
            var note = session.Note;
            var result = drawable.Result;
            var entry = drawable.Entry;
            var runtime = getRuntime(session.Context, note);
            var order = session.Container.AliveEntries.Values.ToArray();
            var generation = drawable.BindingGeneration;
            session.Advance(3003);
            if (note is BmsLongNote)
            {
                session.Context.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(3003, session.Frame));
                session.Recorded.TryPressRegisteredNote(note, HitResult.Perfect, new BmsPressContext(3003, session.Frame));
            }
            var state = note is BmsLongNote held ? session.Context.GetLongNoteState(held) : (BmsLongNoteState?)null;
            var oldRaw = raw(result);
            var oldEvents = JsonSerializer.Serialize(session.Score.JudgementEvents);
            var oldCallback = session.CaptureResultCallback();
            var boundaries = new List<string>();
            using var probes = observe(drawable, session, (method, exit) =>
            {
                boundaries.Add(method.Name + (exit ? ".exit" : ".enter"));
                if (method.Name == "OnFree" && exit)
                    Assert.Throws<InvalidOperationException>(() => session.ProductionOutput.GetRepresentation(note));
            });
            note.JudgementRate = .5;
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            session.Recorded.RefreshNoteConfiguration(note);
            session.Output.Reapply(note);
            Assert.That(drawable.BindingGeneration, Is.EqualTo(generation + 1));
            Assert.That(drawable.Entry, Is.SameAs(entry));
            Assert.That(drawable.Result, Is.SameAs(result));
            Assert.That(getRuntime(session.Context, note), Is.SameAs(runtime));
            Assert.That(note is BmsLongNote heldAfter ? session.Context.GetLongNoteState(heldAfter) : (BmsLongNoteState?)null, Is.EqualTo(state));
            Assert.That(session.Container.AliveEntries.Values, Is.EqualTo(order));
            Assert.That(JsonSerializer.Serialize(session.Score.JudgementEvents), Is.EqualTo(oldEvents));
            Assert.That(result.TimeOffset, Is.Zero);
            Assert.That(raw(result), Is.EqualTo(session.Output.GetRawTime(note)), "The framework setter also stamps unjudged Note and LN results");
            TestContext.WriteLine($"P3A07_LIFECYCLE_EFFECT defaults;kind={objectKind};generation={generation}->{drawable.BindingGeneration};resultRetained={ReferenceEquals(drawable.Result, result)};boundaries={string.Join(',', boundaries)}");
            Assert.That(boundaries, Is.EqualTo(new[] { "OnFree.enter", "OnFree.exit", "OnApply.enter", "OnApply.exit" }));
            if (note is BmsLongNote)
            {
                Assert.That(readField<double>(drawable, "lastSimulationTime"), Is.NaN);
                Assert.That(readField<bool>(drawable, "bodyGeometryValid"), Is.False);
                Assert.That(session.Context.ActiveLongNotes.Count(n => ReferenceEquals(n, note)), Is.EqualTo(1));
            }
            if (objectKind is Kind.CN or Kind.HCN)
            {
                Assert.That(oldRaw, Is.EqualTo(3003));
                Assert.That(raw(result), Is.EqualTo(4000));
                Assert.That(result.TimeAbsolute, Is.EqualTo(4000));
                Assert.That(((BmsLongNoteJudgementResult)result).EndpointResults.Single().EventTime, Is.EqualTo(3003));
            }
            Assert.That(session.ProductionOutput.GetRepresentation(note), Is.SameAs(drawable));
            session.AssertOldNotificationIgnored(oldCallback!, drawable, result, generation);
            var active = readField<IList>(session.Context.Notes, "activeNoteSlots");
            Assert.That(active.Cast<int>().Distinct().Count(), Is.EqualTo(active.Count));
            Assert.That(readField<IDictionary>(session.ProductionOutput, "representations").Count, Is.EqualTo(order.Count(d => d.HitObject is BmsNote or BmsLongNote)));
            Assert.That(readField<IDictionary>(session.ProductionOutput, "presentationObservers").Count, Is.EqualTo(order.Count(d => d.HitObject is BmsLongNote)));
            var expectedDeadline = session.Note is BmsLongNote ? double.PositiveInfinity : new BmsNoteJudgementWindows(note).NextPassiveJudgementTime(note);
            Assert.That(passiveDeadline(session.Context, note), Is.EqualTo(expectedDeadline));
            session.Mark("reapply");
            TestContext.WriteLine("P3A0_REAPPLY " + JsonSerializer.Serialize(new { objectKind, nonBatch, generation, nextGeneration = drawable.BindingGeneration, boundaries, oldRaw, raw = raw(result), result.TimeAbsolute, oldEvents }));
            session.Finish("RA-1-and-reapply");
        }
    }

    [Test]
    public void RemovalKeepsTheLastCheckBoundAndInvalidatesPooledCallbacks([Values] Kind objectKind, [Values] bool nonBatch)
    {
        load(objectKind, nonBatch: nonBatch);
        AddStep("verify real removal", () => withClock(run));

        void run(Session session)
        {
            var drawable = session.Drawable;
            var result = drawable.Result;
            var generation = drawable.BindingGeneration;
            var oldCallback = session.CaptureResultCallback();
            var boundaries = new List<string>();
            var actualResults = new List<string>();
            var recordedResults = new List<string>();
            var samplesStopped = false;
            Action<DrawableHitObject, JudgementResult> observedResult = (_, _) =>
            {
                Assert.That(samplesStopped, Is.True, "The framework stops samples before the last passive result");
                actualResults.Add(resultSnapshot("main", false));
            };
            drawable.OnNewResult += observedResult;
            session.Output.AfterResultCheckpoint = (_, _) => recordedResults.Add(resultSnapshot("main", true));
            session.Output.SyntheticEndpoint = (_, exit) => recordedResults.Add(resultSnapshot(exit ? "synthetic.after" : "synthetic.before", true));
            using var probes = observe(drawable, session, (method, exit) =>
            {
                boundaries.Add(method.Name + (exit ? ".exit" : ".enter"));
                if (method.Name == "StopAllSamples" && exit) samplesStopped = true;
                if (method.Name != "OnKilled") return;
                Assert.That(session.Context.ActiveLongNotes, Does.Not.Contain(session.Note));
                Assert.That(session.ProductionOutput.GetRepresentation(session.Note), Is.SameAs(drawable));
                Assert.That(session.Context.IsNoteCandidate(session.Note), Is.False);
                Assert.That(session.Context.GetPendingLongNoteTails(), Is.Empty);
                Assert.That(session.Context.GetActiveBodies(), Is.Empty);
                if (!exit)
                {
                    session.Recorded.BeginNoteRemoval(session.Note);
                    session.Recorded.CheckRegisteredNotePassive(session.Note, session.Frame);
                }
            }, sampleStops: true);
            var lifecycleObserver = observation;
            observation = (target, method, exit) =>
            {
                lifecycleObserver?.Invoke(target, method, exit);
                if (ReferenceEquals(target, Playfield) && method.Name == "ApplySyntheticLongNoteEndpoint")
                {
                    Assert.That(samplesStopped, Is.True);
                    actualResults.Add(resultSnapshot(exit ? "synthetic.after" : "synthetic.before", false));
                }
            };
            using var syntheticProbe = new ScopedMethodProbe(typeof(BmsPlayfield).GetMethod("ApplySyntheticLongNoteEndpoint", instance)!,
                GetType().GetMethod(nameof(observeEnter), BindingFlags.Static | BindingFlags.NonPublic), GetType().GetMethod(nameof(observeExit), BindingFlags.Static | BindingFlags.NonPublic));
            session.Advance(4500);
            Playfield.UpdateSubTree();
            drawable.OnNewResult -= observedResult;
            Assert.That(actualResults, Is.EqualTo(recordedResults));
            Assert.That(session.Container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, session.Note)), Is.False);
            Assert.That(result.Type, Is.EqualTo(session.Output.GetMainResult(session.Note).Type));
            Assert.Throws<InvalidOperationException>(() => session.ProductionOutput.GetRepresentation(session.Note));
            TestContext.WriteLine($"P3A07_LIFECYCLE_EFFECT removal;kind={objectKind};freed={drawable.HitObject == null};boundaries={string.Join(',', boundaries)}");
            Assert.That(boundaries, Is.EqualTo(new[] { "OnKilled.enter", "StopAllSamples.enter", "StopAllSamples.exit", "OnKilled.exit", "OnFree.enter", "OnFree.exit" }));
            var count = session.Score.ScoringJudgementEventCount;
            Assert.That(count, Is.EqualTo(objectKind is Kind.CN or Kind.HCN ? 2 : 1));
            Assert.That(session.Output.MainResults, Is.EqualTo(objectKind == Kind.HCN ? 0 : 1));
            Assert.That(session.Output.SyntheticEndpoints, Has.Count.EqualTo(objectKind == Kind.HCN ? 2 : objectKind == Kind.CN ? 1 : 0));
            session.Mark("removed-after-last-check");
            session.Advance(4501);
            Playfield.UpdateSubTree();
            Assert.That(session.Score.ScoringJudgementEventCount, Is.EqualTo(count));
            observation = null;
            session.Advance(8798);
            Playfield.UpdateSubTree();
            var replacement = session.Container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single(d => d.HitObject.StartTime == 9000);
            Assert.That(replacement, Is.SameAs(drawable), "The real column pool reuses the released representation");
            Assert.That(replacement.BindingGeneration, Is.EqualTo(generation + 1));
            Assert.That(replacement.Result, Is.Not.SameAs(result));
            session.AssertOldNotificationIgnored(oldCallback!, drawable, result, generation);
            Assert.That(session.ProductionOutput.GetRepresentation(replacement.HitObject), Is.SameAs(drawable));
            Assert.That(session.Context.IsNoteCandidate(replacement.HitObject), Is.True);
            TestContext.WriteLine("P3A0_REMOVAL " + JsonSerializer.Serialize(new { objectKind, nonBatch, generation, nextGeneration = replacement.BindingGeneration, boundaries, actualResults, recordedResults, result.Type, events = session.Score.JudgementEvents }));
            session.ExtraTrace.AddRange(actualResults);
            session.Finish("removal");

            string resultSnapshot(string checkpoint, bool recording)
            {
                if (!recording)
                {
                    Assert.That(session.Context.IsNoteCandidate(session.Note), Is.False);
                    Assert.That(session.Context.GetPendingLongNoteTails(), Is.Empty);
                    Assert.That(session.Context.GetActiveBodies(), Is.Empty);
                }
                return JsonSerializer.Serialize(new { checkpoint, type = recording ? session.Output.GetMainResult(session.Note).Type : result.Type,
                    state = session.Note is BmsLongNote n ? recording ? session.Recorded.GetLongNoteState(n) : session.Context.GetLongNoteState(n) : (BmsLongNoteState?)null,
                    syntheticCount = recording ? session.Output.SyntheticEndpoints.Count : session.Submissions.Count });
            }
        }
    }

    [Test]
    public void DirectTimingChangeRefreshesCandidatesAndDeadlineWithoutRebinding([Values] Kind objectKind)
    {
        load(objectKind, sameTime: true);
        AddStep("move a live head without reapplying defaults", () => withClock(session =>
        {
            var note = session.Note;
            var generation = session.Drawable.BindingGeneration;
            var slot = session.Context.GetNoteSlot(note);
            var heads = readField<SortedSet<(double Time, long Order, int Slot)>>(session.Context.Notes, "pendingNoteHeads");
            var oldKey = heads.Single(h => h.Slot == slot);
            session.Context.RefreshPassiveDeadline(2798);

            note.StartTime = 2990;
            session.Recorded.RefreshNoteTiming(note);
            Assert.That(session.Drawable.BindingGeneration, Is.EqualTo(generation));
            Assert.That(heads.Single(h => h.Slot == slot), Is.EqualTo((2990d, oldKey.Order, slot)));
            Assert.That(heads.Count(h => h.Time == 3000), Is.EqualTo(1));
            Assert.That(session.Context.FirstPendingNote, Is.SameAs(note));
            Assert.That(session.Context.NextPassiveUpdateTime, Is.EqualTo(double.NegativeInfinity));
            session.Context.RefreshPassiveDeadline(2798);
            Assert.That(session.Context.NextPassiveUpdateTime, Is.EqualTo(passiveDeadline(session.Context, note)));
            Assert.That(readField<BmsHitObject[]>(session.Context, "columnNotes"), Is.Ordered.By(nameof(BmsHitObject.StartTime)));

            session.Advance(2990);
            PressColumn(session.Column, 2990);
            session.Recorded.HandleNotePress(new BmsPressContext(2990, session.Frame));
            Assert.That(note is BmsLongNote hold ? session.Context.GetLongNoteState(hold).HeadJudged : session.Drawable.Result.HasResult, Is.True);
            Assert.That(session.Context.FirstPendingNote, Is.SameAs(session.Recorded.FirstPendingNote));
        }));
    }

    [TestCase(Kind.LN)]
    [TestCase(Kind.CN)]
    [TestCase(Kind.HCN)]
    public void SimulationFeedbackBelongsToTheCurrentRepresentation(Kind objectKind)
    {
        load(objectKind);
        AddStep("reapply and release the registered feedback", () => withClock(session =>
        {
            var drawable = session.Drawable;
            var generation = drawable.BindingGeneration;
            var oldCalls = 0;
            session.ProductionOutput.ObserveSimulationSteps(drawable, (in BmsFrameContext _) => oldCalls++);
            session.ProductionOutput.ObserveSimulationStep(session.Note, session.Frame);
            Assert.That(oldCalls, Is.EqualTo(1));

            session.Note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            Assert.That(drawable.BindingGeneration, Is.EqualTo(generation + 1));
            Assert.That(readField<double>(drawable, "lastSimulationTime"), Is.NaN);
            session.ProductionOutput.UnbindNoteRepresentation(drawable, generation);
            session.ProductionOutput.ObserveSimulationStep(session.Note, session.Frame);
            Assert.That(oldCalls, Is.EqualTo(1));
            Assert.That(readField<double>(drawable, "lastSimulationTime"), Is.EqualTo(session.Frame.SimulationTime));
            Assert.That(typeof(DrawableBmsHitObject).GetMethod("UpdateVisualState", instance), Is.Null);

            Assert.That(session.Container.Remove(drawable.Entry!), Is.True);
            Assert.That(readField<IDictionary>(session.ProductionOutput, "simulationObservers").Contains(drawable), Is.False);
        }));
    }

    [Test]
    public void ChangedSortKeyReplacesTheOldPendingKey([Values] Kind objectKind)
    {
        load(objectKind, sameTime: true);
        AddStep("reapply changed head time through the actual entry", () => withClock(session =>
        {
            var note = session.Note;
            var drawable = session.Drawable;
            var slot = readField<Dictionary<BmsHitObject, int>>(session.Context, "noteSlots")[note];
            var heads = readField<SortedSet<(double Time, long Order, int Slot)>>(session.Context.Notes, "pendingNoteHeads");
            var oldKey = heads.Single(h => h.Slot == slot);
            var order = readField<List<int>>(session.Context.Notes, "activeNoteSlots").ToArray();
            note.StartTime = 2990;
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            session.Recorded.RefreshNoteConfiguration(note);
            Assert.That(heads.Any(h => h.Slot == slot && h.Time == 3000), Is.False);
            Assert.That(heads.Single(h => h.Slot == slot), Is.EqualTo((2990d, oldKey.Order, slot)));
            Assert.That(heads.Count(h => h.Time == 3000), Is.EqualTo(1));
            Assert.That(session.Context.FirstPendingNote, Is.SameAs(note));
            Assert.That(session.Recorded.FirstPendingNote, Is.SameAs(note));
            Assert.That(readField<SortedSet<(double Time, long Order, int Slot)>>(session.Recorded.Notes, "pendingNoteHeads"), Is.EqualTo(heads));
            Assert.That(readField<List<int>>(session.Context.Notes, "activeNoteSlots"), Is.EqualTo(order));
            Assert.That(session.ProductionOutput.GetRepresentation(note), Is.SameAs(drawable));
            session.Context.RefreshPassiveDeadline(2798);
            Assert.That(session.Context.NextPassiveUpdateTime, Is.EqualTo(passiveDeadline(session.Context, note)));
            note.StartTime = 3000;
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            session.Recorded.RefreshNoteConfiguration(note);
            Assert.That(readField<BmsHitObject[]>(session.Context, "columnNotes"), Is.EqualTo(readField<BmsHitObject[]>(session.Recorded, "columnNotes")));
            Assert.That(readField<SortedSet<(double Time, long Order, int Slot)>>(session.Recorded.Notes, "pendingNoteHeads"), Is.EqualTo(heads));
        }));
    }

    [Test]
    public void MainResultNotificationsPrecedeResetAndHistoryRestoration([Values] Kind objectKind, [Values] bool nonBatch)
    {
        load(objectKind, nonBatch: nonBatch);
        AddStep("verify real main result rollback", () => withClock(run));

        void run(Session session)
        {
            var drawable = session.Drawable;
            var entry = drawable.Entry;
            var result = drawable.Result;
            var initialHealth = session.Health.CurrentGaugeStates.ToArray();
            session.Advance(3003);
            PressColumn(session.Column, 3000);
            session.Recorded.HandleNotePress(new BmsPressContext(3000, session.Frame));
            if (objectKind == Kind.LN)
            {
                session.Advance(3997);
                ReleaseColumn(session.Column, 3990);
                session.Recorded.HandleExecutionRelease(3990, session.Frame);
            }
            session.Mark("judged-before-rewind");
            var actual = new List<string>();
            var recorded = new List<string>();
            Action<JudgementResult> playfieldRevert = r => { if (ReferenceEquals(r, result)) capture("playfield", false); };
            Action<DrawableHitObject, JudgementResult> drawableRevert = (d, _) => { if (ReferenceEquals(d, drawable)) capture("drawable", false); };
            Playfield.RevertResult += playfieldRevert;
            drawable.OnRevertResult += drawableRevert;
            session.Output.Reverting = _ => { capture("playfield", true); capture("entry.before", true); capture("drawable", true); capture("entry.after", true); };
            session.Output.AfterReset = _ => capture("reset", true);
            var probes = new List<ScopedMethodProbe>();
            try
            {
                observation = (target, method, exit) =>
                {
                    if (ReferenceEquals(target, entry)) capture(exit ? "entry.after" : "entry.before", false);
                    if (ReferenceEquals(target, result) && exit) capture("reset", false);
                };
                foreach (var method in new[] { typeof(HitObjectLifetimeEntry).GetMethod("OnRevertResult", instance)!, typeof(JudgementResult).GetMethod("Reset", instance)! })
                    probes.Add(new ScopedMethodProbe(method, GetType().GetMethod(nameof(observeEnter), BindingFlags.NonPublic | BindingFlags.Static),
                        GetType().GetMethod(nameof(observeExit), BindingFlags.NonPublic | BindingFlags.Static)));
                probes.Add(new ScopedMethodProbe(typeof(osu.Game.Rulesets.UI.Playfield).GetMethod("revertResult", instance)!, GetType().GetMethod(nameof(keepBoundary), BindingFlags.NonPublic | BindingFlags.Static)));
                session.Traverse(2999);
            }
            finally
            {
                observation = null;
                for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose();
                Playfield.RevertResult -= playfieldRevert;
                drawable.OnRevertResult -= drawableRevert;
            }
            Assert.That(actual, Has.Count.EqualTo(5));
            Assert.That(actual, Is.EqualTo(recorded));
            Assert.That(result.HasResult, Is.False);
            Assert.That(session.Health.CurrentGaugeStates, Is.EqualTo(initialHealth));
            Assert.That(session.Score.ScoringJudgementEventCount, Is.Zero);
            Assert.That(session.Context.FirstPendingNote, nonBatch ? Is.Null : Is.SameAs(session.Note));
            Assert.That(session.Context.IsNoteCandidate(session.Note), Is.True);
            if (session.Note is BmsLongNote note)
            {
                Assert.That(session.Context.GetLongNoteState(note).HeadJudged, Is.False);
                Assert.That(session.Context.GetLongNoteState(note).PendingReleaseTime, Is.Null);
            }
            session.Mark("reset-and-history-restored-before-input");
            session.Advance(3003);
            Assert.That(PressColumn(session.Column, 3000), Is.EqualTo(session.Recorded.HandleNotePress(new BmsPressContext(3000, session.Frame))));
            session.Mark("same-head-selected-after-rewind");
            TestContext.WriteLine("P3A0_REVERT " + JsonSerializer.Serialize(new { objectKind, nonBatch, actual, recorded }));
            session.Finish("main-revert");

            void capture(string checkpoint, bool recording)
            {
                var current = recording ? session.Output.GetMainResult(session.Note)
                    : new BmsMainResultState(result.Type, result.TimeAbsolute, result.TimeOffset, result.GameplayRate, ((BmsJudgementResult)result).SuppressPenalty);
                Assert.That(current.HasResult, Is.EqualTo(checkpoint != "reset"), "Every original notification sees the old authoritative result");
                if (!recording) Assert.That(session.Context.GetNoteResult(session.Note), Is.EqualTo(current));
                (recording ? recorded : actual).Add(JsonSerializer.Serialize(new { checkpoint, current, raw = recording ? session.Output.GetRawTime(session.Note) : raw(result),
                    state = session.Note is BmsLongNote n ? recording ? session.Recorded.GetLongNoteState(n) : session.Context.GetLongNoteState(n) : (BmsLongNoteState?)null }));
            }
        }
    }

    private static void keepBoundary() { }

    [Test]
    public void HcnReapplyRetainsNonEmptyBodyAndPendingReleaseHistory()
    {
        load(Kind.HCN, pms: true);
        AddStep("verify reapply with real histories", () => withClock(run));

        void run(Session session)
        {
            var note = (BmsLongNote)session.Note;
            var drawable = session.Drawable;
            var runtime = (BmsHellChargeNoteRuntime)getRuntime(session.Context, note)!;
            var tracker = runtime.Body;
            Assert.That(session.Column.PreserveLongNoteHistory && tracker.HistoryEnabled, Is.True, "Replay playback retains applicable HCN histories");
            session.Advance(3003);
            PressColumn(session.Column, 3000);
            session.Recorded.HandleNotePress(new BmsPressContext(3000, session.Frame));
            session.Traverse(3204);
            var accumulatorAt3204 = readField<double>(tracker, "accumulator");
            session.Advance(3300);
            ReleaseColumn(session.Column, 3300);
            session.Recorded.HandleExecutionRelease(3300, session.Frame);
            session.Traverse(3400);
            Assert.That(runtime.State.PendingReleaseTime, Is.EqualTo(3300));
            var history = readField<BmsHellChargeHistory>(tracker, "history");
            var releases = readField<IList>(runtime.Hold, "releaseHistory");
            Assert.That(releases.Count, Is.GreaterThan(0));
            Assert.That(readField<IList>(history, "frames").Count + readField<IList>(history, "runs").Count, Is.GreaterThan(0));
            var before = histories(runtime);
            session.Mark("nonempty-history-before-reapply");
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            session.Recorded.RefreshNoteConfiguration(note);
            session.Output.Reapply(note);
            Assert.That(getRuntime(session.Context, note), Is.SameAs(runtime));
            Assert.That(runtime.Body, Is.SameAs(tracker));
            Assert.That(readField<BmsHellChargeHistory>(tracker, "history"), Is.SameAs(history));
            Assert.That(readField<IList>(runtime.Hold, "releaseHistory"), Is.SameAs(releases));
            Assert.That(histories(runtime), Is.EqualTo(before));
            session.Mark("nonempty-history-after-reapply");
            foreach (var time in new[] { 3350d, 3299d })
            {
                session.Traverse(time);
                Assert.That(readField<double>(tracker, "accumulator"), Is.EqualTo(accumulatorAt3204));
                Assert.That(runtime.State.PendingReleaseTime, time > 3300 ? Is.EqualTo(3300) : Is.Null);
                var recordedRuntime = (BmsHellChargeNoteRuntime)getRuntime(session.Recorded, note)!;
                Assert.That(histories(runtime), Is.EqualTo(histories(recordedRuntime)));
                session.Mark("history-rewind:" + time);
            }
            session.Traverse(3401);
            session.Mark("history-replayed");
            session.ExtraTrace.Add(histories(runtime));
            session.Finish("nonempty-hcn-reapply");
        }

        static string histories(BmsHellChargeNoteRuntime runtime)
        {
            var tracker = runtime.Body;
            var history = readField<BmsHellChargeHistory>(tracker, "history");
            return JsonSerializer.Serialize(new { accumulator = readField<double>(tracker, "accumulator"),
                frames = readField<IList>(history, "frames").Cast<object>(), runs = readField<IList>(history, "runs").Cast<object>(),
                releases = readField<IList>(runtime.Hold, "releaseHistory").Cast<object>() });
        }
    }

    [TestCase(Kind.LN)]
    [TestCase(Kind.CN)]
    [TestCase(Kind.HCN)]
    public void PooledActivationRestoresTheLogicalObjectBeforeRegisteringItsRepresentation(Kind objectKind)
    {
        load(objectKind);
        AddStep("verify activation after ordinary rewind", () => withClock(run));

        void run(Session session)
        {
            var note = (BmsLongNote)session.Note;
            var original = session.Drawable;
            var runtime = getRuntime(session.Context, note);
            var generation = original.BindingGeneration;
            session.Advance(3003);
            PressColumn(session.Column, 3000);
            session.Recorded.HandleNotePress(new BmsPressContext(3000, session.Frame));
            session.Advance(3997);
            ReleaseColumn(session.Column, 3990);
            session.Recorded.HandleExecutionRelease(3990, session.Frame);
            session.Traverse(4400);
            Assert.That(session.Container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, note)), Is.False);
            var registered = 0;
            observation = (target, method, exit) =>
            {
                if (!ReferenceEquals(target, session.ProductionOutput)) return;
                var drawable = session.Drawable;
                Assert.That(getRuntime(session.Context, note), Is.SameAs(runtime));
                Assert.That(session.Context.GetLongNoteState(note).IsHolding, Is.True);
                Assert.That(session.Context.GetLongNoteState(note).TailJudged, Is.False);
                if (exit)
                {
                    registered++;
                    Assert.That(session.ProductionOutput.GetRepresentation(note), Is.SameAs(drawable));
                    Assert.That(session.Context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
                    Assert.That(session.Context.GetActiveBodies(), Is.EqualTo(objectKind == Kind.HCN ? new[] { note } : Array.Empty<BmsLongNote>()));
                }
            };
            using (var probe = new ScopedMethodProbe(typeof(BmsJudgementBridge).GetMethod("Register", instance)!,
                       GetType().GetMethod(nameof(observeEnter), BindingFlags.Static | BindingFlags.NonPublic), GetType().GetMethod(nameof(observeExit), BindingFlags.Static | BindingFlags.NonPublic)))
            {
                try { session.Traverse(3500); }
                finally { observation = null; }
            }
            Assert.That(registered, Is.EqualTo(1));
            Assert.That(session.Drawable, Is.SameAs(original));
            Assert.That(original.BindingGeneration, Is.EqualTo(generation + 1));
            session.Mark("reactivated-with-restored-history");
            session.Advance(3997);
            ReleaseColumn(session.Column, 3990);
            session.Recorded.HandleExecutionRelease(3990, session.Frame);
            session.Mark("tail-replayed-after-activation");
            session.Finish("pooled-activation");
        }
    }

    public enum PauseState { Holding, HeadPoor, SuccessfulTail }

    [TestCase(Kind.LN, PauseState.Holding, 3600)]
    [TestCase(Kind.LN, PauseState.Holding, 3990)]
    [TestCase(Kind.CN, PauseState.Holding, 3600)]
    [TestCase(Kind.CN, PauseState.Holding, 3990)]
    [TestCase(Kind.HCN, PauseState.Holding, 3600)]
    [TestCase(Kind.HCN, PauseState.Holding, 3990)]
    [TestCase(Kind.HCN, PauseState.HeadPoor, 3600)]
    [TestCase(Kind.HCN, PauseState.SuccessfulTail, 3990)]
    public void RealPlayerPausePreservesCompletionAndPresentationThroughResume(Kind objectKind, PauseState pauseState, double pauseTime)
    {
        load(objectKind, allowPause: true);
        AddStep("verify actual player pause", () => withClock(run));

        void run(Session session)
        {
            var note = (BmsLongNote)session.Note;
            var drawable = session.Drawable;
            if (pauseState == PauseState.HeadPoor)
                session.Traverse(passiveDeadline(session.Context, note) + 1);
            else
            {
                session.Advance(3003);
                PressColumn(session.Column, 3000);
                session.Recorded.HandleNotePress(new BmsPressContext(3000, session.Frame));
            }
            if (pauseState == PauseState.SuccessfulTail)
            {
                session.Advance(3980);
                ReleaseColumn(session.Column, 3980);
                session.Recorded.HandleExecutionRelease(3980, session.Frame);
            }
            session.Traverse(pauseTime);
            session.Mark("before-pause");
            var visuals = new List<string>();
            var pauseVisuals = new List<string>();
            var probes = new List<ScopedMethodProbe>();
            try
            {
                observation = (target, method, exit) => { if (ReferenceEquals(target, drawable)) pauseVisuals.Add(method.Name.Split('.').Last() + (exit ? ".after" : ".before")); };
                probes.Add(new ScopedMethodProbe(drawable.GetType().GetMethod("applyPresentation", instance)!,
                    GetType().GetMethod(nameof(observeEnter), BindingFlags.Static | BindingFlags.NonPublic),
                    GetType().GetMethod(nameof(observeExit), BindingFlags.Static | BindingFlags.NonPublic)));
                Player.GameplayClockContainer.Seek(pauseTime);
                Player.GameplayClockContainer.Start();
                Assert.That(Player.GameplayClockContainer.CurrentTime, Is.EqualTo(pauseTime));
                Assert.That(Player.Pause(), Is.True);
                session.Recorded.CompleteLongNotesAtPause(session.Frame);
                captureVisual("paused");
            }
            finally
            {
                observation = null;
                for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose();
            }
            session.Mark("paused");
            var completed = session.Context.GetLongNoteState(note);
            var result = session.Output.GetMainResult(note);
            var scores = JsonSerializer.Serialize(session.Score.JudgementEvents);
            var health = JsonSerializer.Serialize(session.Health.GaugeHistory);
            if (pauseState == PauseState.Holding)
            {
                Assert.That(completed.TailJudged, Is.True);
                Assert.That(completed.LongNoteStarted, Is.False);
                Assert.That(session.Recorded.GetActiveBodies(), Is.Empty);
            }
            else
                Assert.That(completed.HasActiveBody, Is.True, "A previously scored HCN tail retains its old frozen body state during resume");
            Player.Resume();
            Assert.That(Playfield.ResumeRewindEndTime, Is.EqualTo(pauseTime));
            var target = Playfield.ResumeRewindStartTime;
            foreach (var time in new[] { target, pauseTime - 1, target, pauseTime - 1 })
            {
                session.Advance(time);
                Playfield.EndGameplayFrame();
                Playfield.UpdateSubTree();
                session.SynchronizeActivations();
                Assert.That(Playfield.IsResumeRewinding, Is.True);
                Assert.That(session.Context.GetLongNoteState(note), Is.EqualTo(completed));
                Assert.That(session.Output.GetMainResult(note), Is.EqualTo(result));
                Assert.That(JsonSerializer.Serialize(session.Score.JudgementEvents), Is.EqualTo(scores));
                Assert.That(JsonSerializer.Serialize(session.Health.GaugeHistory), Is.EqualTo(health));
                session.Mark("resume:" + time);
                captureVisual("resume:" + time);
            }
            var endpointCount = session.Score.ScoringJudgementEventCount;
            session.Traverse(pauseTime);
            Assert.That(Playfield.IsResumeRewinding, Is.False);
            session.Traverse(pauseTime + 1);
            Assert.That(PressColumn(session.Column, pauseTime + 1), Is.EqualTo(session.Recorded.HandleNotePress(new BmsPressContext(pauseTime + 1, session.Frame))));
            session.Mark("first-input-after-resume");
            session.Traverse(Math.Min(pauseTime + 202, 3999));
            session.Mark("body-after-resume");
            Assert.That(session.Score.ScoringJudgementEventCount, Is.EqualTo(endpointCount), "Resuming cannot submit either endpoint again");
            Assert.That(session.Context.GetLongNoteState(note).TailJudged, Is.True);
            if (pauseState == PauseState.Holding) Assert.That(session.Context.GetLongNoteState(note).HasActiveBody, Is.False);
            TestContext.WriteLine("P3A0_PAUSE " + JsonSerializer.Serialize(new { objectKind, pauseState, pauseTime, pauseVisuals, visuals }));
            session.ExtraTrace.AddRange(visuals);
            session.ExtraTrace.Add(JsonSerializer.Serialize(pauseVisuals));
            session.Finish("player-pause:" + pauseState + ":" + pauseTime);

            void captureVisual(string checkpoint)
            {
                var current = session.Container.AliveEntries.Values.OfType<DrawableBmsHitObject>().FirstOrDefault(d => ReferenceEquals(d.HitObject, note));
                visuals.Add(JsonSerializer.Serialize(new { checkpoint, bound = current != null, alpha = current?.Alpha, end = current?.LifetimeEnd,
                    bodyAlpha = current == null ? (float?)null : readField<osu.Framework.Graphics.Drawable>(current, "longNoteBody").Alpha,
                    tailAlpha = current == null ? (float?)null : readField<osu.Framework.Graphics.Drawable>(current, "longNoteTail").Alpha }));
            }
        }
    }

    private IDisposable observe(DrawableBmsHitObject drawable, Session session, Action<MethodBase, bool> callback, bool sampleStops = false)
    {
        observation = (target, method, exit) => { if (ReferenceEquals(target, drawable)) callback(method, exit); };
        var probes = new List<ScopedMethodProbe>();
        try
        {
            foreach (var method in new[] { drawable.GetType().GetMethod("OnApply", instance, Type.EmptyTypes)!, typeof(DrawableBmsHitObject).GetMethod("OnFree", instance, Type.EmptyTypes)!, drawable.GetType().GetMethod("OnKilled", instance)! })
                probes.Add(new ScopedMethodProbe(method.DeclaringType!.GetMethod(method.Name, instance, Type.EmptyTypes)!,
                    typeof(TestSceneBmsJudgementLifecycleExecution).GetMethod(nameof(observeEnter), BindingFlags.Static | BindingFlags.NonPublic),
                    typeof(TestSceneBmsJudgementLifecycleExecution).GetMethod(nameof(observeExit), BindingFlags.Static | BindingFlags.NonPublic)));
            // Earlier probe scopes can leave the base free callback inlined in these real callers.
            var free = drawable.GetType().GetMethod("OnFree", instance, Type.EmptyTypes)!;
            if (free.DeclaringType != typeof(DrawableBmsHitObject))
                probes.Add(new ScopedMethodProbe(free.DeclaringType!.GetMethod("OnFree", instance, Type.EmptyTypes)!,
                    GetType().GetMethod(nameof(keepBoundary), BindingFlags.Static | BindingFlags.NonPublic)));
            probes.Add(new ScopedMethodProbe(typeof(DrawableHitObject).GetMethod("OnFree", instance, [typeof(HitObjectLifetimeEntry)])!,
                GetType().GetMethod(nameof(keepBoundary), BindingFlags.Static | BindingFlags.NonPublic)));
            if (sampleStops)
            {
                probes.Add(new ScopedMethodProbe(typeof(DrawableHitObject).GetMethod("StopAllSamples", instance)!,
                    GetType().GetMethod(nameof(observeEnter), BindingFlags.Static | BindingFlags.NonPublic), GetType().GetMethod(nameof(observeExit), BindingFlags.Static | BindingFlags.NonPublic)));
                probes.Add(new ScopedMethodProbe(typeof(DrawableHitObject).GetMethod("OnKilled", instance)!, GetType().GetMethod(nameof(keepBoundary), BindingFlags.Static | BindingFlags.NonPublic)));
            }
        }
        catch { new ProbeScope(probes).Dispose(); throw; }
        return new ProbeScope(probes);
    }

    private sealed class ProbeScope(List<ScopedMethodProbe> probes) : IDisposable
    {
        public void Dispose() { observation = null; for (var i = probes.Count - 1; i >= 0; i--) probes[i].Dispose(); }
    }

    private static void observeEnter(object __instance, MethodBase __originalMethod) => observation?.Invoke(__instance, __originalMethod, false);
    private static void observeExit(object __instance, MethodBase __originalMethod) => observation?.Invoke(__instance, __originalMethod, true);
    private static T readField<T>(object target, string name) => (T)target.GetType().GetField(name, instance)!.GetValue(target)!;
    private static double? raw(JudgementResult result) => (double?)typeof(JudgementResult).GetProperty("RawTime", instance)!.GetValue(result);

    private static object? getRuntime(BmsColumnJudgementRuntime context, BmsHitObject note)
    {
        return BmsJudgementStateInspection.GetRuntime(context, note);
    }

    private static double passiveDeadline(BmsColumnJudgementRuntime context, BmsHitObject note)
    {
        var slot = readField<Dictionary<BmsHitObject, int>>(context, "noteSlots")[note];
        return note is BmsLongNote
            ? BmsJudgementStateInspection.GetHolds(context).HeadDeadline(slot)
            : context.NoteWindows[slot].NextPassiveJudgementTime(note);
    }

    [TestCase(Kind.CN)]
    [TestCase(Kind.HCN)]
    public void ScratchSubmissionAndStateRewindPreserveTheBaselineTimes(Kind objectKind)
    {
        load(objectKind, true);
        AddStep("verify full scratch rewind", () => withClock(run));

        void run(Session session)
        {
            var note = (BmsLongNote)session.Note;
            var context = session.Context;
            var output = session.Output;
            var recorded = session.Recorded;
            session.Advance(3003);
            PressColumn(session.Column, 3000);
            recorded.HandleNotePress(new BmsPressContext(3000, session.Frame));
            session.Mark("head");
            session.Traverse(3200);
            var beforeTail = session.Health.CurrentGaugeStates.ToArray();
            var beforeTailHistory = session.Health.GaugeHistory.Count;
            session.Advance(3997);
            ReleaseColumn(session.Column, 3990);
            recorded.HandleExecutionRelease(3990, session.Frame);
            PressColumn(session.Column, 3990, true);
            recorded.HandleNotePress(new BmsPressContext(3990, session.Frame, true));
            Assert.That(session.Submissions.Single().Time, Is.EqualTo(3997));
            Assert.That(output.SyntheticApplicationTimes.Single(), Is.EqualTo(3997));
            var tail = output.SyntheticEndpoints.Single();
            Assert.That(tail.EventTime, Is.EqualTo(3990));
            Assert.That(tail.ApplicationTime, Is.EqualTo(3990));
            Assert.That(tail.ExpectedTime, Is.EqualTo(4000));
            Assert.That(tail.TimeOffset, Is.EqualTo(-10));
            Assert.That(tail.GameplayRate, Is.EqualTo(1.5));
            Assert.That(context.GetLongNoteState(note).TailTime, Is.EqualTo(3990));
            Assert.That(session.Health.GaugeHistory[^1].Time, Is.EqualTo(3990));
            session.Mark("tail");
            session.Traverse(3997);
            Assert.That(session.Submissions, Has.Count.EqualTo(1));
            session.Traverse(3996);
            Assert.That(session.Submissions, Is.Empty);
            Assert.That(output.SyntheticEndpoints, Is.Empty);
            Assert.That(session.Drawable.Result.HasResult, Is.True);
            // ST-1: tail state uses event time while the result stack uses submission time.
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.True);
            Assert.That(context.GetLongNoteState(note).TailTime, Is.EqualTo(3990));
            Assert.That(session.Health.CurrentGaugeStates, Is.EqualTo(beforeTail));
            Assert.That(session.Health.GaugeHistory, Has.Count.EqualTo(beforeTailHistory));
            Assert.That(recorded.GetPendingLongNoteTails(), Is.Empty);
            Assert.That(context.GetPendingLongNoteTails(), Is.Empty);
            session.Mark("submission-reverted-state-retained");
            session.Traverse(3989);
            Assert.That(context.GetLongNoteState(note).TailJudged, Is.False);
            Assert.That(context.GetLongNoteState(note).IsHolding, Is.True);
            Assert.That(recorded.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
            Assert.That(context.GetPendingLongNoteTails(), Is.EqualTo(new[] { note }));
            session.Mark("event-reverted");
            session.Finish("ST-1");
        }
    }

    private void withClock(Action<Session> action)
    {
        var oldClock = Playfield.Clock;
        var oldScoreClock = Player.ScoreProcessor.Clock;
        var oldHealthClock = Player.HealthProcessor.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2798, IsRunning = true, Rate = 1.5 };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = clock;
        Player.HealthProcessor.Clock = clock;
        try
        {
            Playfield.EndGameplayFrame();
            Playfield.UpdateSubTree();
            var column = (BmsColumn)Playfield.Stage.Columns[columnIndex];
            action(new Session(this, column, manual, clock));
        }
        finally
        {
            Playfield.Clock = oldClock;
            Playfield.ProcessCustomClock = processClock;
            Player.ScoreProcessor.Clock = oldScoreClock;
            Player.HealthProcessor.Clock = oldHealthClock;
        }
    }

    private sealed class Session
    {
        private readonly TestSceneBmsJudgementLifecycleExecution scene;
        private readonly ManualClock manual;
        private readonly FramedClock clock;
        internal readonly BmsColumn Column;
        internal readonly BmsColumnHitObjectContainer Container;
        internal readonly BmsColumnJudgementRuntime Context;
        internal readonly BmsRecordingJudgementOutput Output = new();
        internal readonly BmsColumnJudgementRuntime Recorded;
        internal readonly BmsHitObject Note;
        internal readonly BmsJudgementResult Result;
        internal readonly BmsHealthProcessor Health;
        internal readonly BmsScoreProcessor Score;
        internal readonly List<(double Time, BmsLongNoteJudgementResult Result)> Submissions;
        internal BmsJudgementBridge ProductionOutput => Column.JudgementBridge;
        private readonly List<string> trace = [];
        private readonly List<string> scores = [];
        internal readonly List<string> ExtraTrace = [];
        private readonly HashSet<BmsHitObject> recordedActive = [];
        internal DrawableBmsHitObject Drawable => Container.AliveEntries.Values.OfType<DrawableBmsHitObject>().First(d => ReferenceEquals(d.HitObject, Note));
        internal BmsFrameContext Frame => new(manual.CurrentTime, manual.Rate);

        internal Session(TestSceneBmsJudgementLifecycleExecution scene, BmsColumn column, ManualClock manual, FramedClock clock)
        {
            this.scene = scene;
            this.manual = manual;
            this.clock = clock;
            Column = column;
            Context = column.Judgements;
            Container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            Note = scene.Playfield.Beatmap.HitObjects.First(n => n.Column == scene.columnIndex && n.StartTime == 3000);
            Result = (BmsJudgementResult)Drawable.Result;
            var notes = scene.Playfield.Beatmap.HitObjects.Where(n => n.Column == scene.columnIndex && n is BmsNote or BmsLongNote).ToArray();
            Recorded = new BmsJudgementEngine(notes, scene.Playfield.LayoutVariant, scene.columnIndex, !scene.mixed, Output).GetColumn(scene.columnIndex);
            foreach (var drawable in Container.AliveEntries.Values)
                if (drawable.HitObject is BmsNote or BmsLongNote)
                {
                    Recorded.ActivateNote((BmsHitObject)drawable.HitObject);
                    recordedActive.Add((BmsHitObject)drawable.HitObject);
                }
            Submissions = (List<(double Time, BmsLongNoteJudgementResult Result)>)typeof(BmsPlayfield).GetField("syntheticResults", instance)!.GetValue(scene.Playfield)!;
            Health = (BmsHealthProcessor)scene.Player.HealthProcessor;
            Health.SetGaugeTypes([BmsGaugeType.Normal, BmsGaugeType.Hard, BmsGaugeType.ExHard]);
            Score = (BmsScoreProcessor)scene.Player.ScoreProcessor;
        }

        internal void Advance(double time) { manual.CurrentTime = time; clock.ProcessFrame(); }

        internal void SynchronizeActivations()
        {
            var alive = Container.AliveEntries.Values.Select(d => d.HitObject).OfType<BmsHitObject>().Where(n => n is BmsNote or BmsLongNote).ToArray();
            foreach (var note in recordedActive.Where(n => !alive.Contains(n)).ToArray())
            {
                Recorded.BeginNoteRemoval(note);
                recordedActive.Remove(note);
            }
            foreach (var note in alive)
                if (recordedActive.Add(note)) Recorded.ActivateNote(note);
        }

        internal Action<DrawableHitObject, JudgementResult> CaptureResultCallback()
            => readField<Action<DrawableHitObject, JudgementResult>>(readField<IDictionary>(ProductionOutput, "boundObjects")[Drawable]!, "ResultHandler");

        internal void AssertOldNotificationIgnored(Action<DrawableHitObject, JudgementResult> oldCallback, DrawableBmsHitObject drawable, JudgementResult result, long generation)
        {
            var checkpoint = readField<Action<BmsHitObject>>(ProductionOutput, "resultCheckpoint");
            var historyCheckpoint = readField<Action<BmsHitObject, double>>(ProductionOutput, "resultHistoryCheckpoint");
            var calls = 0;
            ProductionOutput.SetResultCheckpoints(historyCheckpoint, note => { calls++; checkpoint(note); });
            try
            {
                oldCallback(drawable, result);
                ProductionOutput.UnbindNoteRepresentation(drawable, generation);
                Assert.That(calls, Is.Zero);
            }
            finally { ProductionOutput.SetResultCheckpoints(historyCheckpoint, checkpoint); }
        }

        internal void Traverse(double time)
        {
            var rewind = time < manual.CurrentTime;
            Advance(time);
            scene.Playfield.EndGameplayFrame();
            scene.Playfield.UpdateSubTree();
            if (rewind)
            {
                Output.RevertSyntheticAfter(time);
                Output.RevertAfter(time);
                Recorded.RewindLongNotes(time);
                Recorded.RewindPressHistory(time);
                Recorded.RestoreExecutionState(Frame);
            }
            else if (Note is BmsLongNote note)
            {
                Recorded.UpdateRegisteredBodies(Frame, clock.ElapsedFrameTime, Column.IsPressed);
                if (Container.AliveEntries.Values.Any(d => ReferenceEquals(d.HitObject, note)))
                {
                    Recorded.CheckRegisteredNotePassive(note, Frame);
                    Recorded.UpdateRegisteredLongNote(note, Frame, clock.ElapsedFrameTime, Column.IsPressed);
                }
                else
                {
                    Recorded.BeginNoteRemoval(note);
                    recordedActive.Remove(note);
                    Recorded.CheckRegisteredNotePassive(note, Frame);
                }
            }
            SynchronizeActivations();
        }

        internal void Mark(string checkpoint)
        {
            if (Note is BmsLongNote note)
            {
                Assert.That(Context.GetLongNoteState(note), Is.EqualTo(Recorded.GetLongNoteState(note)), checkpoint);
                if (scene.kind == Kind.HCN)
                {
                    var actualRuntime = (BmsHellChargeNoteRuntime)getRuntime(Context, note)!;
                    var recordedRuntime = (BmsHellChargeNoteRuntime)getRuntime(Recorded, note)!;
                    Assert.That(readField<double>(actualRuntime.Body, "accumulator"),
                        Is.EqualTo(readField<double>(recordedRuntime.Body, "accumulator")), checkpoint);
                }
                Assert.That(Context.GetPendingLongNoteTails(), Is.EqualTo(Recorded.GetPendingLongNoteTails()), checkpoint);
                Assert.That(Context.GetActiveBodies(), Is.EqualTo(Recorded.GetActiveBodies()), checkpoint);
                Assert.That(Context.GetHoldingNotes(), Is.EqualTo(Recorded.GetHoldingNotes()), checkpoint);
            }
            Assert.That(new BmsMainResultState(Result.Type, Result.TimeAbsolute, Result.TimeOffset,
                Result.GameplayRate, Result.SuppressPenalty), Is.EqualTo(Output.GetMainResult(Note)), checkpoint);
            trace.Add(JsonSerializer.Serialize(new { checkpoint, result = Output.GetMainResult(Note), state = Note is BmsLongNote n ? Context.GetLongNoteState(n) : (BmsLongNoteState?)null,
                submissions = Submissions.Select(s => s.Time), pending = Recorded.GetPendingLongNoteTails().Select(n => n.StartTime), bodies = Recorded.GetActiveBodies().Select(n => n.StartTime) }));
            scores.Add(JsonSerializer.Serialize(new { checkpoint, events = Score.JudgementEvents, combo = Score.Combo.Value, total = Score.TotalScore.Value,
                health = Health.GaugeHistory, states = Health.CurrentGaugeStates }));
        }

        internal void Finish(string scenario)
        {
            TestContext.WriteLine("P3A0_LIFECYCLE " + JsonSerializer.Serialize(new { scenario, scene.kind, scene.mixed, trace, scores }));
            TestContext.WriteLine("P3A0_LIFECYCLE_OBSERVATIONS " + JsonSerializer.Serialize(ExtraTrace));
        }
    }
}
