#nullable enable
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementIndexes
{
    [TestCase(false)]
    [TestCase(true)]
    public void VisualUpdatesPreserveBindingsAndJudgementState(bool mine)
    {
        load(mine);
        seek(3000);
        AddStep("update presentation at one timestamp", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var context = container.Judgements;
            var drawables = container.AliveEntries.Values.OfType<DrawableBmsHitObject>().ToArray();
            var generations = drawables.Select(d => d.BindingGeneration).ToArray();
            var states = context.NoteRegistrations.Select(r => context.GetNoteResult(r.Object)).ToArray();
            for (var i = 0; i < 32; i++) container.UpdateSubTree();
            Assert.That(drawables.Select(d => d.BindingGeneration), Is.EqualTo(generations));
            Assert.That(context.NoteRegistrations.Select(r => context.GetNoteResult(r.Object)), Is.EqualTo(states));
        });
    }
}
