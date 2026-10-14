#nullable enable
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementIndexes
{
    private bool orderingFixture;

    [TestCase(false)]
    [TestCase(true)]
    public void PooledMembershipDoesNotChangeLogicalCandidates(bool clearAll)
    {
        load(true, ordering: true);
        seek(3000);
        AddStep("return and rebind representations", () =>
        {
            var container = (BmsColumnHitObjectContainer)Playfield.Stage.Columns[1].HitObjectContainer;
            var candidates = container.Judgements.GetNoteCandidates().ToArray();
            var pending = container.Judgements.FirstPendingNote;
            var entries = container.AliveEntries.Keys.ToArray();
            Assert.That(entries.Length, Is.GreaterThan(128));
            for (var round = 0; round < 3; round++)
            {
                if (clearAll)
                    container.Clear();
                else
                    foreach (var entry in entries) Assert.That(container.Remove(entry), Is.True);
                Assert.That(container.Judgements.GetNoteCandidates(), Is.EqualTo(candidates));
                Assert.That(container.Judgements.FirstPendingNote, Is.SameAs(pending));
                foreach (var entry in entries)
                {
                    container.Add(entry);
                    container.UpdateSubTree();
                    Assert.That(container.Judgements.GetNoteCandidates(), Is.EqualTo(candidates));
                    Assert.That(container.Judgements.FirstPendingNote, Is.SameAs(pending));
                }
            }
        });
    }
}
