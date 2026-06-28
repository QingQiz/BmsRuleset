#nullable enable
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Objects.Drawables;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsLongNoteBodyTint : BmsPlayerTestScene
{
    private const double start_time = 3000;
    private const double second_start_time = 5500;
    private const double duration = 900;
    private const double early_release_offset = -500;
    private const long tick = 192;
    private const long second_tick = 384;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(createReplayFrames);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 160,
            LockedLongNoteMode = BmsLongNoteMode.ChargeNote,
            HitObjects =
            {
                new BmsLongNote
                {
                    StartTime = start_time,
                    Duration = duration,
                    Column = 1,
                    TickInfo = new BmsTickInfo { Tick = tick, EndTick = tick + 96 },
                },
                new BmsLongNote
                {
                    StartTime = second_start_time,
                    Duration = duration,
                    Column = 1,
                    TickInfo = new BmsTickInfo { Tick = second_tick, EndTick = second_tick + 96 },
                },
            },
        };

        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset, endPadding: 3000, bpm: 120);
        return beatmap;
    }

    private static IList<ReplayFrame> createReplayFrames(BmsBeatmap beatmap)
    {
        var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, 1)!.Value;

        return
        [
            new BmsReplayFrame(0),
            new BmsReplayFrame(start_time, action),
            new BmsReplayFrame(start_time + duration + early_release_offset),
            new BmsReplayFrame(second_start_time, action),
            new BmsReplayFrame(second_start_time + duration),
        ];
    }

    private static Drawable longNoteBodyOf(DrawableBmsHitObject longNote)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (Drawable)longNote.GetType().GetField("longNoteBody", flags)!.GetValue(longNote)!;
    }

    private static Drawable longNoteTailContainerOf(DrawableBmsHitObject longNote)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (Drawable)longNote.GetType().GetField("longNoteTailContainer", flags)!.GetValue(longNote)!;
    }

    [Test]
    public void TestBodyAlphaTracksHeldState()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("seek before long note", () => Player.GameplayClockContainer.Seek(start_time - 100));
        AddUntilStep("held body before release", () => Player.GameplayClockContainer.CurrentTime >= start_time + 300);
        AddAssert("held body and tail are fully opaque", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTick(tick);
            return longNote != null
                   && longNoteBodyOf(longNote).Alpha == 1f
                   && longNoteTailContainerOf(longNote).Alpha == 1f;
        });
        AddUntilStep("released body before tail", () => Player.GameplayClockContainer.CurrentTime >= start_time + duration + early_release_offset + 120);
        // Released-early fades body+tail together (matches DrawableBmsLongNote.released_alpha) instead
        // of greying only the body, so a coloured tail no longer clashes with a grey body.
        AddAssert("released body and tail are faded", () =>
        {
            var longNote = Playfield.GetAliveObjectAtTick(tick);
            return longNote != null
                   && longNoteBodyOf(longNote).Alpha == 0.4f
                   && longNoteTailContainerOf(longNote).Alpha == 0.4f;
        });
    }

    [Test]
    public void TestTailNonPoorReleaseClearsBody()
    {
        AddStep("load player", LoadPlayer);
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        AddUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        AddStep("seek before normal tail", () => Player.GameplayClockContainer.Seek(second_start_time - 100));
        AddUntilStep("past tail release", () => Player.GameplayClockContainer.CurrentTime >= second_start_time + duration + 20);
        AddAssert("tail non-poor clears long note", () => Playfield.GetAliveObjectAtTick(second_tick) == null);
    }
}
