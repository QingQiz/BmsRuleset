#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Judgement;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementIndexes
{
    private bool orderingFixture;
    private ScopedMethodProbe? activationProbe;
    private static int activationRefreshes;
    private static Action? registrationCompleted;
    private const BindingFlags private_instance = BindingFlags.NonPublic | BindingFlags.Instance;

    [TearDown]
    public void RemoveActivationProbe()
    {
        activationProbe?.Dispose();
        activationProbe = null;
        registrationCompleted = null;
    }

    [Test]
    public void InitialActivationDoesNotRebuildExistingOrder()
    {
        AddStep("observe order rebuilds", () =>
        {
            activationRefreshes = 0;
            activationProbe = new ScopedMethodProbe(typeof(BmsJudgementBridge).GetMethod("refreshActiveOrder", private_instance),
                GetType().GetMethod(nameof(countActivationRefresh), BindingFlags.NonPublic | BindingFlags.Static));
        });
        load(true, ordering: true);
        seek(3000);
        AddStep("burst activation preserves order without full rebuilds", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            Assert.That(container.AliveEntries.Count, Is.GreaterThan(128));
            Assert.That(activationRefreshes, Is.Zero);
            assertActivationOrder(container);
            RemoveActivationProbe();
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReusedSlotsPublishOrderBeforeRegisterReturns(bool clearAll)
    {
        load(true, ordering: true);
        seek(3000);
        AddStep("reuse several note, mine and invisible slots", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var allEntries = container.AliveEntries.Keys.ToArray();
            var notes = allEntries.Where(e => e.HitObject is BmsNote).Take(4).ToArray();
            var mines = allEntries.Where(e => e.HitObject is BmsLandmine).Take(4).ToArray();
            var invisible = allEntries.Where(e => e.HitObject is BmsInvisibleNote).ToArray();
            Assert.That(notes.Length, Is.EqualTo(4));
            Assert.That(mines.Length, Is.EqualTo(4));
            Assert.That(invisible.Length, Is.EqualTo(4));
            HitObjectLifetimeEntry[] removed = clearAll ? allEntries : [.. notes, .. mines, .. invisible];
            var registrations = 0;
            registrationCompleted = () => { registrations++; assertActivationOrder(container); };
            using var probe = new ScopedMethodProbe(typeof(BmsJudgementBridge).GetMethod("Register", private_instance),
                postfix: GetType().GetMethod(nameof(assertRegistrationOrder), BindingFlags.NonPublic | BindingFlags.Static));
            try
            {
                for (var round = 0; round < 3; round++)
                {
                    if (clearAll)
                        container.Clear();
                    else
                        foreach (var entry in removed) Assert.That(container.Remove(entry), Is.True);
                    assertActivationOrder(container);
                    // Separate updates expose every partially filled free list, including slots
                    // occupied by invisible objects that never enter a judgement registry.
                    foreach (var entry in removed)
                    {
                        container.Add(entry);
                        container.UpdateSubTree();
                        assertActivationOrder(container);
                    }
                }
                Assert.That(registrations, Is.EqualTo(removed.Length * 3));
            }
            finally { registrationCompleted = null; }
        });
    }

    private static void countActivationRefresh() => activationRefreshes++;
    private static void assertRegistrationOrder() => registrationCompleted?.Invoke();

    private static void assertActivationOrder(BmsColumnHitObjectContainer container)
    {
        var context = container.Judgements;
        var notes = container.AliveEntries.Values.Select(d => d.HitObject).OfType<BmsHitObject>()
            .Where(context.IsNoteActive).ToArray();
        Assert.That(context.Notes.ActiveSlots, Is.EqualTo(notes.Select(context.GetNoteSlot)));
        Assert.That(notes.Select(n => context.Notes.RankOf(context.GetNoteSlot(n))), Is.EqualTo(Enumerable.Range(0, notes.Length)));
        var registry = typeof(BmsColumnJudgementRuntime).GetField("landmines", private_instance)!.GetValue(context)!;
        var activeMines = (List<BmsLandmine>)typeof(BmsLandmineRegistry).GetField("activeLandmines", private_instance)!.GetValue(registry)!;
        Assert.That(activeMines, Is.EqualTo(container.AliveEntries.Values.Select(d => d.HitObject).OfType<BmsLandmine>().Where(context.IsLandmineActive)));
    }
}
