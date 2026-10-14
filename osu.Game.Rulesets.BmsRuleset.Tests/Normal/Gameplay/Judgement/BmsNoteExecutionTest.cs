using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Rules;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;

[TestFixture]
public class BmsNoteExecutionTest
{
    [Test]
    public void RegistrationsShareOnlySupportedOperationsAndValueWindows()
    {
        var notes = makeNotes(1024);
        var first = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, new BmsRecordingJudgementOutput()).GetColumn(1);
        var second = new BmsJudgementEngine(notes, BmsLayoutVariant.Bme7K, 1, new BmsRecordingJudgementOutput()).GetColumn(1);
        Assert.That(typeof(BmsNoteRegistration).IsValueType, Is.True);
        Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<BmsNoteJudgementWindows>(), Is.False);
        Assert.That(first.NoteRegistrations, Has.Length.EqualTo(notes.Length));
        for (var i = 0; i < notes.Length; i++)
        {
            var registration = first.NoteRegistrations[i];
            Assert.That(registration.Object, Is.SameAs(notes[i]));
            Assert.That(registration.Slot, Is.EqualTo(i));
            Assert.That(registration.Press, Is.SameAs(first.NoteRegistrations[0].Press));
            Assert.That(registration.Passive, Is.SameAs(first.NoteRegistrations[0].Passive));
            Assert.That(registration.Press.Target, Is.SameAs(registration.Passive.Target));
        }
        Assert.That(first.NoteRegistrations[0].Press, Is.Not.SameAs(second.NoteRegistrations[0].Press), "each column borrows its own typed storage");
    }

    [Test]
    public void UnsupportedScoringTypesCannotEnterTheBoundedPath()
    {
        var note = makeNotes(1)[0];
        note.Beatmap.LockedLongNoteMode = (BmsLongNoteMode)999;
        var longNote = new BmsLongNote { Beatmap = note.Beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        Assert.Throws<InvalidOperationException>(() => new BmsJudgementEngine([longNote], BmsLayoutVariant.Bme7K, 1, new BmsRecordingJudgementOutput()));
    }

    private static BmsHitObject[] makeNotes(int count)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8, Rank = 2 };
        return Enumerable.Range(0, count).Select(i => (BmsHitObject)new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 + i * 1000 }).ToArray();
    }
}
