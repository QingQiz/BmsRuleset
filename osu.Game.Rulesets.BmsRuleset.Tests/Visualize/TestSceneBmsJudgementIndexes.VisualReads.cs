#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementIndexes
{
    private static readonly HashSet<HitObject> visualReadNotes = [];
    private static bool measuringVisualReads;
    private static int visualStartTimeReads;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void VisualLayoutReadsStartTimeOnlyWhenNeeded(bool constant, bool softConstant)
    {
        load(false, longNotes: false);
        seek(3000);
        AddStep("verify dense visual work and geometry", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var notes = container.AliveEntries.Values.OfType<DrawableBmsHitObject>()
                .Where(note => note.HitObject is BmsNote && note.HitObject.StartTime > 3000).ToArray();
            Assert.That(notes, Has.Length.GreaterThanOrEqualTo(future_note_count));
            var scroll = Playfield.ScrollController;
            var originalConstant = scroll.ConstantScrollActive;
            var originalSoftConstant = scroll.SoftConstant.Value;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var update = typeof(BmsColumnHitObjectContainer).GetMethod("UpdateAfterChildrenLife", flags)!.CreateDelegate<Action>(container);
            using var probe = new ScopedMethodProbe(typeof(HitObject).GetProperty(nameof(HitObject.StartTime))!.GetMethod!,
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countVisualStartTimeRead), BindingFlags.Static | BindingFlags.NonPublic)!);
            try
            {
                scroll.ConstantScrollActive = constant;
                scroll.SoftConstant.Value = softConstant;
                scroll.Update(3000);
                update();
                var geometry = notes.Select(note => (note.Y, note.VisualsSuppressed, note.DrawInfo, note.DrawColourInfo)).ToArray();
                var results = Player.Results.Count;
                var health = Player.HealthProcessor.Health.Value;
                visualReadNotes.UnionWith(notes.Select(note => note.HitObject));
                visualStartTimeReads = 0;
                measuringVisualReads = true;
                for (var frame = 0; frame < 32; frame++)
                    update();
                measuringVisualReads = false;
                TestContext.WriteLine($"VISUAL_START_TIME_READS constant={constant} softConstant={softConstant} entries={32 * notes.Length} reads={visualStartTimeReads}");
                Assert.That(visualStartTimeReads, Is.EqualTo(constant || softConstant ? 32 * notes.Length : 0));
                Assert.That(notes.Select(note => (note.Y, note.VisualsSuppressed, note.DrawInfo, note.DrawColourInfo)), Is.EqualTo(geometry));
                Assert.That(Player.Results.Count, Is.EqualTo(results));
                Assert.That(Player.HealthProcessor.Health.Value, Is.EqualTo(health));

                // A runtime timing change must be read on the next layout, without an added cache.
                var changed = notes[0];
                changed.HitObject.StartTime += 137;
                scroll.Update(3500);
                update();
                container.UpdateSubTree();
                var scale = scroll.ScrollCoordinateScale(container.DrawHeight, Playfield.Stage.HitTargetPosition);
                foreach (var note in notes.Where(note => !note.VisualsSuppressed))
                {
                    var position = constant ? note.HitObject.StartTime : note.HitObject.ScrollPositionAtStartTime;
                    var expectedY = -(Playfield.Stage.HitTargetPosition + (float)((position - scroll.CurrentScrollPosition) * scale));
                    Assert.That(note.Y, Is.EqualTo(expectedY));
                    if (softConstant)
                    {
                        var visual = note.ChildrenOfType<Drawable>().FirstOrDefault(child =>
                            ReferenceEquals(child.Parent, note)
                            && child is Container or BmsNoteVisual or osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.BmsResolvedNoteSprite);
                        Assert.That(visual, Is.Not.Null);
                        Assert.That(visual!.Alpha, Is.EqualTo(scroll.GetSoftConstantAlpha(note.HitObject.StartTime)));
                    }
                }
            }
            finally
            {
                measuringVisualReads = false;
                visualReadNotes.Clear();
                scroll.ConstantScrollActive = originalConstant;
                scroll.SoftConstant.Value = originalSoftConstant;
                scroll.Update(3000);
            }
        });
    }

    private static void countVisualStartTimeRead(HitObject __instance)
    {
        if (measuringVisualReads && visualReadNotes.Contains(__instance)) visualStartTimeReads++;
    }
}
