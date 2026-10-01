using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Mods;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Input;

[TestFixture]
public class BmsAutoScratchActionsTest
{
    [Test]
    public void TapIsProducedOnceAtItsDeadline()
    {
        var producer = create([new BmsNote { StartTime = 1000, Column = 0 }]);
        Assert.That(drain(producer, 999), Is.Empty);
        Assert.That(drain(producer, 1000), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, true) }));
        Assert.That(drain(producer, 1000), Is.Empty);
        Assert.That(drain(producer, 1010), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, false) }));
    }

    [TestCase(BmsLongNoteMode.LongNote)]
    [TestCase(BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsLongNoteMode.HellChargeNote)]
    public void LongNoteTailUsesOrdinaryReleaseAndReversal(BmsLongNoteMode mode)
    {
        var producer = create([new BmsLongNote { StartTime = 1000, Duration = 500, Column = 0 }], mode);
        Assert.That(drain(producer, 1000), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, true) }));
        Assert.That(drain(producer, 1499), Is.Empty);
        Assert.That(drain(producer, 1500), Is.EqualTo(mode == BmsLongNoteMode.LongNote
            ? new[] { new ActionChange(BmsAction.Scratch, false) }
            : [new ActionChange(BmsAction.Scratch, false), new ActionChange(BmsAction.ScratchReverse, true)]));
        Assert.That(drain(producer, 1510), Is.EqualTo(mode == BmsLongNoteMode.LongNote
            ? Array.Empty<ActionChange>() : [new ActionChange(BmsAction.ScratchReverse, false)]));
    }

    [Test]
    public void CrossedShortNotesRetainEveryTransition()
    {
        var producer = create([
            new BmsLongNote { StartTime = 1000, Duration = .0001, Column = 0 },
            new BmsLongNote { StartTime = 1000.0002, Duration = .0001, Column = 0 },
        ], BmsLongNoteMode.ChargeNote);
        Assert.That(drain(producer, 1001), Is.EqualTo(new[]
        {
            new ActionChange(BmsAction.Scratch, true), new ActionChange(BmsAction.Scratch, false),
            new ActionChange(BmsAction.ScratchReverse, true), new ActionChange(BmsAction.ScratchReverse, false),
            new ActionChange(BmsAction.Scratch, true), new ActionChange(BmsAction.Scratch, false),
            new ActionChange(BmsAction.ScratchReverse, true),
        }));
    }

    [Test]
    public void RewindRestoresHeldStateAndRearmsDeadlines()
    {
        var producer = create([new BmsLongNote { StartTime = 1000, Duration = 500, Column = 0 }], BmsLongNoteMode.ChargeNote);
        drain(producer, 1600);
        Assert.That(drain(producer, 1505), Is.EqualTo(new[] { new ActionChange(BmsAction.ScratchReverse, true) }));
        Assert.That(drain(producer, 1200), Is.EqualTo(new[]
        {
            new ActionChange(BmsAction.ScratchReverse, false), new ActionChange(BmsAction.Scratch, true),
        }));
        Assert.That(drain(producer, 999), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, false) }));
        Assert.That(drain(producer, 1000), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, true) }));
    }

    [Test]
    public void AdjacentHoldsReleaseBeforePressingAgainAtTheSameTime()
    {
        var producer = create([
            new BmsLongNote { StartTime = 1000, Duration = 100, Column = 0 },
            new BmsLongNote { StartTime = 1100, Duration = 100, Column = 0 },
        ]);
        drain(producer, 1000);
        Assert.That(drain(producer, 1100), Is.EqualTo(new[]
        {
            new ActionChange(BmsAction.Scratch, false), new ActionChange(BmsAction.Scratch, true),
        }));
        Assert.That(drain(producer, 1100), Is.Empty);
        Assert.That(drain(producer, 1200), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, false) }));
    }

    [Test]
    public void BothScratchColumnsCanRemainHeldIndependently()
    {
        var producer = create([
            new BmsLongNote { StartTime = 1000, Duration = 200, Column = 0 },
            new BmsLongNote { StartTime = 1000, Duration = 400, Column = 15 },
        ], layout: BmsLayoutVariant.Bme7KDouble);
        Assert.That(drain(producer, 1000), Is.EqualTo(new[]
        {
            new ActionChange(BmsAction.Scratch, true), new ActionChange(BmsAction.P2Scratch, true),
        }));
        Assert.That(drain(producer, 1200), Is.EqualTo(new[] { new ActionChange(BmsAction.Scratch, false) }));
        Assert.That(drain(producer, 1400), Is.EqualTo(new[] { new ActionChange(BmsAction.P2Scratch, false) }));
    }

    [TestCase(BmsLayoutVariant.Bme7KDouble, 15, BmsAction.P2Scratch)]
    [TestCase(BmsLayoutVariant.Bme7K2P, 0, BmsAction.Scratch)]
    [TestCase(BmsLayoutVariant.Bms5KDouble, 11, BmsAction.P2Scratch)]
    public void OnlyScratchColumnsProduceActions(BmsLayoutVariant layout, int secondScratch, BmsAction expected)
    {
        var producer = create([
            new BmsNote { StartTime = 1000, Column = secondScratch },
            new BmsNote { StartTime = 1000, Column = 1 },
            new BmsLandmine { StartTime = 1200, Column = secondScratch },
        ], layout: layout);
        Assert.That(drain(producer, 1000), Is.EqualTo(new[] { new ActionChange(expected, true) }));
        Assert.That(drain(producer, 2000), Is.EqualTo(new[] { new ActionChange(expected, false) }));
    }

    [TestCase(BmsLayoutVariant.Pms9K)]
    [TestCase(BmsLayoutVariant.Pms9K2P)]
    [TestCase(BmsLayoutVariant.Pms9KDouble)]
    public void LayoutWithoutScratchProducesNoActions(BmsLayoutVariant layout)
    {
        var producer = create([new BmsNote { StartTime = 1000, Column = 0 }], layout: layout);
        Assert.That(drain(producer, 2000), Is.Empty);
    }

    [Test]
    public void PollingAndRewindingDoNotAllocate()
    {
        var producer = create([new BmsNote { StartTime = 1000, Column = 0 }]);
        // Warm both paths so the assertion isolates the mod's steady-state allocation cost.
        for (var i = 0; i < 2; i++)
        {
            drain(producer, 1010);
            drain(producer, 0);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        drain(producer, 1010);
        drain(producer, 0);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
    }

    private static ActionRecorder create(List<BmsHitObject> notes, BmsLongNoteMode mode = BmsLongNoteMode.LongNote,
                                             BmsLayoutVariant layout = BmsLayoutVariant.Bme7K)
    {
        var mod = new BmsModAutoScratch();
        var recorder = new ActionRecorder(mod);
        mod.PrepareActions(new BmsBeatmap { LayoutVariant = layout, LockedLongNoteMode = mode, HitObjects = notes },
            action => recorder.Changes.Add(new ActionChange(action, true)),
            action => recorder.Changes.Add(new ActionChange(action, false)));
        return recorder;
    }

    private static List<ActionChange> drain(ActionRecorder recorder, double time)
    {
        recorder.Changes.Clear();
        recorder.Mod.UpdateActions(time);
        return recorder.Changes;
    }
    private readonly record struct ActionChange(BmsAction Action, bool Pressed);

    private sealed record ActionRecorder(BmsModAutoScratch Mod)
    {
        public List<ActionChange> Changes { get; } = [];
    }
}
