#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Framework.Input.Events;
using osu.Framework.Input.States;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsJudgementCheckpoints : BmsPlayerTestScene
{
    private BmsLongNoteMode mode;
    private bool replayInputs;
    private BmsCoreSchedulingRecorder? recorder;

    protected override TestPlayer CreatePlayer(Ruleset ruleset) => CreateBmsPlayer(_ => replayInputs
        ? [new BmsReplayFrame(0), new BmsReplayFrame(3000, BmsAction.Key1), new BmsReplayFrame(3001),
            new BmsReplayFrame(3002, BmsAction.Key1), new BmsReplayFrame(3003), new BmsReplayFrame(60000)]
        : [new BmsReplayFrame(0), new BmsReplayFrame(60000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2, Total = 24,
            LockedLongNoteMode = mode,
            HitObjects =
            [
                new BmsNote { StartTime = 3000, Column = 0, SampleKey = 11, SampleVolume = 80 },
                new BmsNote { StartTime = 3000, Column = 1, SampleKey = 12, SampleVolume = 70 },
                new BmsLongNote { StartTime = 3001, Duration = 300, Column = 0, SampleKey = 13, SampleVolume = 60, TailSampleKey = 14, TailSampleVolume = 50 },
                new BmsNote { StartTime = 3010, Column = 1 },
                new BmsNote { StartTime = 30000, Column = 2 },
            ],
        };
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void FixedClockRecordsOrdinaryAndDeferredCheckpoints(BmsLongNoteMode longNoteMode)
    {
        string[]? ordinaryObservations = null;
        foreach (var deferred in new[] { false, true })
        {
            load(longNoteMode);
            AddStep("record fixed production traversal", () => withClock(advance =>
            {
                startRecording();
                if (deferred) Playfield.BeginGameplayFrame();
                advance(2999);
                Playfield.UpdateSubTree();
                advance(3000);
                press();
                Playfield.UpdateSubTree();
                advance(3001);
                release();
                Playfield.UpdateSubTree();
                advance(3002);
                Playfield.UpdateSubTree();
                advance(3003);
                Playfield.UpdateSubTree();
                if (deferred) Playfield.EndGameplayFrame();
                recorder!.Mark("burst.complete");

                var scratchTimes = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents
                    .Where(e => e.Source.Column == 0).SelectMany(e => e.TimingObservations).Select(o => o.ActualTime).ToArray();
                Assert.That(scratchTimes[0], Is.EqualTo(3000), "AS-1: automation consumes each simulation step regardless of visual deferral.");
                var holding = (BmsLongNote)Playfield.GetAliveObjectAtTime(3001)!.HitObject;
                var context = ((BmsColumn)Playfield.Stage.Columns[holding.Column]).Judgements;
                Assert.That(context.PressedDirections, Is.EqualTo(1));
                Assert.That(context.GetLongNoteState(holding).HeadTime, Is.EqualTo(3001));

                double[] times = [3202, 3301, 3605, 2999, 3000, 3001, 3301];
                foreach (var time in times)
                {
                    advance(time);
                    Playfield.UpdateSubTree();
                }
                recorder.Mark("scenario.complete");
                var rows = recorder.Rows.Select(r => r.Split('|')).ToArray();
                var tapSample = Array.FindIndex(rows, r => r[4].EndsWith("/keysound>") && r[11].Contains("sampleKey=11"));
                var tapResult = Array.FindIndex(rows, r => r[4].EndsWith("/result.notify=") && r[8] == "0");
                Assert.That(tapResult, Is.GreaterThanOrEqualTo(0).And.LessThan(tapSample));
                if (longNoteMode != BmsLongNoteMode.LongNote)
                {
                    var headResult = Array.FindIndex(rows, r => r[4].EndsWith("/result.notify=") && r[8] == "2");
                    var headSample = Array.FindIndex(rows, r => r[4].EndsWith("/keysound>") && r[11].Contains("sampleKey=13"));
                    Assert.That(headResult, Is.GreaterThanOrEqualTo(0).And.LessThan(headSample));
                }
                if (longNoteMode == BmsLongNoteMode.HellChargeNote)
                    Assert.That(rows.Count(r => r[4].EndsWith("/body.tick>") && r[5] == "3202"), Is.EqualTo(1));
                var observations = observableRows(recorder.Rows);
                if (deferred)
                    Assert.That(observations, Is.EqualTo(ordinaryObservations), "Ordered results, endpoints, keysounds and intermediate health are independent of visual grouping.");
                else
                    ordinaryObservations = observations;
                finishRecording($"traversal-{longNoteMode}-{deferred}");
            }));
        }
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void ApplyDefaultsAndRewindObserveTheRealFrameworkCallbacks(BmsLongNoteMode longNoteMode)
    {
        load(longNoteMode);
        AddStep("reapply a held note and rewind its results", () => withClock(advance =>
        {
            startRecording();
            advance(3001);
            Playfield.UpdateSubTree();
            var drawable = (DrawableBmsHitObject)Playfield.GetAliveObjectAtTime(3001)!;
            var note = drawable.HitObject;
            var entry = drawable.Entry;
            var context = ((BmsColumn)Playfield.Stage.Columns[note.Column]).Judgements;
            var state = context.GetLongNoteState((BmsLongNote)note);
            var events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Count;

            recorder!.Mark("reapply.before");
            var reapplyStart = recorder.Rows.Count;
            note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            recorder.Mark("reapply.after");
            Assert.That(drawable.Entry, Is.SameAs(entry));
            Assert.That(context.GetLongNoteState((BmsLongNote)note), Is.EqualTo(state));
            Assert.That(state.HeadJudged, Is.True);
            Assert.That(context.PressedDirections, Is.EqualTo(1), "Reapplying a drawable preserves framework-held input.");
            Assert.That(drawable.Result.TimeOffset, Is.Zero, "The framework reapplies the main result time at the end of the object.");
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents, Has.Count.EqualTo(events));
            Assert.That(recorder.Rows.Skip(reapplyStart).Any(r => r.Contains("/representation.killed")), Is.False, "ApplyDefaults must preserve the lifetime registration.");
            Assert.That(context.ActiveLongNotes.Count(n => ReferenceEquals(n, note)), Is.EqualTo(1));

            advance(3301);
            Playfield.UpdateSubTree();
            advance(2999);
            Playfield.UpdateSubTree();
            recorder.Mark("rewind.restored");
            Assert.That(context.FirstPendingNote?.StartTime, Is.EqualTo(3000));
            assertRevertObservations(requireDrawable: true);
            advance(3000);
            Playfield.UpdateSubTree();
            recorder.Mark("replay.press");
            finishRecording($"lifecycle-{longNoteMode}");
        }));
    }

    [Test]
    public void OrdinaryNoteReapplyKeepsItsCandidateAndResultIdentity()
    {
        load(BmsLongNoteMode.LongNote);
        AddStep("reapply an unjudged ordinary note", () => withClock(advance =>
        {
            startRecording();
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var note = column.Judgements.FirstPendingNote!;
            var drawable = column.JudgementBridge.GetRepresentation(note);
            var result = drawable.Result;
            var entry = drawable.Entry;
            drawable.HitObject.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
            Assert.That(drawable.Entry, Is.SameAs(entry));
            Assert.That(drawable.Result, Is.SameAs(result));
            Assert.That(column.Judgements.FirstPendingNote, Is.SameAs(note));
            Assert.That(recorder!.Rows.Any(r => r.Contains("/representation.killed")), Is.False);
            advance(3000);
            press();
            Playfield.UpdateSubTree();
            advance(2999);
            Playfield.UpdateSubTree();
            recorder.Mark("rewind.restored");
            Assert.That(column.Judgements.FirstPendingNote?.StartTime, Is.EqualTo(3000));
            assertRevertObservations(requireDrawable: false);
            finishRecording("lifecycle-note");
        }));
    }

    [Test]
    public void ParentInputAndRepeatedTimestampKeepSeparateBatches()
    {
        load(BmsLongNoteMode.HellChargeNote);
        AddStep("flush parent input before advancing simulation", () => withClock(advance =>
        {
            startRecording();
            advance(3000);
            press();
            Playfield.BeginGameplayFrame();
            advance(3001);
            release();
            Playfield.UpdateSubTree();
            press();
            Playfield.UpdateSubTree();
            release();
            Playfield.UpdateSubTree();
            Playfield.EndGameplayFrame();
            var rows = recorder!.Rows.Select(r => r.Split('|')).ToArray();
            Assert.That(rows.Single(r => r[4] == "frame.begin/batch>")[5], Is.EqualTo("3000"));
            Assert.That(rows.Count(r => r[4] == "simulation/batch>" && r[5] == "3001"), Is.EqualTo(3));
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.First().TimingObservations.Single().ActualTime, Is.EqualTo(3000));
            finishRecording("parent-input-batches");
        }));
    }

    [Test]
    public void ReplayCatchUpUsesProductionFrameBoundaries()
    {
        load(BmsLongNoteMode.HellChargeNote, true);
        AddStep("observe replay catch-up", () =>
        {
            startRecording();
            Player.GameplayClockContainer.Seek(3004);
        });
        AddUntilStep("replay reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 3004) < .001);
        AddStep("check actual replay scheduling", () =>
        {
            recorder!.Recording = false;
            var rows = recorder.Rows.Select(r => r.Split('|')).ToArray();
            TestContext.WriteLine("P3A0_REPLAY_ROWS " + JsonSerializer.Serialize(recorder.Rows));
            string[] inputTimes = ["3000", "3001", "3002", "3003"];
            var manualInputs = rows.Where(r => r[4].EndsWith("/input.queue>") && r[11].Contains("column=1")).ToArray();
            Assert.That(manualInputs.Select(r => r[5]), Is.EqualTo(inputTimes));
            var generatedInputs = rows.Where(r => r[4].EndsWith("/input.queue>") && r[11].Contains("column=0")).ToArray();
            Assert.That(generatedInputs.Select(r => r[5]), Is.EqualTo(new[] { "3000", "3001", "3001" }));
            Assert.That(rows.Any(r => r[4].EndsWith("/column.deferred>")), Is.True);
            Assert.That(rows.Any(r => r[4] == "frame.end/playfield.update>"), Is.True);
            TestContext.WriteLine("P3A0_REPLAY " + JsonSerializer.Serialize(new
            {
                inputs = rows.Where(r => r[4].EndsWith("/input.queue>")).Select(r => new { frame = r[1], step = r[2], time = r[5], args = r[11] }),
                generated = generatedInputs.Select(r => new { frame = r[1], step = r[2], time = r[5], state = r[12] }),
                events = ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents,
            }));
            stopRecording();
        });
    }

    private void load(BmsLongNoteMode longNoteMode, bool inputs = false)
    {
        AddStep("load checkpoint chart", () =>
        {
            stopRecording();
            mode = longNoteMode;
            replayInputs = inputs;
            LoadPlayer([new BmsModAutoScratch()]);
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        AddStep("stop before measured steps", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(2998); });
        AddUntilStep("simulation reached start", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 2998) < .001);
    }

    private void withClock(Action<Action<double>> action)
    {
        var previousClock = Playfield.Clock;
        var scoreClock = Player.ScoreProcessor.Clock;
        var healthClock = Player.HealthProcessor.Clock;
        var processClock = Playfield.ProcessCustomClock;
        var manual = new ManualClock { CurrentTime = 2998, IsRunning = true, Rate = 1 };
        var clock = new FramedClock(manual);
        clock.ProcessFrame();
        Playfield.Clock = clock;
        Playfield.ProcessCustomClock = false;
        Player.ScoreProcessor.Clock = clock;
        Player.HealthProcessor.Clock = clock;
        try
        {
            // Close the host frame before controlling traversal grouping; simulation times stay identical.
            Playfield.EndGameplayFrame();
            Playfield.UpdateSubTree();
            var mod = Player.DrawableRuleset.Mods.OfType<BmsModAutoScratch>().Single();
            action(time => { manual.CurrentTime = time; clock.ProcessFrame(); mod.UpdateActions(time); });
        }
        finally
        {
            stopRecording();
            Playfield.EndGameplayFrame();
            Playfield.Clock = previousClock;
            Playfield.ProcessCustomClock = processClock;
            Player.ScoreProcessor.Clock = scoreClock;
            Player.HealthProcessor.Clock = healthClock;
        }
    }

    private void press() => Playfield.OnPressed(new KeyBindingPressEvent<BmsAction>(new InputState(), BmsAction.Key1));
    private void release() => Playfield.OnReleased(new KeyBindingReleaseEvent<BmsAction>(new InputState(), BmsAction.Key1));

    private void startRecording()
    {
        recorder = new BmsCoreSchedulingRecorder(Playfield, (BmsScoreProcessor)Player.ScoreProcessor, (BmsHealthProcessor)Player.HealthProcessor)
        {
            Recording = true,
        };
    }

    private void assertRevertObservations(bool requireDrawable)
    {
        var rows = recorder!.Rows.Select(r => r.Split('|')).ToArray();
        var notifications = rows.Where(r => r[4].EndsWith("/result.externalRevert=")).ToArray();
        Assert.That(notifications, Is.Not.Empty, "Observe the framework revert callback even after pool return.");
        Assert.That(notifications.All(r => !r[13].StartsWith("None@")), Is.True, "The callback still sees the old main result.");
        var drawableNotifications = rows.Where(r => r[4].EndsWith("/result.drawableRevert=")).ToArray();
        if (requireDrawable)
            Assert.That(drawableNotifications, Is.Not.Empty, "Also observe the callback on a still-bound Drawable.");
        Assert.That(drawableNotifications.All(r => !r[13].StartsWith("None@")), Is.True);
        // Observe the caller after Reset: patching the tiny Reset method can miss an inlined call.
        var resets = rows.Where(r => r[4].EndsWith("/result.revert<")).ToArray();
        Assert.That(resets, Is.Not.Empty);
        Assert.That(resets.All(r => r[13].StartsWith("None@")), Is.True);
        Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.2).Within(1e-8), "Restore gauge history after all column result stacks.");
    }

    private void finishRecording(string scenario)
    {
        recorder!.Recording = false;
        TestContext.WriteLine("P3A0_CHECKPOINTS " + JsonSerializer.Serialize(new { scenario, rows = recorder.Rows }));
        using var stream = GetType().Assembly.GetManifestResourceStream("osu.Game.Rulesets.BmsRuleset.Tests.reference_fixtures.judgement_p3_checkpoints.json")
                           ?? throw new InvalidDataException("Missing P3 checkpoint baseline.");
        using var baseline = JsonDocument.Parse(stream);
        Assert.That(baseline.RootElement.GetProperty("traces").TryGetProperty(scenario, out var expected), Is.True, "Missing old-path scenario " + scenario);
        var historical = expected.EnumerateArray().Select(r => r.GetString()!.Split('|')).ToArray();
        if (scenario.StartsWith("traversal-"))
        {
            var wasDeferred = scenario.EndsWith("True");
            Assert.That(historical.First(r => r[4].EndsWith("/result.notify=") && r[8] == "0")[5],
                Is.EqualTo(wasDeferred ? "3003" : "3000"), "Preserve the original AS-1 evidence instead of replacing its fixture.");
            if (mode == BmsLongNoteMode.HellChargeNote)
                Assert.That(historical.Count(r => r[4].EndsWith("/body.tick>") && r[5] == "3202"), Is.EqualTo(wasDeferred ? 0 : 1));
        }
        // Lifecycle behavior is asserted above against normal framework inputs; the old AS fixture is historical evidence.
    }

    private static string[] observableRows(System.Collections.Generic.IEnumerable<string> rows)
        => rows.Select(r => r.Split('|'))
               .Where(r => r[4].EndsWith("/result.notify=") || r[4].EndsWith("/endpoint>") || r[4].EndsWith("/endpoint<")
                           || r[4].EndsWith("/keysound>") || r[4].EndsWith("/body.tick>") || r[4].EndsWith("/body.tick<"))
               .Select(r => string.Join('|', r[4].Split('/')[^1], r[5], r[7], r[8], r[11], r[12], r[13], r[16])).ToArray();

    [TearDown]
    public void StopCheckpointProbes() => stopRecording();

    private void stopRecording()
    {
        recorder?.Dispose();
        recorder = null;
    }
}
