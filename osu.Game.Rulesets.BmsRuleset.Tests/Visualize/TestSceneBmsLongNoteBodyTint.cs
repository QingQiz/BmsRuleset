#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsLongNoteBodyTint : BmsPlayerTestScene
{
    private const double start_time = 3000;
    private const double second_start_time = 5500;
    private const double duration = 900;
    private const double fast_release_offset = -500;
    private const double repress_offset = -200;

    private BmsLongNoteMode mode = BmsLongNoteMode.ChargeNote;
    private bool? useLazer;
    private double secondReleaseOffset;
    private Func<BmsBeatmap, IList<ReplayFrame>> replayFrames = null!;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(replayFrames);

    private void loadPlayer()
        => loadPlayer(createReplayFrames);

    private void loadPlayer(Func<BmsBeatmap, IList<ReplayFrame>> frames)
    {
        replayFrames = frames;
        LoadPlayer(useLazer.HasValue
            ? [new BmsModManiaProfile { OverallDifficulty = { Value = 8 }, UseLazer = { Value = useLazer.Value } }]
            : []);
    }

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 160,
            LockedLongNoteMode = mode,
            HitObjects =
            {
                new BmsLongNote
                {
                    Kind = mode.ToNoteKind(),
                    StartTime = start_time,
                    Duration = duration,
                    Column = 1,
                },
                new BmsLongNote
                {
                    Kind = mode.ToNoteKind(),
                    StartTime = second_start_time,
                    Duration = duration,
                    Column = 1,
                },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    private IList<ReplayFrame> createReplayFrames(BmsBeatmap beatmap)
    {
        var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, 1)!.Value;

        return
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(start_time, action),
            new BmsReplayFrame(start_time + duration + fast_release_offset),
            new BmsReplayFrame(start_time + duration + repress_offset, action),
            new BmsReplayFrame(start_time + duration + 100),
            new BmsReplayFrame(second_start_time, action),
            new BmsReplayFrame(second_start_time + duration + secondReleaseOffset),
            new BmsReplayFrame(second_start_time + duration + 1000),
        ];
    }

    private static Drawable longNoteBodyOf(DrawableBmsHitObject longNote)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (Drawable)longNote.GetType().GetField("longNoteBody", flags)!.GetValue(longNote)!;
    }

    private static Drawable longNoteTailOf(DrawableBmsHitObject longNote)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (Drawable)longNote.GetType().GetField("longNoteTail", flags)!.GetValue(longNote)!;
    }

    private static Drawable noteHeadOf(DrawableBmsHitObject longNote)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (Drawable)longNote.GetType().GetField("head", flags)!.GetValue(longNote)!;
    }

    private static Drawable noteHeadDrawableOf(DrawableBmsHitObject longNote)
        => ((BmsNoteVisual)noteHeadOf(longNote)).Drawable;

    [TestCase(BmsLongNoteMode.LongNote, 0.4f, false, null)]
    [TestCase(BmsLongNoteMode.ChargeNote, 0.4f, false, null)]
    [TestCase(BmsLongNoteMode.HellChargeNote, 1f, true, null)]
    [TestCase(BmsLongNoteMode.LongNote, 0.4f, false, true)]
    [TestCase(BmsLongNoteMode.LongNote, 0.4f, false, false)]
    public void TestFailedBodyRepressVisual(BmsLongNoteMode mode, float expectedAlpha, bool expectedPinned, bool? useLazer)
    {
        AddStep("set long note mode", () =>
        {
            this.mode = mode;
            this.useLazer = useLazer;
            secondReleaseOffset = 0;
        });
        AddStep("load player", loadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddAssert("selected gameplay profile is active", () => Playfield.EngineChart.Configuration.ProfileId,
            () => Is.EqualTo(useLazer.HasValue ? useLazer.Value ? "osu-lazer-mania-standardised" : "osu-stable-mania-score-v1" : "bms-ruleset-v1"));
        seek(start_time - 100);
        seek(start_time + 300);
        AddUntilStep("held body and tail are fully opaque", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTime(start_time);
            return longNote != null
                   && longNoteBodyOf(longNote).Alpha == 1f
                   && longNoteTailOf(longNote).Alpha == 1f;
        });
        AddUntilStep("body starts at head centre", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTime(start_time);

            if (longNote == null)
                return false;

            var bodyTop = BmsPlayfieldAssertions.TopOf(longNoteBodyOf(longNote));
            var bodyBottom = BmsPlayfieldAssertions.BottomOf(longNoteBodyOf(longNote));
            var headDrawable = noteHeadDrawableOf(longNote);
            var headTop = BmsPlayfieldAssertions.TopOf(headDrawable);
            var headBottom = BmsPlayfieldAssertions.BottomOf(headDrawable);

            var headCentre = (headTop + headBottom) / 2;
            var bodyEdgeNearHead = Math.Abs(bodyTop - headCentre) < Math.Abs(bodyBottom - headCentre) ? bodyTop : bodyBottom;
            return Math.Abs(bodyEdgeNearHead - headCentre) <= 1;
        });
        seek(start_time + duration + fast_release_offset + 120);
        // A fast release fades body+tail together (matches DrawableBmsLongNote.released_alpha) instead
        // of greying only the body, so a coloured tail no longer clashes with a grey body.
        AddUntilStep("released body and tail are faded", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTime(start_time);
            return longNote != null
                   && longNoteBodyOf(longNote).Alpha == 0.4f
                   && longNoteTailOf(longNote).Alpha == 0.4f;
        });
        seek(start_time + duration + repress_offset + 120);
        AddUntilStep("failed repress has mode-specific alpha", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTime(start_time);
            return longNote != null
                   && longNoteBodyOf(longNote).Alpha == expectedAlpha
                   && longNoteTailOf(longNote).Alpha == expectedAlpha;
        });
        AddUntilStep("failed repress has mode-specific position", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTime(start_time);

            if (longNote == null)
                return false;

            var headY = BmsPlayfieldAssertions.TopOf(noteHeadOf(longNote));
            var judgementLineY = Playfield.JudgementLineY();
            return expectedPinned
                ? Math.Abs(headY - judgementLineY) <= 1
                : headY > judgementLineY + 1;
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TestImmediateChargeNoteRepressRetainsDimStyle(bool missedManiaHead)
    {
        const double release_time = start_time + 200;
        AddStep("load CN replay", () =>
        {
            mode = BmsLongNoteMode.ChargeNote;
            useLazer = missedManiaHead ? true : null;
            loadPlayer(beatmap =>
            {
                var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, 1)!.Value;
                return [
                    new BmsReplayFrame(0),
                    new BmsReplayFrame(missedManiaHead ? start_time - 300 : start_time, action),
                    new BmsReplayFrame(release_time),
                    new BmsReplayFrame(release_time + 1, action),
                    new BmsReplayFrame(start_time + duration),
                ];
            });
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1 && Playfield.Stage.IsLoaded);
        seek(start_time - 400);

        foreach (var time in new[] { release_time, release_time + 1, release_time + 100, release_time + 250 })
        {
            AddStep($"play to {time}", () => SeekReplayInFrames(time));
            AddAssert("body retains its drawn opacity", () =>
            {
                var note = Playfield.GetAliveObjectAtTime(start_time);
                return note != null && (!missedManiaHead || note.Result.Type == HitResult.Miss)
                       && Math.Abs(longNoteBodyOf(note).DrawColourInfo.Colour.MinAlpha - 0.4f) < 0.001f
                       && Math.Abs(longNoteTailOf(note).DrawColourInfo.Colour.MinAlpha - 0.4f) < 0.001f;
            });
        }

        AddStep("play past tail deadline", () => SeekReplayInFrames(start_time + duration + 300));
        AddAssert("long note retired", () => Playfield.GetAliveObjectAtTime(start_time) == null);
    }

    [Test]
    public void TestTailNonPoorReleaseClearsBody()
    {
        AddStep("set ordinary CN release", () =>
        {
            mode = BmsLongNoteMode.ChargeNote;
            useLazer = null;
            secondReleaseOffset = 0;
        });
        AddStep("load player", loadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        seek(second_start_time - 100);
        seek(second_start_time + duration + 20);
        AddUntilStep("tail non-poor clears long note", () => Playfield.GetAliveObjectAtTime(second_start_time) == null);
    }

    [TestCase(BmsLongNoteMode.LongNote, -165, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.ChargeNote, -165, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.LongNote, -120, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.ChargeNote, -120, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.LongNote, -90, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.ChargeNote, -90, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.HellChargeNote, -165, true, 0.4f, null)]
    [TestCase(BmsLongNoteMode.HellChargeNote, -120, true, 1f, null)]
    [TestCase(BmsLongNoteMode.HellChargeNote, -90, true, 1f, null)]
    [TestCase(BmsLongNoteMode.LongNote, -246.751, true, 0.4f, true)]
    [TestCase(BmsLongNoteMode.LongNote, -246.75, false, 0f, true)]
    [TestCase(BmsLongNoteMode.LongNote, -191.25, false, 0f, true)]
    [TestCase(BmsLongNoteMode.LongNote, -29.25, false, 0f, true)]
    [TestCase(BmsLongNoteMode.LongNote, -127.001, true, 0.4f, false)]
    [TestCase(BmsLongNoteMode.LongNote, -127, false, 0f, false)]
    [TestCase(BmsLongNoteMode.LongNote, -30, false, 0f, false)]
    public void TestEarlyTailWindowBodyVisibility(BmsLongNoteMode noteMode, double releaseOffset, bool visible, float alpha, bool? useLazer)
    {
        AddStep("configure tail release", () =>
        {
            mode = noteMode;
            this.useLazer = useLazer;
            secondReleaseOffset = releaseOffset;
        });
        AddStep("load player", loadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddAssert("selected gameplay profile is active", () => Playfield.EngineChart.Configuration.ProfileId,
            () => Is.EqualTo(useLazer.HasValue ? useLazer.Value ? "osu-lazer-mania-standardised" : "osu-stable-mania-score-v1" : "bms-ruleset-v1"));
        seek(second_start_time + duration + releaseOffset - 1);
        AddUntilStep("held long note is visible", () => Playfield.GetAliveObjectAtTime(second_start_time) != null);
        seek(second_start_time + duration + releaseOffset + 1);
        AddUntilStep("released body visibility matches presentation", () =>
        {
            var drawable = Playfield.GetAliveObjectAtTime(second_start_time);
            return visible
                ? drawable != null && longNoteBodyOf(drawable).Alpha == alpha && longNoteTailOf(drawable).Alpha == alpha
                : drawable == null;
        });
    }

    private void seek(double time)
    {
        // These visual states last only until the next replay input or tail. Keep the clock fixed
        // while assertions run, and wait for replay simulation rather than just the parent clock.
        AddStep($"seek to {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation reached target", () => Player.DrawableRuleset.FrameStableClock.CurrentTime, () => Is.EqualTo(time).Within(0.000001));
    }
}
