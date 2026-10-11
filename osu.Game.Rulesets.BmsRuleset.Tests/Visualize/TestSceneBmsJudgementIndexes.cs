#nullable enable
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.State;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
[NonParallelizable]
[Category("JudgementInstrumentation")]
public partial class TestSceneBmsJudgementIndexes : BmsPlayerTestScene
{
    private const int future_note_count = 128;
    private bool includeMine;
    private bool includeLongNotes;
    private static int repressCalls;
    private static int unrelatedBodyCalls;
    private static int layoutDepth;
    private static int layoutCalls;
    private static int layoutBindingCalls;
    private static int unrelatedLayoutBindingCalls;
    private static int layoutEntries;
    private static int unrelatedLayoutEntries;
    private static int snapshotRebuilds;
    private static int snapshotEntries;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void LayoutBindingChecksOnlyVisitLongNotes(bool mine, bool longNotes)
    {
        load(mine, longNotes: longNotes);
        seek(3000);
        AddStep("layout skips unrelated bindings", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Values.Count(d => d.HitObject is BmsNote), Is.GreaterThan(64));
            if (longNotes)
                Assert.That(container.AliveEntries.Values.Count(d => d.HitObject is BmsLongNote), Is.GreaterThan(64));
            else
                Assert.That(container.AliveEntries.Values.Count(d => d.HitObject is BmsLongNote), Is.Zero);
            using var layoutProbe = new ScopedMethodProbe(typeof(BmsCoreJudgementSchedule).GetMethod("RunLayout", BindingFlags.Instance | BindingFlags.NonPublic)!,
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(beginLayout), BindingFlags.Static | BindingFlags.NonPublic)!,
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(endLayout), BindingFlags.Static | BindingFlags.NonPublic)!);
            using var bindingProbe = new ScopedMethodProbe(typeof(BmsCoreJudgementSchedule).GetMethod("current", BindingFlags.Instance | BindingFlags.NonPublic)!,
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countLayoutBinding), BindingFlags.Static | BindingFlags.NonPublic)!);
            layoutDepth = layoutCalls = layoutBindingCalls = unrelatedLayoutBindingCalls = 0;
            layoutEntries = unrelatedLayoutEntries = 0;
            container.Judgements.Schedule.BeginBatch(0, 1000, 1000);
            container.UpdateSubTree();

            Assert.That(layoutDepth, Is.Zero);
            Assert.That(layoutCalls, Is.EqualTo(1));
            Assert.That(unrelatedLayoutEntries, Is.Zero, "The cached layout order must contain only long notes.");
            if (longNotes)
            {
                Assert.That(layoutEntries, Is.GreaterThan(64));
                Assert.That(layoutBindingCalls, Is.GreaterThan(64));
            }
            else
            {
                Assert.That(layoutEntries, Is.Zero);
                Assert.That(layoutBindingCalls, Is.Zero);
            }

            Assert.That(unrelatedLayoutBindingCalls, Is.Zero, "Tap and mine bindings have no post-layout operation.");
            TestContext.WriteLine($"LAYOUT_BINDINGS mine={mine} longNotes={longNotes} entries={layoutEntries} calls={layoutBindingCalls} unrelated={unrelatedLayoutBindingCalls}");
        });
    }

    [Test]
    public void RebuildingScheduleOrderReusesExistingBindings()
    {
        load(false);
        seek(3000);
        AddStep("rebuild order without allocating bindings", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Count, Is.GreaterThan(128));
            var refresh = typeof(BmsColumnHitObjectContainer).GetMethod("refreshCoreOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(container);
            for (var i = 0; i < 32; i++)
            {
                container.InvalidateCoreOrder();
                refresh();
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 32; i++)
            {
                container.InvalidateCoreOrder();
                refresh();
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Assert.That(allocated, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StableColumnFramesDoNotCopyScheduleOrder(bool longNotes)
    {
        load(true, longNotes: longNotes);
        seek(3000);
        AddStep("copy schedule order only after membership changes", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Count, Is.GreaterThan(128));
            container.Judgements.Schedule.BeginBatch(0, 1000, 1000);
            container.UpdateSubTree();
            var snapshotMethod = typeof(BmsCoreJudgementSchedule).Assembly
                                     .GetType("osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.BmsScheduledOrder")
                                     ?.GetMethod("refreshSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)
                                 ?? typeof(BmsCoreJudgementSchedule).GetMethod("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var probe = new ScopedMethodProbe(snapshotMethod,
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countSnapshotRebuild), BindingFlags.Static | BindingFlags.NonPublic)!);
            snapshotRebuilds = snapshotEntries = 0;

            for (var i = 0; i < 32; i++)
            {
                container.Judgements.Schedule.BeginBatch(0, 1001 + i, 1001 + i);
                container.UpdateSubTree();
            }

            Assert.That(snapshotRebuilds, Is.Zero);
            Assert.That(snapshotEntries, Is.Zero);

            container.InvalidateCoreOrder();
            container.Judgements.Schedule.BeginBatch(0, 2000, 2000);
            container.UpdateSubTree();
            const int maximumRebuilds = 2;
            Assert.That(snapshotRebuilds, Is.InRange(1, maximumRebuilds),
                "A membership change must publish the changed cached order before the next stable frame.");
            var layoutOrder = (IReadOnlyList<BmsScheduledObject>)typeof(BmsColumnHitObjectContainer)
                .GetField("coreLayoutOrder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(container)!;
            var subtreeOrder = (IReadOnlyList<BmsScheduledObject>)typeof(BmsColumnHitObjectContainer)
                .GetField("coreSubtreeOrder", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(container)!;
            Assert.That(subtreeOrder.Count, Is.GreaterThanOrEqualTo(future_note_count));
            Assert.That(snapshotEntries, Is.GreaterThan(0));
            Assert.That(snapshotEntries, Is.LessThanOrEqualTo(layoutOrder.Count + subtreeOrder.Count));
            var rebuiltEntries = snapshotEntries;
            var rebuiltRebuilds = snapshotRebuilds;

            for (var i = 0; i < 32; i++)
            {
                container.Judgements.Schedule.BeginBatch(0, 2001 + i, 2001 + i);
                container.UpdateSubTree();
            }

            Assert.That(snapshotRebuilds, Is.EqualTo(rebuiltRebuilds));
            Assert.That(snapshotEntries, Is.EqualTo(rebuiltEntries));
            TestContext.WriteLine($"SCHEDULE_SNAPSHOTS longNotes={longNotes} stableFrames=64 rebuilds={snapshotRebuilds} entries={snapshotEntries}");
        });
    }

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(_ => [new BmsReplayFrame(0), new BmsReplayFrame(11000)]);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2,
            Total = (3 + 3 * future_note_count) * 4,
            LockedLongNoteMode = BmsLongNoteMode.HellChargeNote,
            HitObjects = includeLongNotes ? [new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1 }] : [],
        };
        if (includeMine)
            beatmap.HitObjects.Add(new BmsLandmine { StartTime = 3201, Column = 1, LandmineDamagePercent = 10 });
        if (orderingFixture)
        {
            for (var i = 0; i < 4; i++)
            {
                beatmap.HitObjects.Add(new BmsLandmine { StartTime = 5100, Column = 1 });
            }

            beatmap.InvisibleNotes = Enumerable.Range(0, 4).Select(_ => new BmsInvisibleNote { StartTime = 5100, Column = 1 }).ToArray();
        }

        for (var i = 0; i < future_note_count; i++)
        {
            if (includeLongNotes)
                beatmap.HitObjects.Add(new BmsLongNote { StartTime = 5000 + i, Duration = 500, Column = 1 });
            beatmap.HitObjects.Add(new BmsNote { StartTime = 5200 + i, Column = 1 });
        }

        beatmap.HitObjects.Add(new BmsNote { StartTime = 10000, Column = 2 });
        beatmap.HitObjects = beatmap.HitObjects.OrderBy(h => h.StartTime).ToList();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HeadAndRepressIgnoreFutureLongNotes(bool mine)
    {
        load(mine);
        seek(3000);
        AddStep("head and active hold use the same judgement with bounded work", () =>
        {
            var column = Playfield.Stage.Columns[1];
            Assert.That(column.HitObjectContainer.AliveEntries.Values.Count(d => d.HitObject is BmsLongNote), Is.GreaterThan(64));
            using var probe = new ScopedMethodProbe(typeof(BmsHellChargeNoteJudgementRule).GetMethod("TryRepress", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countRepress), BindingFlags.Static | BindingFlags.NonPublic));

            repressCalls = 0;
            Assert.That(PressColumn(column, 3000), Is.EqualTo(PressOutcome.Hit));
            Assert.That(repressCalls, Is.Zero, "An exact head press must not query unstarted future holds.");
            Assert.That(PressColumn(column, 3001), Is.EqualTo(PressOutcome.Hit));
            Assert.That(repressCalls, Is.EqualTo(1), "Only the active hold participates in a repress.");
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Perfect }));
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.24).Within(1e-8));
        });
    }

    [Test]
    public void MineColumnBodyPassSkipsTapsAndUnstartedHolds()
    {
        load(true);
        seek(3000);
        AddStep("start HCN", () => Assert.That(PressColumn(Playfield.Stage.Columns[1], 3000), Is.EqualTo(PressOutcome.Hit)));
        seek(3200);
        AddStep("body phase work depends on active holds", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Count, Is.GreaterThan(128));
            using var holdProbe = new ScopedMethodProbe(typeof(BmsHellChargeNoteJudgementRule).GetMethod("UpdateBody", BindingFlags.Instance | BindingFlags.NonPublic),
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countUnstartedBody), BindingFlags.Static | BindingFlags.NonPublic));
            unrelatedBodyCalls = 0;
            for (var i = 0; i < 20; i++)
            {
                var frame = new BmsFrameContext(container.Time.Current, 1);
                container.Judgements.UpdateLandmines(frame, true);
                container.Judgements.UpdateRegisteredBodies(frame, container.Time.Elapsed, true);
            }

            Assert.That(unrelatedBodyCalls, Is.Zero, "The body phase must not visit taps, mines or unstarted long notes.");
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.24).Within(1e-8), "Repeated passes at one timestamp must not tick.");
        });
        seek(3201);
        AddStep("mine still precedes the strict body tick", () =>
        {
            Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(.16).Within(1e-8));
            Assert.That(((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.Select(e => e.Source.Kind),
                Is.EqualTo(new[] { BmsJudgementSourceKind.LongNote, BmsJudgementSourceKind.Landmine }));
        });
    }

    private void load(bool mine, bool ordering = false, bool longNotes = true)
    {
        AddStep("load dense column", () =>
        {
            includeMine = mine;
            includeLongNotes = longNotes;
            orderingFixture = ordering;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("show future notes", () =>
        {
            Playfield.ScrollController.SetConfiguredScrollSpeed(1);
            Playfield.RefreshAllLifetimes();
        });
    }

    private void seek(double time)
    {
        AddStep("seek " + time, () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("reached " + time, () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
    }

    private static void countRepress() => repressCalls++;

    private static void countSnapshotRebuild(object __instance, object[] __args)
    {
        snapshotRebuilds++;
        var order = __instance as IReadOnlyList<BmsScheduledObject> ?? (IReadOnlyList<BmsScheduledObject>)__args[0];
        snapshotEntries += order.Count;
    }

    private static void beginLayout(IReadOnlyList<BmsScheduledObject> order)
    {
        layoutDepth++;
        layoutCalls++;
        layoutEntries += order.Count;
        for (var i = 0; i < order.Count; i++)
        {
            if (order[i].Object is not BmsLongNote)
                unrelatedLayoutEntries++;
        }
    }

    private static void endLayout() => layoutDepth--;

    private static void countLayoutBinding(BmsScheduledObject item)
    {
        if (layoutDepth == 0)
            return;

        layoutBindingCalls++;
        if (item.Object is not BmsLongNote)
            unrelatedLayoutBindingCalls++;
    }

    private static void countUnstartedBody(BmsHellChargeNoteRuntime state)
    {
        if (!state.Hold.HeadJudged)
            unrelatedBodyCalls++;
    }
}
