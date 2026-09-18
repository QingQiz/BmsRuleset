using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.Scoring;
using osu.Game.Rulesets.Replays;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[HeadlessTest]
public partial class TestSceneBmsReplayRewindTiming : BmsPlayerTestScene
{
    private double inputOffset;
    private bool splitChords;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(createReplay);

    private IList<ReplayFrame> createReplay(BmsBeatmap beatmap)
    {
        var frames = BmsTestReplays.CreateOffsetAutoPlayFrames(beatmap, inputOffset);
        if (!splitChords)
            return frames;

        var result = new List<ReplayFrame>();
        var pressed = new List<BmsAction>();
        // Live recording emits a frame per action, so chords can advance the drawable
        // lifecycle between two inputs with the same timestamp.
        foreach (var frame in frames.Cast<BmsReplayFrame>())
        {
            foreach (var action in pressed.Except(frame.Actions).ToArray())
            {
                pressed.Remove(action);
                result.Add(new BmsReplayFrame(frame.Time, pressed.ToArray()));
            }

            foreach (var action in frame.Actions.Except(pressed).ToArray())
            {
                pressed.Add(action);
                result.Add(new BmsReplayFrame(frame.Time, pressed.ToArray()));
            }
        }

        return result;
    }

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
        };
        for (var i = 0; i < 90; i++)
        {
            beatmap.HitObjects.Add(new BmsNote { StartTime = 1000 + i * 100, Column = i % 7 + 1 });
            beatmap.HitObjects.Add(new BmsNote { StartTime = 1000 + i * 100, Column = (i + 3) % 7 + 1 });
        }

        beatmap.HitObjects.Add(new BmsNote { StartTime = 20000, Column = 1 });
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [Test]
    public void TestShortNoteTimingAfterRepeatedRewinds([Values(-150, -25, 0, 25, 150)] double offset, [Values] bool split)
    {
        BmsTimingObservation[] original = [];
        AddStep("load dense replay", () =>
        {
            inputOffset = offset;
            splitChords = split;
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
        seek(11000);
        AddStep("capture original timings", () => original = observations());
        AddAssert("all chords judged at recorded time", () =>
            original.Length == 180 && original.All(o => Math.Abs(o.TimeOffset - offset) < 0.001));

        double[] targets = [500, 1000 + offset, 1450, 4000 + offset - 1, 4000 + offset, 4000 + offset + 1, 7555];
        foreach (var target in targets)
        {
            seek(target);
            AddAssert($"timings restored at {target}", () => observations(),
                () => Is.EqualTo(original.Where(o => o.ActualTime <= target).ToArray()));
            seek(11000);
            AddAssert($"same timings after replay from {target}", () => observations(), () => Is.EqualTo(original));
        }
    }

    private BmsTimingObservation[] observations()
        => ((BmsScoreProcessor)Player.ScoreProcessor).JudgementEvents.SelectMany(e => e.TimingObservations).ToArray();

    private void seek(double time)
    {
        AddStep($"seek {time}", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(time);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < 0.001);
    }
}
