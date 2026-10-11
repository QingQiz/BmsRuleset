#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Skinning.Legacy;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsDrawableTraversal
{
    private static BmsColumn? observedPulseColumn;
    private static readonly List<double> holdPulseTimes = [];
    private static double pulseSimulationTime;

    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.HellChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.HellChargeNote)]
    public void LongNoteHoldLightMatchesPresentationTimeline(BmsTestSkins.SkinKind skin, BmsLongNoteMode mode)
        => verifyHoldLightTimeline(skin, mode);

    private void verifyHoldLightTimeline(BmsTestSkins.SkinKind skin, BmsLongNoteMode mode)
    {
        AddStep("load long note", () => { this.skin = skin; this.mode = mode; LoadPlayer(); });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("seek before head", () => { Player.GameplayClockContainer.Stop(); Player.GameplayClockContainer.Seek(4800); });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddStep("compare hold pulse timeline", () =>
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var drawable = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().Single();
            var note = (BmsLongNote)drawable.HitObject;
            var callback = drawable.GetType().GetMethod("observeSimulationStep", flags)!;
            var lastTime = drawable.GetType().GetField("lastSimulationTime", flags)!;
            var lastPulse = drawable.GetType().GetField("lastHoldExplosionTime", flags)!;
            var previousClock = Playfield.Clock;
            var previousProcessClock = Playfield.ProcessCustomClock;
            var manual = new ManualClock { CurrentTime = 4800, IsRunning = true };
            var clock = new FramedClock(manual);
            clock.ProcessFrame();
            Playfield.Clock = clock;
            Playfield.ProcessCustomClock = false;
            observedLongNote = drawable;
            observedPulseColumn = column;
            using var readProbe = new ScopedMethodProbe(drawable.GetType().GetMethod("readPresentation", flags)!,
                GetType().GetMethod(nameof(countPresentation), statics)!);
            using var pulseProbe = new ScopedMethodProbe(typeof(BmsColumn).GetMethod(nameof(BmsColumn.TriggerHitExplosion))!,
                GetType().GetMethod(nameof(countHoldPulse), statics)!);
            try
            {
                Playfield.EndGameplayFrame();
                container.UpdateSubTree();
                runTimeline("future", [4800, 4800, 4999]);
                advance(5000);
                PressColumn(column, 5000);
                Assert.That(column.Judgements.GetLongNoteState(note).IsHolding, Is.True);
                runTimeline("held", [5000, 5000, 5001, 5079, 5080, 5080, 5081, 5159, 5160, 5200, 5240, 5241]);

                // Held input can change without publishing a new hold snapshot.
                column.Judgements.PressedDirections = 0;
                runTimeline("same state released input", [5242, 5319, 5320, 5320, 5400]);
                column.Judgements.PressedDirections = 1;
                runTimeline("same state repressed input", [5401, 5479, 5480, 6980]);
                advance(6980);
                ReleaseColumn(column, 6980);
                Assert.That(column.Judgements.GetLongNoteState(note).TailJudged, Is.True);
                runTimeline("judged tail", [6980, 6981, 7000, 7001, 7100]);

                advance(4800);
                column.Judgements.RewindLongNotes(4800);
                drawable.RestoreRewoundVisuals();
                column.Judgements.RestoreExecutionState(new BmsFrameContext(4800, 1));
                Assert.That(column.Judgements.GetLongNoteState(note).HeadJudged, Is.False);
                runTimeline("rewound", [4800, 4999, 5000, 5079, 5080]);
            }
            finally
            {
                observedLongNote = null;
                observedPulseColumn = null;
                holdPulseTimes.Clear();
                Playfield.Clock = previousClock;
                Playfield.ProcessCustomClock = previousProcessClock;
            }

            void advance(double time)
            {
                manual.CurrentTime = time;
                clock.ProcessFrame();
            }

            void runTimeline(string phase, double[] times)
            {
                var referenceLastTime = (double)lastTime.GetValue(drawable)!;
                var referenceLastPulse = (double)lastPulse.GetValue(drawable)!;
                var mapping = BmsLongNotePresentation.SelectMapping(mode);
                var expectedPulses = new List<double>();
                var dueReads = 0;
                var unguardedReads = 0;
                var state = column.Judgements.GetLongNoteState(note);
                var results = Player.Results.Count;
                var health = Player.HealthProcessor.Health.Value;
                var graphics = drawable.ChildrenOfType<Drawable>().Prepend(drawable).ToArray();
                var geometry = graphics.Select(d => (d.Position, d.Size, d.Scale, d.Alpha)).ToArray();
                presentationReads = 0;
                holdPulseTimes.Clear();
                foreach (var time in times)
                {
                    advance(time);
                    if (referenceLastTime != time)
                    {
                        referenceLastTime = time;
                        if (time >= note.StartTime && time <= note.EndTime)
                        {
                            unguardedReads++;
                            var current = mapping(note, state, new BmsLongNotePresentationContext(time, column.Judgements.PressedDirections > 0,
                                drawable.Judged, column.ResumeRewindEndTime));
                            if (time - referenceLastPulse >= BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION)
                                dueReads++;
                            // This is the original decision order, evaluated independently of the drawable.
                            if (current.HoldLightActive && time - referenceLastPulse >= BmsLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION)
                            {
                                expectedPulses.Add(time);
                                referenceLastPulse = time;
                            }
                        }
                    }
                    pulseSimulationTime = time;
                    callback.Invoke(drawable, [new BmsFrameContext(time, 1)]);
                    Assert.That(holdPulseTimes, Is.EqualTo(expectedPulses), $"{phase} at {time}");
                    Assert.That(lastPulse.GetValue(drawable), Is.EqualTo(referenceLastPulse), $"{phase} at {time}");
                }
                Assert.That(column.Judgements.GetLongNoteState(note), Is.EqualTo(state), phase);
                Assert.That(Player.Results.Count, Is.EqualTo(results), phase);
                Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(health), phase);
                Assert.That(graphics.Select(d => (d.Position, d.Size, d.Scale, d.Alpha)), Is.EqualTo(geometry), phase);
                TestContext.WriteLine($"HOLD_PULSES skin={skin} mode={mode} phase={phase} calls={times.Length} reads={presentationReads} originalReads={unguardedReads} dueReads={dueReads} pulses={holdPulseTimes.Count}");
            }
        });
    }

    private static void countHoldPulse(BmsColumn __instance, BmsHitLightStyle style)
    {
        if (ReferenceEquals(__instance, observedPulseColumn) && style == BmsHitLightStyle.Hold)
            holdPulseTimes.Add(pulseSimulationTime);
    }
}
