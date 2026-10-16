#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osuTK;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsDrawableTraversal
{
    private static readonly HashSet<Drawable> inputDescendants = [];
    private static int nonPositionalVisits;
    private static int positionalVisits;

    [TestCase(BmsTestSkins.SkinKind.Argon)]
    [TestCase(BmsTestSkins.SkinKind.Legacy)]
    public void InputQueueMembershipDoesNotNeedToTraverseNoteDescendants(BmsTestSkins.SkinKind skin)
    {
        AddStep("load mixed objects", () =>
        {
            this.skin = skin;
            ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!)
                .SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("seek before heads", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
        AddStep("compare input queues and descendant work", () =>
        {
            var notes = soundFixtureObjects();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var nonPositional = typeof(CompositeDrawable).GetMethod("BuildNonPositionalInputQueue", flags)!
                .CreateDelegate<Func<CompositeDrawable, List<Drawable>, bool, bool>>();
            var positional = typeof(CompositeDrawable).GetMethod("BuildPositionalInputQueue", flags)!
                .CreateDelegate<Func<CompositeDrawable, Vector2, List<Drawable>, bool>>();
            var position = Playfield.ScreenSpaceDrawQuad.Centre;
            var expectedNonPositional = new List<Drawable>();
            var expectedPositional = new List<Drawable>();
            var actualNonPositional = new List<Drawable>();
            var actualPositional = new List<Drawable>();
            var callbacks = BindingFlags.Static | BindingFlags.NonPublic;
            using var nonPositionalProbe = new ScopedMethodProbe(typeof(Drawable).GetMethod("BuildNonPositionalInputQueue", flags)!,
                typeof(TestSceneBmsDrawableTraversal).GetMethod(nameof(countNonPositionalVisit), callbacks)!);
            using var positionalProbe = new ScopedMethodProbe(typeof(Drawable).GetMethod("BuildPositionalInputQueue", flags)!,
                typeof(TestSceneBmsDrawableTraversal).GetMethod(nameof(countPositionalVisit), callbacks)!);
            inputDescendants.UnionWith(notes.SelectMany(note => note.ChildrenOfType<Drawable>()));
            var originalFlags = notes.Select(note => note.HandleUserInput).ToArray();
            try
            {
                nonPositionalVisits = positionalVisits = 0;
                buildQueues(expectedNonPositional, expectedPositional);
                Assert.That(nonPositionalVisits, Is.Zero, "The column already prunes non-interactive notes.");
                Assert.That(positionalVisits, Is.Zero);
                Assert.That(expectedNonPositional, Does.Contain(Playfield));
                Assert.That(expectedNonPositional.OfType<IKeyBindingHandler<BmsAction>>(), Is.Not.Empty);
                if (skin == BmsTestSkins.SkinKind.Legacy)
                {
                    Assert.That(expectedNonPositional.OfType<LegacyBmsKeyArea>(), Is.Not.Empty);
                    Assert.That(expectedNonPositional.OfType<LegacyBmsColumnLight>(), Is.Not.Empty);
                }
                foreach (var note in notes) note.HandleUserInput = false;
                for (var frame = 0; frame < 32; frame++)
                {
                    buildQueues(actualNonPositional, actualPositional);
                    Assert.That(actualNonPositional, Is.EqualTo(expectedNonPositional));
                    Assert.That(actualPositional, Is.EqualTo(expectedPositional));
                }
                Assert.That(nonPositionalVisits, Is.Zero);
                Assert.That(positionalVisits, Is.Zero);
                TestContext.WriteLine($"NOTE_INPUT_TRAVERSAL skin={skin} frames=32 originalNonPositional=0 originalPositional=0 changedNonPositional=0 changedPositional=0");
            }
            finally
            {
                for (var i = 0; i < notes.Length; i++) notes[i].HandleUserInput = originalFlags[i];
                inputDescendants.Clear();
            }

            void buildQueues(List<Drawable> keyboard, List<Drawable> mouse)
            {
                keyboard.Clear();
                mouse.Clear();
                nonPositional(Playfield, keyboard, false);
                positional(Playfield, position, mouse);
            }
        });
    }

    private static void countNonPositionalVisit(Drawable __instance)
    {
        if (inputDescendants.Contains(__instance)) nonPositionalVisits++;
    }

    private static void countPositionalVisit(Drawable __instance)
    {
        if (inputDescendants.Contains(__instance)) positionalVisits++;
    }
}
