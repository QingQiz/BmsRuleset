using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Audio;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Media.Audio.Playback;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice;

[TestFixture]
public class BmsPracticeBeatmapTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void SelectableEndIncludesLastTapOrHoldInsideItsActualClosingMeasure(bool hold)
    {
        BmsHitObject last = hold
            ? new BmsLongNote { StartTime = 500, Duration = 2000, Column = 1 }
            : new BmsNote { StartTime = 2500, Column = 1 };
        var full = createBeatmap(last);
        full.TimingMap = new BmsTimingMap(192,
            [new(0, 0, 192, 1), new(1, 192, 144, 0.75)], [new(0, 120, 0)], []);

        var settings = BmsPracticeSettings.CreateDefault(full);
        Assert.That(settings.EndTime, Is.EqualTo(3500));
        Assert.That(settings.IsValidFor(full), Is.True);
        Assert.That(BmsPracticeGrid.Snap(full.TimingMap, settings.EndTime, 0), Is.EqualTo(settings.EndTime));
        Assert.That(BmsPracticeBeatmap.Create(full, settings).HitObjects, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ObjectAtMeasureBoundaryUsesFollowingClosingBar(bool hold)
    {
        BmsHitObject last = hold
            ? new BmsLongNote { StartTime = 500, Duration = 1500, Column = 1 }
            : new BmsNote { StartTime = 2000, Column = 1 };
        var full = createBeatmap(last);
        full.TimingMap = new BmsTimingMap(192,
            [new(0, 0, 192, 1), new(1, 192, 96, 0.5)], [new(0, 120, 0)], []);

        var settings = BmsPracticeSettings.CreateDefault(full);
        Assert.That(settings.EndTime, Is.EqualTo(3000));
        Assert.That(settings.Contains(last.StartTime), Is.True);
        Assert.That(BmsPracticeGrid.Snap(full.TimingMap, settings.EndTime, 0), Is.EqualTo(settings.EndTime));
    }

    [Test]
    public void ClosingMeasureBoundsUseBpmStopAndLeadInTiming()
    {
        var full = createBeatmap(new BmsNote { StartTime = 1900, Column = 1 });
        full.TimingMap = new BmsTimingMap(192, [new(0, 0, 192, 1)],
            [new(0, 120, 0), new(96, 240, 1500)], [new(48, 500, 48, 120, 0)]).ShiftedBy(300);

        Assert.That(BmsPracticeBeatmap.GetTimeBounds(full).EndTime, Is.EqualTo(2300));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BoundsWithoutTimingKeepExclusiveOneMillisecondFallback(bool hold)
    {
        var full = createBeatmap(hold
            ? new BmsLongNote { StartTime = 500, Duration = 2000, Column = 1 }
            : new BmsNote { StartTime = 2500, Column = 1 });

        Assert.That(BmsPracticeBeatmap.GetTimeBounds(full).EndTime, Is.EqualTo(2501));
    }

    [Test]
    public void BoundsDoNotTruncateTailWhenTimingMeasuresEndBeforeIt()
    {
        var full = createBeatmap(new BmsLongNote { StartTime = 500, Duration = 2000, Column = 1 });
        full.TimingMap = new BmsTimingMap(192, [new(0, 0, 192, 1)], [new(0, 120, 0)], []);

        Assert.That(BmsPracticeBeatmap.GetTimeBounds(full).EndTime, Is.EqualTo(2501));
    }

    [Test]
    public void SelectionIncludesStartExcludesEndAndRetainsCompleteHolds()
    {
        var before = new BmsLongNote { StartTime = 500, Duration = 2000, Column = 1, SampleKey = 1, TailSampleKey = 2 };
        var first = new BmsNote { StartTime = 1000, Column = 2, SampleKey = 3 };
        var selectedHold = new BmsLongNote { StartTime = 1500, Duration = 2000, Column = 3, SampleKey = 4, TailSampleKey = 5 };
        var excluded = new BmsNote { StartTime = 2000, Column = 4, SampleKey = 6 };
        var excludedMine = new BmsLandmine { StartTime = 700, Column = 1, SampleKey = 7 };
        var full = createBeatmap(before, first, selectedHold, excluded, excludedMine);
        var settings = new BmsPracticeSettings { StartTime = 1000, EndTime = 2000 };

        var practice = BmsPracticeBeatmap.Create(full, settings);

        Assert.That(practice.HitObjects.Select(note => note.StartTime), Is.EqualTo(new[] { 1000d, 1500d }));
        Assert.That(((BmsLongNote)practice.HitObjects[1]).EndTime, Is.EqualTo(3500));
        Assert.That(practice.HitObjects.All(note => ReferenceEquals(note.Beatmap, practice)), Is.True);
        Assert.That(practice.HitObjects[0], Is.Not.SameAs(first));
        Assert.That(practice.PracticeEndTime, Is.EqualTo(settings.EndTime));
        Assert.That(full.PracticeEndTime, Is.Null);
        Assert.That(first.Beatmap, Is.SameAs(full));
        Assert.That(full.HitObjects.Count, Is.EqualTo(5));
        Assert.That(practice.BackgroundSampleEvents.Select(sample => sample.SampleKey), Is.EquivalentTo(new ushort[] { 1, 2, 6 }));
        Assert.That(practice.BackgroundSampleEvents.Single(sample => sample.SampleKey == 2).Time, Is.EqualTo(2500));
        Assert.That(practice.BackgroundSampleEvents.Any(sample => sample.SampleKey is 3 or 4 or 5 or 7), Is.False);
    }

    [Test]
    public void KeySoundStateBeforeSelectionIsAudioOnlyAndSurvivesLongNotes()
    {
        var full = createBeatmap(
            new BmsNote { StartTime = 500, Column = 1, SampleKey = 1 },
            new BmsLongNote { StartTime = 700, Duration = 900, Column = 1, SampleKey = 2 },
            new BmsNote { StartTime = 1500, Column = 1, SampleKey = 4 });
        full.InvisibleNotes =
        [
            new BmsInvisibleNote { Beatmap = full, StartTime = 600, Column = 1, SampleKey = 3 },
            new BmsInvisibleNote { Beatmap = full, StartTime = 1400, Column = 1, SampleKey = 5 },
        ];
        var practice = BmsPracticeBeatmap.Create(full, new BmsPracticeSettings { StartTime = 1000, EndTime = 1501 });
        var cursor = new BmsKeySoundCursor(practice.HitObjects, practice.PracticeInitialKeySounds.Concat(practice.InvisibleNotes).OrderBy(note => note.StartTime).ToArray());

        Assert.That(practice.InvisibleNotes.Select(note => note.SampleKey), Is.EqualTo(new ushort?[] { 5 }));
        Assert.That(practice.PracticeInitialKeySounds.Single().SampleKey, Is.EqualTo(3));
        Assert.That(cursor.Next(1000, _ => false)?.SampleKey, Is.EqualTo(3));
        Assert.That(cursor.Next(1450, _ => false)?.SampleKey, Is.EqualTo(5));
        Assert.That(practice.CountScoringNotes(), Is.EqualTo(1));
    }

    [TestCase(BmsLongNoteMode.LongNote, 2)]
    [TestCase(BmsLongNoteMode.ChargeNote, 3)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 3)]
    public void HealthCountUsesLongNoteMode(BmsLongNoteMode mode, int expected)
    {
        var full = createBeatmap(new BmsNote(), new BmsLongNote(), new BmsLandmine());
        full.LockedLongNoteMode = mode;
        Assert.That(full.CountScoringNotes(), Is.EqualTo(expected));
    }

    [Test]
    public void MixedGaugeChainKeepsFullChartPerJudgementChanges([Values(0d, 230d, 400d)] double total, [Values] BmsGaugeProfileFamily family)
    {
        var full = createBeatmap(Enumerable.Range(0, 100).Select(index => new BmsNote { StartTime = index * 100, Column = 1 }).ToArray());
        full.Total = total;
        var practice = BmsPracticeBeatmap.Create(full, new BmsPracticeSettings { StartTime = 1000, EndTime = 2000 });
        using var normalHealth = new BmsHealthProcessor();
        using var practiceHealth = new BmsHealthProcessor();
        normalHealth.ApplyBeatmap(full);
        practiceHealth.ApplyBeatmap(practice);
        normalHealth.SetGaugeTypes(Enum.GetValues<BmsGaugeType>(), profileFamilyOverride: family);
        practiceHealth.SetGaugeTypes(Enum.GetValues<BmsGaugeType>(), profileFamilyOverride: family);

        foreach (var result in new[] { HitResult.Ok, HitResult.Great, HitResult.Perfect, HitResult.Good, HitResult.Meh })
        {
            normalHealth.RegisterNonConsumingJudgement(result);
            practiceHealth.RegisterNonConsumingJudgement(result);
            var expected = normalHealth.CurrentGaugeStates;
            var actual = practiceHealth.CurrentGaugeStates;
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].GaugeType, Is.EqualTo(expected[i].GaugeType));
                Assert.That(actual[i].Health, Is.EqualTo(expected[i].Health).Within(1e-12), $"{family}/{expected[i].GaugeType}/{result}");
                Assert.That(actual[i].Failed, Is.EqualTo(expected[i].Failed));
            }
        }

        Assert.That(practice.Total, Is.EqualTo(full.Total));
        Assert.That(practice.PracticeFullNoteCount, Is.EqualTo(100));
    }

    [Test]
    public void PreparedModsFixSeedsWithoutChangingSongSelectOrKeepingCourseState()
    {
        var random = new BmsModLaneRandom();
        var gauge = new BmsModHardGauge();
        gauge.InitialHealth.Value = 0.25;
        gauge.GaugeProfileFamily.Value = BmsGaugeProfileFamily.Lr2;
        var autoGauge = new BmsModAutoGauge();
        autoGauge.ClassHealth.Value = 0.3;

        var mods = BmsPracticeSession.PrepareMods([random, gauge, autoGauge]);
        var again = BmsPracticeSession.PrepareMods(mods);

        Assert.That(random.Seed.Value, Is.Null);
        Assert.That(mods.OfType<IHasSeed>().Single().Seed.Value, Is.Not.Null);
        Assert.That(again.OfType<IHasSeed>().Single().Seed.Value, Is.EqualTo(mods.OfType<IHasSeed>().Single().Seed.Value));
        Assert.That(mods.OfType<BmsModHardGauge>().Single().InitialHealth.Value, Is.Null);
        Assert.That(mods.OfType<BmsModHardGauge>().Single().GaugeProfileFamily.Value, Is.Null);
        Assert.That(mods.OfType<BmsModAutoGauge>().Single().ClassHealth.Value, Is.Null);
        Assert.That(gauge.InitialHealth.Value, Is.EqualTo(0.25));
        Assert.That(mods.OfType<BmsModBranchReplay>(), Is.Empty);
        Assert.That(again.OfType<BmsModBranchReplay>(), Is.Empty);
    }

    [Test]
    public void PreviewFreezesBranchAndRandomLayoutAcrossConversions()
    {
        const string chart = "#BPM 120\n#RANDOM 2\n#IF 1\n#00111:0100\n#ENDIF\n#IF 2\n#00112:0002\n#ENDIF\n#ENDRANDOM\n#00213:0300\n#WAV01 a.wav\n#WAV02 b.wav\n#WAV03 c.wav\n";
        var decoded = BmsBeatmapDecoder.DecodeBytes(Encoding.UTF8.GetBytes(chart), new BeatmapInfo());
        var source = new TestWorkingBeatmap(decoded);
        var mods = BmsPracticeSession.PrepareMods([new BmsModLaneRandom()]);
        var first = BmsPracticeSession.GetFullBeatmap(source, mods);
        mods = [.. mods, BmsPracticeSession.CreateBranchReplay(first)!];
        Assert.That(mods.OfType<BmsModBranchReplay>().Single().Decisions.Value, Is.Not.Empty);
        var expected = first.HitObjects.Select(note => (note.StartTime, note.Column, note.SampleKey)).ToArray();
        for (var i = 0; i < 4; i++)
        {
            var again = BmsPracticeSession.GetFullBeatmap(source, BmsPracticeSession.PrepareMods(mods));
            Assert.That(again.BranchDecisions, Is.EqualTo(first.BranchDecisions));
            Assert.That(again.HitObjects.Select(note => (note.StartTime, note.Column, note.SampleKey)), Is.EqualTo(expected));
        }
    }

    [Test]
    public void SessionKeepsBranchesPrivateAndReusesTheSelectedBranchForEveryAttempt([Values] bool hasRandomBranches, [Values] bool withPreview)
    {
        const string chart = "#BPM 120\n#RANDOM 2\n#IF 1\n#00111:0100\n#ENDIF\n#IF 2\n#00112:0002\n#ENDIF\n#ENDRANDOM\n#00213:0300\n";
        var decoded = BmsBeatmapDecoder.DecodeBytes(Encoding.UTF8.GetBytes(hasRandomBranches ? chart : chart.Replace("#RANDOM 2", "#SETRANDOM 1")), new BeatmapInfo());
        var source = new TestWorkingBeatmap(decoded);
        var previewMods = BmsPracticeSession.PrepareMods([new BmsModLaneRandom()]);
        var preview = BmsPracticeSession.GetFullBeatmap(source, previewMods);
        var branchReplay = BmsPracticeSession.CreateBranchReplay(preview);
        Assert.That(branchReplay != null, Is.EqualTo(hasRandomBranches));
        if (withPreview && branchReplay != null)
            previewMods = [.. previewMods, branchReplay];
        var session = new BmsPracticeSession(source, new BmsPracticeSettings { StartTime = 0, EndTime = 6000 }, previewMods);
        var full = session.GetFullBeatmap();
        var expected = full.HitObjects.Select(note => (note.StartTime, note.Column, note.SampleKey)).ToArray();

        Assert.That(session.Mods.Select(mod => mod.GetType()), Is.EqualTo(new[] { typeof(BmsModLaneRandom) }));
        if (withPreview)
            Assert.That(full.BranchDecisions, Is.EqualTo(preview.BranchDecisions));

        for (var i = 0; i < 3; i++)
        {
            var working = new BmsPracticeWorkingBeatmap(session, null!);
            var attempt = working.GetPlayableBeatmap(new BmsRuleset().RulesetInfo, session.Mods, default);
            Assert.That(attempt.HitObjects.Cast<BmsHitObject>().Select(note => (note.StartTime, note.Column, note.SampleKey)), Is.EqualTo(expected));
            Assert.That(source.LastConversionMods.OfType<BmsModBranchReplay>().Count(), Is.EqualTo(hasRandomBranches ? 1 : 0));
        }
    }

    [Test]
    public void NativeConversionDoesNotMutateSourceObjects()
    {
        var full = createBeatmap(new BmsNote { StartTime = 1000, Column = 2, SampleKey = 1 });
        var converted = (BmsBeatmap)new BmsBeatmapConverter(full, new BmsRuleset()).Convert();
        Assert.That(converted.BeatmapInfo, Is.Not.SameAs(full.BeatmapInfo));
        converted.HitObjects[0].Column = 3;
        Assert.That(full.HitObjects[0].Column, Is.EqualTo(2));
        Assert.That(full.HitObjects[0].Beatmap, Is.SameAs(full));
    }

    [Test]
    public void SelectedHoldRetainsScrollAndAudioStateWithoutSharingMutableSamples()
    {
        var hold = new BmsLongNote
        {
            StartTime = 1000, Duration = 500, Column = 2, SourceChannel = 51,
            SampleKey = 1, SampleVolume = 70, TailSampleKey = 2, TailSampleVolume = 60,
            ScrollPositionAtStartTime = 123, ScrollPositionAtEndTime = 456, VisualScrollPositionAtEndTime = 789,
            JudgementRate = 0.8, Samples = [new HitSampleInfo(HitSampleInfo.HIT_NORMAL)],
        };
        var full = createBeatmap(hold);
        var practice = BmsPracticeBeatmap.Create(full, new BmsPracticeSettings { StartTime = 1000, EndTime = 1501 });
        var clone = (BmsLongNote)practice.HitObjects.Single();

        Assert.That(clone.Beatmap, Is.SameAs(practice));
        Assert.That(clone.SourceChannel, Is.EqualTo(hold.SourceChannel));
        Assert.That(clone.SampleKey, Is.EqualTo(hold.SampleKey));
        Assert.That(clone.SampleVolume, Is.EqualTo(hold.SampleVolume));
        Assert.That(clone.TailSampleKey, Is.EqualTo(hold.TailSampleKey));
        Assert.That(clone.TailSampleVolume, Is.EqualTo(hold.TailSampleVolume));
        Assert.That(clone.ScrollPositionAtStartTime, Is.EqualTo(123));
        Assert.That(clone.ScrollPositionAtEndTime, Is.EqualTo(456));
        Assert.That(clone.VisualScrollPositionAtEndTime, Is.EqualTo(789));
        Assert.That(clone.JudgementRate, Is.EqualTo(hold.JudgementRate));
        clone.Samples.Clear();
        clone.Column = 3;
        Assert.That(hold.Samples, Has.Count.EqualTo(1));
        Assert.That(hold.Column, Is.EqualTo(2));
    }

    [Test]
    public void PracticeWorkingBeatmapHasPrivateRawMetadataAndLeavesSourceTrackUnloaded()
    {
        var full = createBeatmap(new BmsNote { StartTime = 1000, Column = 1 });
        var source = new TestWorkingBeatmap(full);
        var session = new BmsPracticeSession(source, BmsPracticeSettings.CreateDefault(full), []);
        var working = new BmsPracticeWorkingBeatmap(session, null!);

        Assert.That(working.Beatmap, Is.Not.SameAs(full));
        Assert.That(working.Beatmap.BeatmapInfo, Is.Not.SameAs(full.BeatmapInfo));
        Assert.That(source.TrackLoaded, Is.False);
        Assert.That(working.TrackLoaded, Is.False);
    }

    [Test]
    public void RepeatedNativePreviewsKeepIdenticalLayoutAndSourceOwnership()
    {
        var full = createBeatmap(Enumerable.Range(0, 7).Select(column => new BmsNote { StartTime = column * 1000, Column = column, SampleKey = (ushort)(column + 1) }).ToArray());
        var source = new TestWorkingBeatmap(full);
        var originalColumns = full.HitObjects.Select(note => note.Column).ToArray();
        var mods = BmsPracticeSession.PrepareMods([new BmsModLaneRandom()]);
        var expected = BmsPracticeSession.GetFullBeatmap(source, mods).HitObjects.Select(note => note.Column).ToArray();

        for (var i = 0; i < 3; i++)
        {
            Assert.That(BmsPracticeSession.GetFullBeatmap(source, mods).HitObjects.Select(note => note.Column), Is.EqualTo(expected));
            Assert.That(full.HitObjects.Select(note => note.Column), Is.EqualTo(originalColumns));
            Assert.That(full.HitObjects.All(note => ReferenceEquals(note.Beatmap, full)), Is.True);
        }
    }

    [Test]
    public void EmptyAndInvalidRangesAreRejectedAndLastTapIsIncludedByDefault()
    {
        var full = createBeatmap(new BmsNote { StartTime = 1000, Column = 1 });
        Assert.That(BmsPracticeSettings.CreateDefault(full).Contains(1000), Is.True);
        Assert.That(new BmsPracticeSettings { StartTime = 0, EndTime = 1000 }.IsValidFor(full), Is.False);
        Assert.That(new BmsPracticeSettings { StartTime = double.NaN, EndTime = 1001 }.IsValidFor(full), Is.False);
        Assert.That(new BmsPracticeSettings { StartTime = 1000, EndTime = 1001, CountIn = 6 }.IsValidFor(full), Is.False);
        Assert.Throws<ArgumentOutOfRangeException>(() => BmsPracticeBeatmap.Create(full, new BmsPracticeSettings { StartTime = 1000, EndTime = 1000 }));
    }

    private static BmsBeatmap createBeatmap(params BmsHitObject[] notes)
    {
        var beatmap = new BmsBeatmap { TotalColumns = 8, LayoutVariant = BmsLayoutVariant.Bme7K, HitObjects = [.. notes] };
        foreach (var note in notes)
            note.Beatmap = beatmap;
        return beatmap;
    }

    private sealed class TestWorkingBeatmap(IBeatmap beatmap) : WorkingBeatmap(beatmap.BeatmapInfo, null!)
    {
        public IReadOnlyList<Mod> LastConversionMods { get; private set; } = [];

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            LastConversionMods = mods;
            return base.GetPlayableBeatmap(ruleset, mods, token);
        }

        protected override IBeatmap GetBeatmap() => beatmap;
        public override Texture GetBackground() => null!;
        protected override Track GetBeatmapTrack() => null!;
        protected override ISkin GetSkin() => null!;
        public override Stream GetStream(string storagePath) => throw new NotSupportedException();
    }
}
