#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsDrawableTraversal
{
    private static DrawableBmsHitObject? childLifeNote;
    private static int childLifeFrameworkCalls;

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void StaticChildrenSkipScansAndRestoreFrameworkLifecycleWhenTheirStateChanges(BmsTestSkins.SkinKind skin)
    {
        loadSoundFixture(skin, BmsLongNoteMode.LongNote);
        AddStep("compare lifecycle transitions against framework", () =>
        {
            var note = soundFixtureObjects().Single(note => note.HitObject is BmsNote && note.HitObject is not BmsInvisibleNote);
            childLifeNote = note;
            childLifeFrameworkCalls = 0;
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
            var framework = typeof(CompositeDrawable).GetMethod("CheckChildrenLife", instance)!;
            // Emit a non-virtual call so the reference cannot dispatch back into the
            // candidate override and accidentally compare the candidate with itself.
            var reference = new DynamicMethod("FrameworkChildLifeReference", typeof(bool), [typeof(CompositeDrawable)],
                typeof(TestSceneBmsDrawableTraversal), true);
            var il = reference.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, framework);
            il.Emit(OpCodes.Ret);
            var frameworkCheck = reference.CreateDelegate<Func<CompositeDrawable, bool>>();
            var candidateCheck = typeof(DrawableBmsHitObject).GetMethod("CheckChildrenLife", instance | BindingFlags.DeclaredOnly)!
                .CreateDelegate<Func<DrawableBmsHitObject, bool>>();
            var children = typeof(CompositeDrawable).GetProperty("InternalChildren", instance)!.GetMethod!
                .CreateDelegate<Func<CompositeDrawable, IReadOnlyList<Drawable>>>();
            var add = typeof(CompositeDrawable).GetMethod("AddInternal", instance)!.CreateDelegate<Action<CompositeDrawable, Drawable>>();
            var remove = typeof(CompositeDrawable).GetMethod("RemoveInternal", instance)!.CreateDelegate<Func<CompositeDrawable, Drawable, bool, bool>>();
            var sprite = note.ChildrenOfType<BmsResolvedNoteSprite>().Single();
            var sound = frameworkSound(note);
            using var probe = new ScopedMethodProbe(framework,
                typeof(TestSceneBmsDrawableTraversal).GetMethod(nameof(countChildLifeFrameworkCall), BindingFlags.Static | BindingFlags.NonPublic)!);
            try
            {
                var initial = state();
                for (var frame = 0; frame < 32; frame++)
                {
                    Assert.That(candidateCheck(note), Is.False);
                    Assert.That(state(), Is.EqualTo(initial));
                }
                Assert.That(childLifeFrameworkCalls, Is.Zero, "Stable static children should not enter the framework scan.");
                for (var frame = 0; frame < 32; frame++)
                    Assert.That(frameworkCheck(note), Is.False);
                Assert.That(childLifeFrameworkCalls, Is.EqualTo(32));
                Assert.That(state(), Is.EqualTo(initial));

                // A future lifetime keeps the sprite attached while making it dead,
                // permitting the same original object to cross the boundary both ways.
                compareTransition(() => sprite.LifetimeStart = note.Time.Current + 1000);
                Assert.That(sprite.IsAlive, Is.False);
                compareTransition(() => sprite.LifetimeStart = double.MinValue);
                Assert.That(sprite.IsAlive, Is.True);

                compareTransition(() =>
                {
                    sound.LifetimeStart = double.MinValue;
                    sound.LifetimeEnd = double.MaxValue;
                });
                Assert.That(sound.IsAlive, Is.True);
                compareTransition(() => sound.LifetimeStart = sound.LifetimeEnd = double.PositiveInfinity);
                Assert.That(sound.IsAlive, Is.False);

                var extra = new Box();
                add(note, extra);
                var beforeExtra = childLifeFrameworkCalls;
                candidateCheck(note);
                Assert.That(childLifeFrameworkCalls, Is.EqualTo(beforeExtra + 1));
                Assert.That(extra.IsAlive, Is.True);
                remove(note, extra, true);

                note.ApplyNoteHeightScale(1.37f);
                var beforeWrapped = childLifeFrameworkCalls;
                candidateCheck(note);
                Assert.That(childLifeFrameworkCalls, Is.EqualTo(beforeWrapped + 1), "Retained skin composition must use the original check.");
                note.ApplyNoteHeightScale(1);
                note.UpdateSubTree();
                var final = state();
                var beforeStable = childLifeFrameworkCalls;
                for (var frame = 0; frame < 32; frame++)
                    Assert.That(candidateCheck(note), Is.False);
                Assert.That(childLifeFrameworkCalls, Is.EqualTo(beforeStable));
                Assert.That(state(), Is.EqualTo(final));
                TestContext.WriteLine($"STATIC_CHILD_LIFE skin={skin} stableChecks=64 frameworkEntries=0 referenceChecks=32 transitions=sprite,sound,extra,wrapper");

                object state() => children(note).Select(child => (child, child.IsAlive, child.Parent, child.LifetimeStart, child.LifetimeEnd)).ToArray();

                void compareTransition(Action change)
                {
                    change();
                    var before = childLifeFrameworkCalls;
                    candidateCheck(note);
                    Assert.That(childLifeFrameworkCalls, Is.EqualTo(before + 1));
                    var actual = state();
                    Assert.That(frameworkCheck(note), Is.False, "The unchanged framework must find no unprocessed transition.");
                    Assert.That(state(), Is.EqualTo(actual));
                }
            }
            finally
            {
                childLifeNote = null;
            }
        });
    }

    private static void countChildLifeFrameworkCall(CompositeDrawable __instance)
    {
        if (ReferenceEquals(__instance, childLifeNote))
            childLifeFrameworkCalls++;
    }
}
