#nullable enable
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementIndexes
{
    private static int layoutBindingReads;
    private static object? layoutBindingDictionary;

    [TestCase(false)]
    [TestCase(true)]
    public void FutureLongNoteLayoutUsesItsBindingHandle(bool mine)
    {
        load(mine);
        seek(3000);
        AddStep("reuse layout binding between callbacks", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var schedule = container.Judgements.Schedule;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var callbacks = BindingFlags.Static | BindingFlags.NonPublic;
            var dictionary = typeof(BmsCoreJudgementSchedule).GetField("bindings", flags)!.GetValue(schedule)!;
            var dictionaryType = dictionary.GetType();
            using var layoutProbe = new ScopedMethodProbe(typeof(BmsCoreJudgementSchedule).GetMethod("RunLayout", flags)!,
                GetType().GetMethod(nameof(beginLayout), callbacks)!, GetType().GetMethod(nameof(endLayout), callbacks)!);
            using var lookupProbe = new ScopedMethodProbe(dictionaryType.GetMethod("TryGetValue")!,
                GetType().GetMethod(nameof(countBindingRead), callbacks)!);
            using var indexProbe = new ScopedMethodProbe(dictionaryType.GetMethod("get_Item")!,
                GetType().GetMethod(nameof(countBindingRead), callbacks)!);
            layoutDepth = layoutCalls = layoutEntries = unrelatedLayoutEntries = layoutBindingReads = 0;
            layoutBindingDictionary = dictionary;
            var states = container.Judgements.NoteRegistrations.Select(r => container.Judgements.GetNoteResult(r.Object)).ToArray();
            for (var i = 0; i < 32; i++)
            {
                schedule.BeginBatch(0, 1000 + i, 1000 + i);
                container.UpdateSubTree();
            }
            layoutBindingDictionary = null;
            Assert.That(layoutDepth, Is.Zero);
            Assert.That(layoutEntries, Is.GreaterThan(32 * 64));
            TestContext.WriteLine($"LAYOUT_BINDING_READS mine={mine} entries={layoutEntries} reads={layoutBindingReads}");
            Assert.That(layoutBindingReads, Is.Zero);
            Assert.That(container.Judgements.NoteRegistrations.Select(r => container.Judgements.GetNoteResult(r.Object)), Is.EqualTo(states));
        });
    }

    private static void countBindingRead(object __instance)
    {
        // Reference-valued dictionary methods share machine code with unrelated dictionaries.
        if (layoutDepth != 0 && ReferenceEquals(__instance, layoutBindingDictionary)) layoutBindingReads++;
    }
}
