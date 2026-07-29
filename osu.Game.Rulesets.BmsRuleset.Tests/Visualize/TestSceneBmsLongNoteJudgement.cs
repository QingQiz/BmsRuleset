#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring.Gauge;
using osu.Game.Rulesets.BmsRuleset.Scoring.Judgements;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsLongNoteJudgement : BmsPlayerTestScene
{
    private const double first_case_time = 3000;
    private const double case_spacing = 2300;
    private const double long_note_duration = 900;
    private const double text_lead_time = 700;
    private const double cleanup_release_delay = 650;
    private const double hcn_tick_interval = 200;
    private const double post_tail_stability_delay = 460;

    private static readonly double? normal_press = 0;

    private static readonly double? fast_press = -80;

    // Keep "very" offsets beyond BAD so these cases exercise the miss-side paths, not BAD judgement.
    private static readonly double? very_fast_press = -260;
    private static readonly double? slow_press = 80;
    private static readonly double? very_slow_press = 340;
    private static readonly double? bad_fast_press = -200;
    private static readonly double? bad_slow_press = 240;
    private const double bad_fast_edge = -220;
    private const double before_bad_fast_edge = -221;
    private const double bad_slow_edge = 280;
    private const double after_bad_slow_edge = 281;

    private static readonly double? normal_release = 0;
    private static readonly double? fast_release = -180;
    private static readonly double? very_fast_release = -280;
    private static readonly double? slow_release = 180;
    private static readonly double? very_slow_release = 320;

    private static readonly int[] columns = [1, 2, 3, 4, 5, 6, 7];

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(CreateLongNoteReplayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = createLongNoteBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    private void runMode(string modeName, Mod modeMod, BmsLongNoteMode mode)
    {
        AddStep($"load player in {modeName} mode", () => LoadPlayer([modeMod]));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield is BmsPlayfield);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);

        AddAssert("all visual cases labelled", () =>
        {
            var beatmap = (BmsBeatmap)Player.GameplayState.Beatmap;
            var longNotes = beatmap.HitObjects.Where(h => h is BmsLongNote).OrderBy(h => h.StartTime).ToArray();
            var textEvents = beatmap.TextEvents.TextEvents;

            if (longNotes.Length != cases.Count || textEvents.Length != cases.Count)
                return false;

            for (var i = 0; i < cases.Count; i++)
            {
                var expectedStartTime = first_case_time + i * case_spacing;
                var expectedTick = caseTick(i);

                if (longNotes[i].StartTime != expectedStartTime
                    || textEvents[i].Time != expectedStartTime - text_lead_time
                    || textEvents[i].Tick != expectedTick - 48
                    || textEvents[i].Text != $"{i + 1:00} {cases[i].Text}")
                {
                    return false;
                }
            }

            return true;
        });

        AddAssert("very offsets are outside BAD windows", veryOffsetsAreOutsideBadWindows);
        AddAssert("release offsets match tail windows", releaseOffsetsMatchTailWindows);
        AddAssert("bad edge offsets match beatoraja windows", badEdgeOffsetsMatchWindows);

        for (var i = 0; i < cases.Count; i++)
        {
            var index = i;
            CaseSnapshot before = default;
            CaseSnapshot afterFirstRelease = default;
            CaseSnapshot afterReleasedBody = default;
            CaseSnapshot afterTail = default;

            AddStep($"seek {index + 1:00}: {cases[index].Text}", () =>
            {
                var startTime = first_case_time + index * case_spacing;
                Player.GameplayClockContainer.Seek(startTime - text_lead_time - 50);
            });
            AddStep($"capture baseline {index + 1:00}", () =>
            {
                Player.HealthProcessor.Health.Value = 0.5;
                before = takeSnapshot();
            });

            if (firstFastReleaseCheckOffset(cases[index], mode) is { } visibilityCheckOffset)
            {
                AddUntilStep($"fast release visual state {index + 1:00}", () =>
                {
                    var startTime = first_case_time + index * case_spacing;
                    return Player.GameplayClockContainer.CurrentTime >= startTime + visibilityCheckOffset;
                });
                AddAssert($"fast release visibility {index + 1:00}", () =>
                        isCaseLongNoteAlive(index), $"{modeName} {cases[index].Text}: beatoraja keeps the LN body drawn until the tail passes");
            }

            if (cases[index].FirstRepressOffsetAfterFirstRelease is { } repressOffset)
            {
                AddUntilStep($"first release judged {index + 1:00}", () =>
                {
                    var firstReleaseOffset = cases[index].FirstReleaseOffsetAfter(cases[index].FirstPressOffset!.Value, onlyBeforeTail: false)!.Value;
                    var startTime = first_case_time + index * case_spacing;
                    return Player.GameplayClockContainer.CurrentTime >= startTime + firstReleaseOffset + 50;
                });
                AddStep($"assert first release {index + 1:00}", () =>
                {
                    var expected = expectedFor(cases[index], mode);
                    afterFirstRelease = takeSnapshot();
                    assertJudgementDelta(modeName, cases[index], expected, before, afterFirstRelease);
                    assertScoreComboDelta(modeName, cases[index], expected, before, afterFirstRelease);

                    if (mode != BmsLongNoteMode.HellChargeNote)
                        assertHealthDelta(modeName, cases[index], expected, before, afterFirstRelease);
                });

                if (mode == BmsLongNoteMode.HellChargeNote && cases[index].TestsHellChargeReleaseRecovery)
                {
                    AddUntilStep($"hcn released body damages {index + 1:00}", () =>
                    {
                        var startTime = first_case_time + index * case_spacing;
                        return Player.GameplayClockContainer.CurrentTime >= startTime + repressOffset - 50;
                    });
                    AddStep($"assert hcn released damage {index + 1:00}", () =>
                    {
                        afterReleasedBody = takeSnapshot();
                        assertNoJudgementScoreComboChange(modeName, cases[index], afterFirstRelease, afterReleasedBody);
                        Assert.That(afterReleasedBody.Health, Is.LessThan(afterFirstRelease.Health), $"{modeName} {cases[index].Text}: released HCN body should drain health");
                        Assert.That(isCaseLongNoteAlive(index), Is.True, $"{modeName} {cases[index].Text}: HCN should stay visible while released before tail");
                    });
                }

                AddUntilStep($"repress does not rejudge {index + 1:00}", () =>
                {
                    var startTime = first_case_time + index * case_spacing;
                    return Player.GameplayClockContainer.CurrentTime >= startTime + repressOffset + 50;
                });
                AddStep($"assert repress no-op {index + 1:00}", () =>
                {
                    var snapshotAfterRepress = takeSnapshot();
                    assertNoJudgementScoreComboChange(modeName, cases[index], afterFirstRelease, snapshotAfterRepress);

                    if (mode != BmsLongNoteMode.HellChargeNote)
                        Assert.That(snapshotAfterRepress.Health, Is.EqualTo(afterFirstRelease.Health).Within(0.000001), $"{modeName} {cases[index].Text}: repress should not change health");
                });

                if (mode == BmsLongNoteMode.HellChargeNote && cases[index].TestsHellChargeReleaseRecovery)
                {
                    AddUntilStep($"hcn repress recovers {index + 1:00}", () =>
                    {
                        var startTime = first_case_time + index * case_spacing;
                        return Player.GameplayClockContainer.CurrentTime >= startTime + long_note_duration - 50;
                    });
                    AddStep($"assert hcn repress recovery {index + 1:00}", () =>
                    {
                        var snapshotAfterRecovery = takeSnapshot();
                        assertNoJudgementScoreComboChange(modeName, cases[index], afterReleasedBody, snapshotAfterRecovery);
                        Assert.That(snapshotAfterRecovery.Health, Is.GreaterThan(afterReleasedBody.Health), $"{modeName} {cases[index].Text}: repressed HCN body should recover health");
                        Assert.That(isCaseLongNoteAlive(index), Is.True, $"{modeName} {cases[index].Text}: HCN should continue displaying after repress before tail");
                    });
                }
            }

            if (mode == BmsLongNoteMode.HellChargeNote && cases[index].TestsHellChargePostTailStop)
            {
                AddUntilStep($"hcn post-tail baseline {index + 1:00}", () =>
                {
                    var startTime = first_case_time + index * case_spacing;
                    return Player.GameplayClockContainer.CurrentTime >= startTime + long_note_duration + 60;
                });
                AddStep($"capture hcn post-tail {index + 1:00}", () =>
                {
                    afterTail = takeSnapshot();
                    Assert.That(isCaseLongNoteAlive(index), Is.False, $"{modeName} {cases[index].Text}: beatoraja stops drawing HCN after the tail passes");
                });
                AddUntilStep($"hcn post-tail stable {index + 1:00}", () =>
                {
                    var startTime = first_case_time + index * case_spacing;
                    return Player.GameplayClockContainer.CurrentTime >= startTime + long_note_duration + post_tail_stability_delay;
                });
                AddStep($"assert hcn post-tail no body tick {index + 1:00}", () =>
                {
                    var snapshotAfterPostTailWait = takeSnapshot();
                    assertNoJudgementScoreComboChange(modeName, cases[index], afterTail, snapshotAfterPostTailWait);
                    Assert.That(snapshotAfterPostTailWait.Health, Is.EqualTo(afterTail.Health).Within(0.000001),
                        $"{modeName} {cases[index].Text}: HCN body ticks must stop once the tail has passed");
                });
            }

            AddUntilStep($"finish {index + 1:00}", () =>
            {
                var startTime = first_case_time + index * case_spacing;
                return Player.GameplayClockContainer.CurrentTime >= startTime + long_note_duration + cleanup_release_delay + 100;
            });
            AddStep($"assert {index + 1:00}", () =>
            {
                var expected = expectedFor(cases[index], mode);
                assertCaseDelta(modeName, cases[index], expected, before, takeSnapshot());
                Assert.That(isCaseLongNoteAlive(index), Is.False, $"{modeName} {cases[index].Text}: long note should be gone after the tail-side lifetime");
            });
        }
    }

    public static IList<ReplayFrame> CreateLongNoteReplayFrames(BmsBeatmap beatmap)
    {
        var actionPoints = new List<ActionPoint>();
        var hitObjects = beatmap.HitObjects
            .Where(h => h is BmsLongNote)
            .OrderBy(h => h.StartTime)
            .ToArray();

        for (var i = 0; i < hitObjects.Length && i < cases.Count; i++)
        {
            var hitObject = hitObjects[i];
            var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, hitObject.Column);

            if (action == null)
                continue;

            foreach (var input in cases[i].Inputs)
                actionPoints.Add(new ActionPoint(hitObject.StartTime + input.OffsetFromHead, action.Value, input.Press));

            if (cases[i].NeedsCleanupRelease)
                actionPoints.Add(new ActionPoint(hitObject.GetEndTime() + cleanup_release_delay, action.Value, false));
        }

        return materialise(actionPoints);
    }

    private static BmsBeatmap createLongNoteBeatmap()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 160,
        };

        var textEvents = new List<BmsTextEvent>();

        for (var i = 0; i < cases.Count; i++)
        {
            var startTime = first_case_time + i * case_spacing;
            var tick = caseTick(i);

            beatmap.HitObjects.Add(new BmsLongNote
            {
                StartTime = startTime,
                Duration = long_note_duration,
                Column = columns[i % columns.Length],
            });

            textEvents.Add(new BmsTextEvent(startTime - text_lead_time, tick - 48, $"{i + 1:00} {cases[i].Text}"));
        }

        beatmap.TextEvents = new BmsTextEvents(string.Empty, textEvents.ToArray());
        return beatmap;
    }

    private static IList<ReplayFrame> materialise(List<ActionPoint> actionPoints)
    {
        var activeActions = new List<BmsAction>();
        var frames = new List<ReplayFrame> { new BmsReplayFrame(0) };

        foreach (var group in actionPoints.GroupBy(p => p.Time).OrderBy(g => g.Key))
        {
            foreach (var point in group.OrderBy(p => p.Press))
            {
                if (point.Press)
                {
                    if (!activeActions.Contains(point.Action))
                        activeActions.Add(point.Action);
                }
                else
                    activeActions.Remove(point.Action);
            }

            frames.Add(new BmsReplayFrame(group.Key, activeActions.ToArray()));
        }

        return frames;
    }

    private static IReadOnlyList<LongNoteVisualCase> createCases()
    {
        var longNoteVisualCases = new List<LongNoteVisualCase>();

        (string Name, double? Offset)[] presses =
        [
            ("normal press", normal_press),
            ("fast press", fast_press),
            ("very fast press", very_fast_press),
            ("slow press", slow_press),
            ("very slow press", very_slow_press),
        ];

        (string Name, double? Offset)[] releases =
        [
            ("normal release", normal_release),
            ("fast release", fast_release),
            ("very fast release", very_fast_release),
            ("slow release", slow_release),
            ("very slow release", very_slow_release),
            ("no release", null),
        ];

        foreach (var press in presses)
        {
            foreach (var release in releases)
            {
                var inputs = new List<RelativeInput>
                {
                    new(press.Offset!.Value, true),
                };

                if (release.Offset != null)
                    inputs.Add(new RelativeInput(long_note_duration + release.Offset.Value, false));

                longNoteVisualCases.Add(new LongNoteVisualCase($"{press.Name} + {release.Name}", inputs.ToArray()));
            }
        }

        longNoteVisualCases.Add(new LongNoteVisualCase("no press + no release", []));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + fast release, gap, repress for normal tail", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration + fast_release!.Value, false),
            new RelativeInput(long_note_duration - 60, true),
            new RelativeInput(long_note_duration, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("fast press + very fast release, gap, slow repress", [
            new RelativeInput(fast_press!.Value, true),
            new RelativeInput(long_note_duration + very_fast_release!.Value, false),
            new RelativeInput(long_note_duration + slow_release!.Value, true),
            new RelativeInput(long_note_duration + very_slow_release!.Value, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + very fast release, long gap, repress through tail", [
            new RelativeInput(0, true),
            new RelativeInput(150, false),
            new RelativeInput(680, true),
            new RelativeInput(long_note_duration, false),
        ], TestsHellChargeReleaseRecovery: true));
        longNoteVisualCases.Add(new LongNoteVisualCase("bad fast press + no release, LN auto tail uses head offset", [
            new RelativeInput(bad_fast_press!.Value, true),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("bad slow press + no release, LN auto tail uses head offset", [
            new RelativeInput(bad_slow_press!.Value, true),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + normal release, HCN body stops after tail", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration, false),
        ], TestsHellChargePostTailStop: true));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + fast bad release, HCN damage stops after tail", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration + fast_release.Value, false),
        ], TestsHellChargePostTailStop: true));
        longNoteVisualCases.Add(new LongNoteVisualCase("head fast BAD edge + normal release", [
            new RelativeInput(bad_fast_edge, true),
            new RelativeInput(long_note_duration, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("head before fast BAD edge + no release", [
            new RelativeInput(before_bad_fast_edge, true),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("head slow BAD edge + normal release", [
            new RelativeInput(bad_slow_edge, true),
            new RelativeInput(long_note_duration, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("head after slow BAD edge + no release", [
            new RelativeInput(after_bad_slow_edge, true),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + tail fast BAD edge", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration + bad_fast_edge, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + tail before fast BAD edge", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration + before_bad_fast_edge, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + tail slow BAD edge", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration + bad_slow_edge, false),
        ]));
        longNoteVisualCases.Add(new LongNoteVisualCase("normal press + tail after slow BAD edge", [
            new RelativeInput(0, true),
            new RelativeInput(long_note_duration + after_bad_slow_edge, false),
        ]));

        return longNoteVisualCases;
    }

    private static bool veryOffsetsAreOutsideBadWindows()
    {
        var headTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: false);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);

        return headTable.ResultForOffset(very_fast_press!.Value) == HitResult.None
               && headTable.ResultForOffset(very_slow_press!.Value) == HitResult.None
               && tailTable.ResultForOffset(very_fast_release!.Value) == HitResult.None
               && tailTable.ResultForOffset(very_slow_release!.Value) == HitResult.None;
    }

    private static bool releaseOffsetsMatchTailWindows()
    {
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);

        return tailTable.ResultForOffset(normal_release!.Value) == HitResult.Perfect
               && tailTable.ResultForOffset(fast_release!.Value) == HitResult.Ok
               && tailTable.ResultForOffset(slow_release!.Value) == HitResult.Ok
               && tailTable.ResultForOffset(very_fast_release!.Value) == HitResult.None
               && tailTable.ResultForOffset(very_slow_release!.Value) == HitResult.None;
    }

    private static bool badEdgeOffsetsMatchWindows()
    {
        var headTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: false);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);

        return headTable.ResultForOffset(bad_fast_edge) == HitResult.Ok
               && headTable.ResultForOffset(before_bad_fast_edge) == HitResult.None
               && headTable.ResultForOffset(bad_slow_edge) == HitResult.Ok
               && headTable.ResultForOffset(after_bad_slow_edge) == HitResult.None
               && tailTable.ResultForOffset(bad_fast_edge) == HitResult.Ok
               && tailTable.ResultForOffset(before_bad_fast_edge) == HitResult.None
               && tailTable.ResultForOffset(bad_slow_edge) == HitResult.Ok
               && tailTable.ResultForOffset(after_bad_slow_edge) == HitResult.None;
    }

    private bool isCaseLongNoteAlive(int index)
        => Playfield.GetAliveObjectAtTime(first_case_time + index * case_spacing) is { HitObject: BmsLongNote } drawable
           && drawable.Alpha > 0;

    private DrawableBmsHitObject? getCaseLongNote(int index)
        => Playfield.GetAliveObjectAtTime(first_case_time + index * case_spacing) is { HitObject: BmsLongNote } drawable
            ? drawable
            : null;

    private static float tailBottomOf(DrawableBmsHitObject longNote)
    {
        var tailContainer = (Container)privateField(longNote, "longNoteTailContainer");
        return BmsPlayfieldAssertions.BottomOf(tailContainer);
    }

    private static float headBottomOf(DrawableBmsHitObject longNote)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var headContainer = (Container)longNote.GetType().GetField("NoteContainer", flags)!.GetValue(longNote)!;
        return BmsPlayfieldAssertions.BottomOf(headContainer);
    }

    private static Drawable longNoteBodyOf(DrawableBmsHitObject longNote)
        => (Drawable)privateField(longNote, "longNoteBody");

    private static object privateField(DrawableBmsHitObject longNote, string fieldName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return longNote.GetType().GetField(fieldName, flags)!.GetValue(longNote)!;
    }

    private static long caseTick(int index) => 192L * (index + 1);

    private static double? firstFastReleaseCheckOffset(LongNoteVisualCase testCase, BmsLongNoteMode mode)
    {
        var firstPress = testCase.FirstPressOffset;

        if (firstPress == null)
            return null;

        var headTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: false);

        if (headTable.ResultForOffset(firstPress.Value) == HitResult.None)
            return null;

        var firstFastRelease = testCase.FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: true);

        if (firstFastRelease == null)
            return null;

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);

        if (tailResultForRelease(mode, firstPress.Value, firstFastRelease.Value, tailTable) != HitResult.Meh)
            return null;

        var checkOffset = Math.Min(firstFastRelease.Value + 120, long_note_duration - 40);
        return checkOffset > firstFastRelease.Value ? checkOffset : null;
    }

    private CaseSnapshot takeSnapshot()
        => new(
            new Dictionary<HitResult, int>(Player.ScoreProcessor.Statistics),
            Player.ScoreProcessor.MaximumStatistics,
            Player.Results.Select(result => result.Type).ToArray(),
            Player.ScoreProcessor.TotalScoreWithoutMods.Value,
            Player.ScoreProcessor.Combo.Value,
            Player.HealthProcessor.Health.Value);

    private static void assertCaseDelta(string modeName, LongNoteVisualCase testCase, CaseExpectation expected, CaseSnapshot before, CaseSnapshot after)
    {
        assertJudgementDelta(modeName, testCase, expected, before, after);
        assertScoreComboDelta(modeName, testCase, expected, before, after);
        assertHealthDelta(modeName, testCase, expected, before, after);
    }

    private static void assertJudgementDelta(string modeName, LongNoteVisualCase testCase, CaseExpectation expected, CaseSnapshot before, CaseSnapshot after)
    {
        foreach (var result in tracked_results)
        {
            var actual = after.ResultCounts.GetValueOrDefault(result) - before.ResultCounts.GetValueOrDefault(result);
            var expectedCount = expected.ResultCounts.GetValueOrDefault(result);

            Assert.That(actual, Is.EqualTo(expectedCount), $"{modeName} {testCase.Text}: {result} count");
        }

        var actualSequence = after.JudgementSequence.Skip(before.JudgementSequence.Count).ToArray();
        Assert.That(actualSequence, Is.EqualTo(expected.JudgementSequence.Where(result => result != HitResult.Miss).ToArray()),
            $"{modeName} {testCase.Text}: judgement sequence");
    }

    private static void assertScoreComboDelta(string modeName, LongNoteVisualCase testCase, CaseExpectation expected, CaseSnapshot before, CaseSnapshot after)
    {
        Assert.That(after.ScoreWithoutMods, Is.EqualTo(expectedScoreFor(after)), $"{modeName} {testCase.Text}: score");
        Assert.That(after.Combo, Is.EqualTo(expected.ComboAfter(before.Combo)), $"{modeName} {testCase.Text}: combo");
    }

    private static void assertHealthDelta(string modeName, LongNoteVisualCase testCase, CaseExpectation expected, CaseSnapshot before, CaseSnapshot after)
    {
        Assert.That(after.Health, Is.EqualTo(expected.HealthAfter(before.Health)).Within(0.000001), $"{modeName} {testCase.Text}: health");
    }

    private static void assertNoJudgementScoreComboChange(string modeName, LongNoteVisualCase testCase, CaseSnapshot before, CaseSnapshot after)
    {
        foreach (var result in tracked_results)
        {
            Assert.That(after.ResultCounts.GetValueOrDefault(result), Is.EqualTo(before.ResultCounts.GetValueOrDefault(result)),
                $"{modeName} {testCase.Text}: repress should not add {result}");
        }

        Assert.That(after.JudgementSequence.Skip(before.JudgementSequence.Count), Is.Empty, $"{modeName} {testCase.Text}: repress should not add judgement results");
        Assert.That(after.ScoreWithoutMods, Is.EqualTo(before.ScoreWithoutMods), $"{modeName} {testCase.Text}: repress should not change score");
        Assert.That(after.Combo, Is.EqualTo(before.Combo), $"{modeName} {testCase.Text}: repress should not change combo");
    }

    private static long expectedScoreFor(CaseSnapshot snapshot)
    {
        var currentBase = snapshot.ResultCounts.GetValueOrDefault(HitResult.Perfect) * 2
                          + snapshot.ResultCounts.GetValueOrDefault(HitResult.Great);
        var maximumBase = snapshot.MaximumResultCounts.Sum(kvp => kvp.Value * baseScoreFor(kvp.Key));

        return maximumBase == 0 ? 0 : (long)Math.Round(1_000_000d * currentBase / maximumBase);
    }

    private static int baseScoreFor(HitResult result) => result switch
    {
        HitResult.Perfect => 2,
        HitResult.Great => 1,
        _ => 0,
    };

    private static CaseExpectation expectedFor(LongNoteVisualCase testCase, BmsLongNoteMode mode)
    {
        var sequence = expectedJudgementSequence(testCase, mode);
        var expectedHealthEvents = healthEvents(testCase, mode, sequence).ToArray();
        var resultCounts = sequence.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());

        return new CaseExpectation(resultCounts, sequence, expectedHealthEvents);
    }

    // beatoraja carries the head offset until LN completion; manual release can only replace it
    // when the release-side miss is worse, while auto-tail keeps the stored head offset.
    private static IReadOnlyList<HitResult> expectedJudgementSequence(LongNoteVisualCase testCase, BmsLongNoteMode mode)
    {
        var headTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: false);
        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);
        var firstPress = testCase.FirstPressOffset;

        if (firstPress == null)
            return missedLongNoteSequence(mode, emptyPoor: false);

        var headResult = headTable.ResultForOffset(firstPress.Value);

        if (headResult == HitResult.None)
        {
            if (mode == BmsLongNoteMode.HellChargeNote)
            {
                var sequence = new List<HitResult>();

                if (headTable.IsEmptyPoorOffset(firstPress.Value))
                    sequence.Add(HitResult.Miss);

                sequence.Add(HitResult.Meh);

                var missedHeadTailRelease = testCase.FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: false);
                sequence.Add(tailResultForRelease(missedHeadTailRelease, tailTable));

                return sequence;
            }

            return missedLongNoteSequence(mode, headTable.IsEmptyPoorOffset(firstPress.Value));
        }

        if (mode == BmsLongNoteMode.LongNote)
        {
            var releaseBeforeTail = testCase.FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: true);
            var judgeOffset = firstPress.Value;

            if (releaseBeforeTail != null)
            {
                var releaseOffsetFromTail = releaseBeforeTail.Value - long_note_duration;
                judgeOffset = Math.Abs(firstPress.Value) > Math.Abs(releaseOffsetFromTail)
                    ? firstPress.Value
                    : releaseOffsetFromTail;
            }

            var result = tailTable.ResultForOffset(judgeOffset);
            return [result == HitResult.None ? HitResult.Meh : result];
        }

        var tailRelease = testCase.FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: false);
        return [headResult, tailResultForRelease(tailRelease, tailTable)];
    }

    private static HitResult tailResultForRelease(double? tailRelease, BmsJudgementWindowTable tailTable)
    {
        if (tailRelease == null)
            return HitResult.Meh;

        var tailOffset = tailRelease.Value - long_note_duration;
        var tailResult = tailTable.IsPastPassivePoorOffset(tailOffset)
            ? HitResult.Meh
            : tailTable.ResultForOffset(tailOffset);

        return tailResult == HitResult.None ? HitResult.Meh : tailResult;
    }

    private static HitResult tailResultForRelease(
        BmsLongNoteMode mode,
        double firstPress,
        double releaseFromHead,
        BmsJudgementWindowTable tailTable)
    {
        if (mode != BmsLongNoteMode.LongNote)
            return tailResultForRelease(releaseFromHead, tailTable);

        var tailOffset = releaseFromHead - long_note_duration;
        var heldOffset = Math.Abs(firstPress) > Math.Abs(tailOffset) ? firstPress : tailOffset;
        var result = tailTable.ResultForOffset(heldOffset);

        return result == HitResult.None ? HitResult.Meh : result;
    }

    // Miss pattern per beatoraja: emptyPoor (Miss) when offset lands in MS window,
    // then Meh for missed head. CN/HCN get a second Meh for missed tail; LN does not.
    private static IReadOnlyList<HitResult> missedLongNoteSequence(BmsLongNoteMode mode, bool emptyPoor)
    {
        var sequence = new List<HitResult>();

        if (emptyPoor)
            sequence.Add(HitResult.Miss);

        sequence.Add(HitResult.Meh);

        if (mode is BmsLongNoteMode.ChargeNote or BmsLongNoteMode.HellChargeNote)
            sequence.Add(HitResult.Meh);

        return sequence;
    }

    private static IEnumerable<HealthEvent> healthEvents(LongNoteVisualCase testCase, BmsLongNoteMode mode, IReadOnlyList<HitResult> sequence)
    {
        var firstPress = testCase.FirstPressOffset;
        var headTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: false);

        if (mode == BmsLongNoteMode.HellChargeNote
            && (firstPress == null || headTable.ResultForOffset(firstPress.Value) == HitResult.None))
        {
            var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);
            var headPoorOffset = headTable.SlowWindowFor(HitResult.Ok) + 0.001;
            var sequenceIndex = 0;

            if (firstPress != null && headTable.IsEmptyPoorOffset(firstPress.Value))
                yield return new HealthEvent(firstPress.Value, sequence[sequenceIndex++], 1);

            yield return new HealthEvent(headPoorOffset, sequence[sequenceIndex++], 1);

            var tailEventRelease = firstPress == null ? null : testCase.FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: false);
            var tailEventOffset = tailEventRelease ?? long_note_duration + tailTable.SlowWindowFor(HitResult.Ok) + 0.001;
            yield return new HealthEvent(tailEventOffset, sequence[sequenceIndex], 1);

            foreach (var result in hcnBodyTickEvents(testCase, headPoorOffset))
                yield return result;

            yield break;
        }

        if (mode != BmsLongNoteMode.HellChargeNote || sequence.Count != 2 || testCase.FirstPressOffset == null)
        {
            for (var i = 0; i < sequence.Count; i++)
                yield return new HealthEvent(i, sequence[i], 1);

            yield break;
        }

        yield return new HealthEvent(testCase.FirstPressOffset.Value, sequence[0], 1);

        var tailRelease = testCase.FirstReleaseOffsetAfter(testCase.FirstPressOffset.Value, onlyBeforeTail: false);
        yield return new HealthEvent(tailRelease ?? long_note_duration + very_slow_release!.Value, sequence[1], 1);

        foreach (var result in hcnBodyTickEvents(testCase))
            yield return result;
    }

    // beatoraja carries HCN body progress in one signed accumulator. A held section can
    // offset a later released section before a damage tick is emitted.
    private static IEnumerable<HealthEvent> hcnBodyTickEvents(LongNoteVisualCase testCase, double? bodyStartOffset = null)
    {
        var firstPress = testCase.FirstPressOffset;

        if (bodyStartOffset == null && firstPress == null)
            yield break;

        var headTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: false);

        if (bodyStartOffset == null && headTable.ResultForOffset(firstPress!.Value) == HitResult.None)
            yield break;

        var tailTable = BmsJudgementProfileProvider.GetTable(BmsLayoutVariant.Bme7K, columns[0], rank: 2, tail: true);
        var offset = bodyStartOffset ?? Math.Max(0, firstPress!.Value);
        var accumulator = 0d;
        var boundaries = testCase.Inputs
            .Select(input => input.OffsetFromHead)
            .Where(inputOffset => inputOffset > offset && inputOffset < long_note_duration)
            .Append(long_note_duration)
            .Order()
            .ToArray();

        foreach (var boundary in boundaries)
        {
            var segmentStart = offset;
            var recovering = hcnBodyRecoveringAt(testCase, segmentStart + 0.001, tailTable);
            var sign = recovering ? 1 : -1;

            while (segmentStart < boundary)
            {
                var remaining = boundary - segmentStart;
                var projected = accumulator + sign * remaining;

                if (recovering && projected > hcn_tick_interval)
                {
                    var distance = hcn_tick_interval - accumulator + 0.001;
                    segmentStart += distance;
                    accumulator += distance - hcn_tick_interval;
                    yield return new HealthEvent(segmentStart, HitResult.Great, 0.5);

                    continue;
                }

                if (!recovering && projected < -hcn_tick_interval)
                {
                    var distance = accumulator + hcn_tick_interval + 0.001;
                    segmentStart += distance;
                    accumulator -= distance - hcn_tick_interval;
                    yield return new HealthEvent(segmentStart, HitResult.Ok, 0.5);

                    continue;
                }

                accumulator = projected;
                segmentStart = boundary;
            }

            offset = boundary;
        }
    }

    private static bool hcnBodyRecoveringAt(LongNoteVisualCase testCase, double offset, BmsJudgementWindowTable tailTable)
    {
        if (testCase.IsPressedAt(offset))
            return true;

        var firstPress = testCase.FirstPressOffset;

        if (firstPress == null)
            return false;

        var firstReleaseBeforeTail = testCase.FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: true);

        if (firstReleaseBeforeTail == null || firstReleaseBeforeTail.Value > offset)
            return false;

        return tailTable.ResultForOffset(firstReleaseBeforeTail.Value - long_note_duration) is HitResult.Perfect or HitResult.Great or HitResult.Good;
    }

    private static IReadOnlyList<LongNoteVisualCase> cases { get; } = createCases();

    private static readonly HitResult[] tracked_results =
    [
        HitResult.Perfect,
        HitResult.Great,
        HitResult.Good,
        HitResult.Ok,
        HitResult.Meh,
        HitResult.Miss,
    ];

    private readonly record struct RelativeInput(double OffsetFromHead, bool Press);

    private readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

    private readonly record struct CaseSnapshot(
        IReadOnlyDictionary<HitResult, int> ResultCounts,
        IReadOnlyDictionary<HitResult, int> MaximumResultCounts,
        IReadOnlyList<HitResult> JudgementSequence,
        long ScoreWithoutMods,
        int Combo,
        double Health);

    private readonly record struct HealthEvent(double Offset, HitResult Result, double Scale);

    private sealed record CaseExpectation(
        IReadOnlyDictionary<HitResult, int> ResultCounts,
        IReadOnlyList<HitResult> JudgementSequence,
        IReadOnlyList<HealthEvent> HealthEvents)
    {
        public double HealthAfter(double startingHealth)
        {
            var calculator = new BmsGaugeCalculator(BmsGaugeProfileFactory.Create(BmsGaugeType.Normal), 160, cases.Count);
            var health = startingHealth;

            foreach (var result in HealthEvents.OrderBy(e => e.Offset))
            {
                var delta = calculator.GetDeltaFor(result.Result, health) * result.Scale;
                health = calculator.ApplyDelta(health, delta);
            }

            return health;
        }

        public int ComboAfter(int beforeCombo)
        {
            var combo = beforeCombo;

            foreach (var result in JudgementSequence)
            {
                switch (result)
                {
                    case HitResult.Miss:
                        break;

                    case HitResult.Ok:
                    case HitResult.Meh:
                        combo = 0;
                        break;

                    default:
                        combo++;
                        break;
                }
            }

            return combo;
        }
    }

    private sealed record LongNoteVisualCase(
        string Text,
        IReadOnlyList<RelativeInput> Inputs,
        bool TestsHellChargeReleaseRecovery = false,
        bool TestsHellChargePostTailStop = false)
    {
        public bool NeedsCleanupRelease => Inputs.LastOrDefault().Press;

        public double? FirstPressOffset => Inputs.FirstOrDefault(relativeInput => relativeInput.Press) is { Press: true } input
            ? input.OffsetFromHead
            : null;

        public double? FirstRepressOffsetAfterFirstRelease
        {
            get
            {
                var firstPress = FirstPressOffset;

                if (firstPress == null)
                    return null;

                var firstRelease = FirstReleaseOffsetAfter(firstPress.Value, onlyBeforeTail: false);

                if (firstRelease == null)
                    return null;

                return Inputs.Where(input => input.Press && input.OffsetFromHead > firstRelease.Value)
                    .OrderBy(input => input.OffsetFromHead)
                    .Select(input => (double?)input.OffsetFromHead)
                    .FirstOrDefault();
            }
        }

        public double? FirstReleaseOffsetAfter(double pressOffset, bool onlyBeforeTail)
            => Inputs.Where(input => !input.Press && input.OffsetFromHead > pressOffset)
                .Where(input => !onlyBeforeTail || input.OffsetFromHead <= long_note_duration)
                .OrderBy(input => input.OffsetFromHead)
                .Select(input => (double?)input.OffsetFromHead)
                .FirstOrDefault();

        public bool IsPressedAt(double offset)
        {
            var pressed = false;

            foreach (var input in Inputs.OrderBy(input => input.OffsetFromHead))
            {
                if (input.OffsetFromHead > offset)
                    break;

                pressed = input.Press;
            }

            return pressed;
        }
    }

    [Test]
    public void TestChargeNoteMode()
        => runMode("CN", new BmsModChargeNote(), BmsLongNoteMode.ChargeNote);

    [Test]
    public void TestHeldChargeNoteTailContinuesPastJudgementLineWithoutReversingBody()
    {
        const int no_release_case_index = 5;

        AddStep("load player in CN mode", () => LoadPlayer([new BmsModChargeNote()]));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("seek held note after tail", () =>
        {
            const double start_time = first_case_time + no_release_case_index * case_spacing;
            Player.GameplayClockContainer.Seek(start_time + long_note_duration + 260);
            Player.GameplayClockContainer.Stop();
        });
        AddUntilStep("held long note alive", () => getCaseLongNote(no_release_case_index)?.Alpha > 0);
        AddStep("assert tail moved below judgement line without body reversal", () =>
        {
            var longNote = getCaseLongNote(no_release_case_index);

            Assert.That(longNote, Is.Not.Null);

            var tailBottom = tailBottomOf(longNote!);
            var judgementLine = Playfield.JudgementLineY();
            var body = longNoteBodyOf(longNote!);

            Assert.That(tailBottom, Is.GreaterThan(judgementLine + 1),
                $"tailBottom={tailBottom}, judgementLine={judgementLine}, noteTop={BmsPlayfieldAssertions.TopOf(longNote!)}, noteBottom={BmsPlayfieldAssertions.BottomOf(longNote!)}");
            Assert.That(body.Alpha, Is.EqualTo(0),
                $"body should not extend from the held judgement-line head toward a tail that has already crossed it. bodyHeight={body.Height}");
        });
    }

    [Test]
    public void TestFastHitHeadFallsUntilItReachesJudgementLine()
    {
        const int fast_press_case_index = 6;
        const double start_time = first_case_time + fast_press_case_index * case_spacing;

        AddStep("load player in LN mode", () => LoadPlayer([new BmsModLongNote()]));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("seek after fast press but before head time", () =>
        {
            Player.GameplayClockContainer.Seek(start_time - 40);
            Player.GameplayClockContainer.Stop();
        });
        AddUntilStep("fast-hit long note alive", () => getCaseLongNote(fast_press_case_index)?.Alpha > 0);
        AddAssert("fast-hit head remains above judgement line", () =>
        {
            var longNote = getCaseLongNote(fast_press_case_index);
            return longNote != null && headBottomOf(longNote) < Playfield.JudgementLineY() - 1;
        });
        AddStep("seek past head time", () => Player.GameplayClockContainer.Seek(start_time + 20));
        AddUntilStep("held head reaches judgement line", () =>
        {
            var longNote = getCaseLongNote(fast_press_case_index);
            return longNote != null && Math.Abs(headBottomOf(longNote) - Playfield.JudgementLineY()) < 1;
        });
    }

    [TestCase(100, true)]
    [TestCase(-100, false)]
    public void TestVisualOffsetChangesPinnedHeadPosition(double visualOffset, bool belowJudgementLine)
    {
        const int fast_press_case_index = 6;
        const double start_time = first_case_time + fast_press_case_index * case_spacing;

        AddStep("load player in LN mode", () => LoadPlayer([new BmsModLongNote()]));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("set visual offset", () => Playfield.VisualOffset.Value = visualOffset);
        AddStep("seek after head time", () =>
        {
            Player.GameplayClockContainer.Seek(start_time + 20);
            Player.GameplayClockContainer.Stop();
        });
        AddUntilStep("fast-hit long note alive", () => getCaseLongNote(fast_press_case_index)?.Alpha > 0);
        AddUntilStep("visual offset moves held head", () =>
        {
            var longNote = getCaseLongNote(fast_press_case_index);
            if (longNote == null)
                return false;

            return belowJudgementLine
                ? headBottomOf(longNote) > Playfield.JudgementLineY() + 1
                : headBottomOf(longNote) < Playfield.JudgementLineY() - 1;
        });
        AddStep("reset visual offset", () => Playfield.VisualOffset.Value = 0);
    }

    [Test]
    public void TestReleasedHeadResumesNaturalPosition()
    {
        const int very_fast_release_case_index = 2;
        const double start_time = first_case_time + very_fast_release_case_index * case_spacing;

        AddStep("load player in LN mode", () => LoadPlayer([new BmsModLongNote()]));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("seek after fast release", () =>
        {
            Player.GameplayClockContainer.Seek(start_time + 700);
            Player.GameplayClockContainer.Stop();
        });
        AddUntilStep("long note enters released state", () =>
        {
            var longNote = getCaseLongNote(very_fast_release_case_index);
            return longNote?.Alpha > 0 && Math.Abs(longNoteBodyOf(longNote).Alpha - 0.4f) < 0.01f;
        });
        AddAssert("released head has moved past judgement line", () =>
        {
            var longNote = getCaseLongNote(very_fast_release_case_index);
            return longNote != null && headBottomOf(longNote) > Playfield.JudgementLineY() + 1;
        });
    }

    [Test]
    public void TestHellChargeNoteMode()
        => runMode("HCN", new BmsModHellChargeNote(), BmsLongNoteMode.HellChargeNote);

    [Test]
    public void TestLongNoteMode()
        => runMode("LN", new BmsModLongNote(), BmsLongNoteMode.LongNote);
}
