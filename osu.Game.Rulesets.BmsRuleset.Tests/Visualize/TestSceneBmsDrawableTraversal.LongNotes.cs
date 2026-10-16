#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsDrawableTraversal
{
    private static DrawableBmsHitObject? observedLongNote;
    private static int presentationReads;
    private static int tailProjections;
    private static int historyObservations;

    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.HellChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.HellChargeNote)]
    public void LongNoteTraversalReadsPresentationOnce(BmsTestSkins.SkinKind skin, BmsLongNoteMode mode)
    {
        AddStep("load long note", () =>
        {
            this.skin = skin;
            this.mode = mode;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("seek before head", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddStep("measure actual column traversal", () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            const BindingFlags counters = BindingFlags.Static | BindingFlags.NonPublic;
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var drawable = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single();
            var note = (BmsLongNote)drawable.HitObject;
            var oldClock = Playfield.Clock;
            var oldProcessClock = Playfield.ProcessCustomClock;
            var manual = new ManualClock { CurrentTime = 4800, IsRunning = true };
            var clock = new FramedClock(manual);
            clock.ProcessFrame();
            Playfield.Clock = clock;
            Playfield.ProcessCustomClock = false;
            observedLongNote = drawable;
            using var readProbe = new ScopedMethodProbe(drawable.GetType().GetMethod("readPresentation", flags)!,
                GetType().GetMethod(nameof(countPresentation), counters)!);
            using var tailProbe = new ScopedMethodProbe(drawable.GetType().GetMethod("tailY", flags)!,
                GetType().GetMethod(nameof(countTailProjection), counters)!);
            using var historyProbe = new ScopedMethodProbe(drawable.GetType().GetField("visualState", flags)!.FieldType
                .GetMethod("UpdateHeadYAtStartTime", BindingFlags.Public | flags)!, GetType().GetMethod(nameof(countHistoryObservation), counters)!);
            // Rebuild callers after installing leaf probes so warmed JIT inlines cannot hide work.
            var keepBoundary = GetType().GetMethod(nameof(keepLongNoteVisualBoundary), counters)!;
            using var frameProbe = new ScopedMethodProbe(drawable.GetType().GetMethod("UpdateVisualFrame", flags)!, keepBoundary);
            using var layoutProbe = new ScopedMethodProbe(drawable.GetType().GetMethod("PrepareVisual", flags)!, keepBoundary);
            using var observationProbe = new ScopedMethodProbe(drawable.GetType().GetMethods(flags)
                .Single(method => method.Name == "observeVisualTime" && method.GetParameters().Length == 4), keepBoundary);
            try
            {
                Playfield.EndGameplayFrame();
                container.UpdateSubTree();
                verifyStableTraversal("future");
                manual.CurrentTime = 5010;
                clock.ProcessFrame();
                container.UpdateSubTree();
                PressColumn(column, 5000);
                Assert.That(column.Judgements.GetLongNoteState(note).IsHolding, Is.True);
                manual.CurrentTime = 5200;
                clock.ProcessFrame();
                container.UpdateSubTree();
                verifyStableTraversal("held");

                var body = (Drawable)drawable.GetType().GetField("longNoteBody", flags)!.GetValue(drawable)!;
                var heldState = column.Judgements.GetLongNoteState(note);
                foreach (var directions in new[] { 0, 1, 0, 1 })
                {
                    column.Judgements.PressedDirections = directions;
                    container.UpdateSubTree();
                    Assert.That(body.Alpha, Is.EqualTo(directions == 0 ? 0.4f : 1f), "Same-time held input is read again without state publication.");
                    Assert.That(column.Judgements.GetLongNoteState(note), Is.EqualTo(heldState));
                }
                historyObservations = 0;
                manual.CurrentTime = 5201;
                clock.ProcessFrame();
                container.UpdateSubTree();
                Assert.That(historyObservations, Is.EqualTo(1), "A new simulation frame still records its visual history.");
                var observedFrame = ((double Time, double Offset, BmsColumnVisualFrame Frame)?)
                    drawable.GetType().GetField("lastVisualObservation", flags)!.GetValue(drawable);
                Assert.That(observedFrame!.Value.Time, Is.EqualTo(5201));

                var oldHeadPosition = note.ScrollPositionAtStartTime;
                var oldTailPosition = note.VisualScrollPositionAtEndTime;
                note.ScrollPositionAtStartTime = Playfield.ScrollController.CurrentScrollPosition + 1_000_000;
                note.VisualScrollPositionAtEndTime = note.ScrollPositionAtStartTime + 100;
                // Unpinning only the visual history lets both endpoints exercise culling while the rule remains held.
                drawable.GetType().GetField("visualState", flags)!.FieldType.GetMethod("Reset")!
                    .Invoke(drawable.GetType().GetField("visualState", flags)!.GetValue(drawable), null);
                column.Judgements.PressedDirections = 0;
                container.UpdateSubTree();
                Assert.That(drawable.VisualsSuppressed, Is.True);
                verifyStableTraversal("culled");

                note.ScrollPositionAtStartTime = oldHeadPosition;
                note.VisualScrollPositionAtEndTime = oldTailPosition;
                column.Judgements.PressedDirections = 1;
                var generation = drawable.BindingGeneration;
                note.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
                Assert.That(drawable.BindingGeneration, Is.EqualTo(generation + 1));
                container.UpdateSubTree();
                verifyStableTraversal("reapplied");

                var previousHistory = drawable.GetType().GetField("lastVisualObservation", flags)!.GetValue(drawable);
                var replacementObservations = 0;
                container.ObserveVisualTime(drawable, (float _, in BmsColumnVisualFrame _) => replacementObservations++);
                container.UpdateSubTree();
                Assert.That(replacementObservations, Is.EqualTo(1), "Replacing an observer retains the generic callback contract.");
                drawable.ClearVisualTimeObserver(drawable.BindingGeneration);
                container.UpdateSubTree();
                Assert.That(replacementObservations, Is.EqualTo(1));
                Assert.That(drawable.GetType().GetField("lastVisualObservation", flags)!.GetValue(drawable), Is.EqualTo(previousHistory));
            }
            finally
            {
                observedLongNote = null;
                Playfield.Clock = oldClock;
                Playfield.ProcessCustomClock = oldProcessClock;
            }

            void verifyStableTraversal(string phase)
            {
                var graphics = drawable.ChildrenOfType<Drawable>().Prepend(drawable).ToArray();
                var geometry = graphics.Select(d => (d.Position, d.Size, d.Scale, d.Alpha)).ToArray();
                var state = column.Judgements.GetLongNoteState(note);
                var results = Player.Results.Count;
                var health = Player.HealthProcessor.Health.Value;
                presentationReads = 0;
                tailProjections = 0;
                historyObservations = 0;
                for (var i = 0; i < 32; i++)
                    container.UpdateSubTree();
                Assert.That(presentationReads, Is.EqualTo(32), phase);
                Assert.That(tailProjections, Is.EqualTo(32), phase);
                Assert.That(historyObservations, Is.Zero, "Revisiting the same frame does not extend visual history.");
                Assert.That(graphics.Select(d => (d.Position, d.Size, d.Scale, d.Alpha)), Is.EqualTo(geometry), phase);
                Assert.That(column.Judgements.GetLongNoteState(note), Is.EqualTo(state), phase);
                Assert.That(Player.Results.Count, Is.EqualTo(results), phase);
                Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(health), phase);
                TestContext.WriteLine($"LONG_NOTE_VISUAL_READS skin={skin} mode={mode} phase={phase} frames=32 reads={presentationReads} tails={tailProjections} history={historyObservations}");
            }
        });
    }

    private static void countPresentation(object __instance)
    {
        if (ReferenceEquals(__instance, observedLongNote)) presentationReads++;
    }

    private static void countTailProjection(object __instance)
    {
        if (ReferenceEquals(__instance, observedLongNote)) tailProjections++;
    }

    private static void countHistoryObservation() => historyObservations++;

    private static void keepLongNoteVisualBoundary() { }
}
