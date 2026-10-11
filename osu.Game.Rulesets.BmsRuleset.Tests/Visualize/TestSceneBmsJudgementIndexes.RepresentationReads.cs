#nullable enable
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Tests.Performance;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementIndexes
{
    private static object? measuredRepresentationBindings;
    private static int representationBindingReads;

    [TestCase(false)]
    [TestCase(true)]
    public void CurrentRepresentationDoesNotQueryTheDrawableMapAgain(bool mine)
    {
        load(mine);
        seek(3000);
        AddStep("read each representation through one binding lookup", () =>
        {
            var column = (BmsColumn)Playfield.Stage.Columns[1];
            var container = (BmsColumnHitObjectContainer)column.HitObjectContainer;
            var drawables = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().ToArray();
            Assert.That(drawables.Length, Is.GreaterThanOrEqualTo(future_note_count));
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var bindings = column.JudgementBridge.GetType().GetField("boundObjects", fields)!.GetValue(column.JudgementBridge)!;
            using var probe = new ScopedMethodProbe(bindings.GetType().GetMethod("TryGetValue")!,
                typeof(TestSceneBmsJudgementIndexes).GetMethod(nameof(countRepresentationBindingRead), BindingFlags.Static | BindingFlags.NonPublic)!);
            representationBindingReads = 0;
            measuredRepresentationBindings = bindings;
            try
            {
                for (var frame = 0; frame < 32; frame++)
                {
                    foreach (var drawable in drawables)
                        Assert.That(column.JudgementBridge.GetRepresentation(drawable.HitObject), Is.SameAs(drawable));
                }
            }
            finally { measuredRepresentationBindings = null; }
            TestContext.WriteLine($"REPRESENTATION_BINDING_READS mine={mine} representations={32 * drawables.Length} redundantReads={representationBindingReads}");
            Assert.That(representationBindingReads, Is.Zero);
        });
    }

    private static void countRepresentationBindingRead(object __instance)
    {
        // Generic reference dictionaries share code with the note-to-binding lookup.
        if (ReferenceEquals(__instance, measuredRepresentationBindings)) representationBindingReads++;
    }
}
