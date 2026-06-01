using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework.Constraints;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Framework.Testing.Drawables.Steps;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public static partial class TestPlayFieldCreator
{
    public const double INIT_HEALTH = 0.2;
    public const double FIRST_NOTE_TIME = 2500;
    public const double NORM_SCENARIO_START_TIME = FIRST_NOTE_TIME + 6000;
    public const double LN_SCENARIO_START_TIME = FIRST_NOTE_TIME;
    public const double LN_SCENARIO_SPACING = 700;
    public const double LN_SCENARIO_DURATION = 800;

    private readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

    /// <summary>
    /// Creates a <see cref="BmsBeatmap"/> with normal notes (varied columns and timing),
    /// long note scenarios (early/late press, mid release, etc.), and landmines.
    /// Designed so a replay with deliberate timing offsets can produce every hit result.
    /// </summary>
    public static BmsBeatmap CreateBeatmap()
    {
        var beatmap = new BmsBeatmap
        {
            LayoutVariant = BmsLayoutVariant.Bme7K,
            TotalColumns = 8,
            Rank = 2,
            Total = 160,
        };

        int[] pattern =
        [
            0, 2, 4, 6, 1, 3, 5, 7,
            0, 4, 2, 6, 3, 7, 1, 5,
            0, 1, 2, 3, 4, 5, 6, 7,
            7, 6, 5, 4, 3, 2, 1, 0,
            0, 2, 4, 6, 1, 3, 5, 7,
            0, 4, 2, 6, 3, 7, 1, 5,
        ];

        for (var i = 0; i < pattern.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = NORM_SCENARIO_START_TIME + i * 125,
                Column = pattern[i],
            });
        }

        int[] lnScenarioColumns = [0, 7, 3, 5, 2, 6, 1];

        for (var i = 0; i < lnScenarioColumns.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = LN_SCENARIO_START_TIME + i * LN_SCENARIO_SPACING,
                Column = lnScenarioColumns[i],
                IsLongNote = true,
                Duration = LN_SCENARIO_DURATION,
            });
        }

        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 6000, Column = 6, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 7000, Column = 1, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 8250, Column = 4, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 9500, Column = 0, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = FIRST_NOTE_TIME + 11000, Column = 7, IsMine = true, LandmineDamagePercent = 2.5 });

        return beatmap;
    }

    /// <summary>
    /// Sets up the <see cref="BeatmapInfo"/> on a beatmap created by <see cref="CreateBeatmap"/>.
    /// </summary>
    /// <param name="ruleset"></param>
    /// <param name="endPadding">Milliseconds added after the last hit object's end time to determine <see cref="BeatmapInfo.Length"/>.</param>
    /// <param name="beatmap"></param>
    public static void SetupBeatmapInfo(BmsBeatmap beatmap, RulesetInfo ruleset, double endPadding = 2500)
    {
        beatmap.BeatmapInfo.Ruleset = ruleset;
        beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
        beatmap.BeatmapInfo.Difficulty.OverallDifficulty = 6;
        beatmap.BeatmapInfo.Difficulty.DrainRate = 5;
        beatmap.BeatmapInfo.BPM = 130;
        beatmap.BeatmapInfo.Length = (int)(beatmap.HitObjects.Max(h => h.EndTime) + endPadding);
    }

    /// <summary>
    /// Decodes a BMS chart string into a <see cref="BmsBeatmap"/>.
    /// </summary>
    public static BmsBeatmap CreateBeatmapFromChart(string chart)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(chart));
        using var reader = new LineBufferedReader(stream);
        var decoded = new BmsBeatmapDecoder().Decode(reader);
        return (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
    }

    /// <summary>
    /// Generates replay frames with deliberate timing offsets so that every
    /// <see cref="BmsRuleset.STATIC_VALID_HIT_RESULTS"/> hit result is produced,
    /// including a deliberate Miss. Long notes use scenario-based timing
    /// (early/late press, mid release, no release, etc.).
    /// </summary>
    public static IList<ReplayFrame> CreateReplayFrames(BmsBeatmap beatmap)
    {
        var hitObjects = beatmap.HitObjects
            .OrderBy(h => h.StartTime)
            .ToArray();

        var actionPoints = new List<ActionPoint>();

        // One deliberate empty POOR before the first note's POOR window, so HitResult.Miss is visible too.
        addPress(actionPoints, hitObjects[0].StartTime - 1200, hitObjects[0]);

        var offsets = new[]
        {
            0d,   // PGREAT
            25d,  // GREAT
            70d,  // GOOD
            150d, // BAD
            -500d // POOR
        };

        for (var i = 0; i < hitObjects.Length; i++)
        {
            var hitObject = hitObjects[i];
            var action = BmsKeyBindingConfiguration.ActionForColumn(beatmap.LayoutVariant, hitObject.Column);

            if (action == null)
                continue;

            if (hitObject.IsLongNote && tryAddLongNoteScenario(actionPoints, hitObject, action.Value))
                continue;

            var time = hitObject.StartTime + offsets[i % offsets.Length];

            actionPoints.Add(new ActionPoint(time, action.Value, true));
            actionPoints.Add(new ActionPoint((hitObject.IsLongNote ? hitObject.EndTime : time) + 20, action.Value, false));
        }

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

    private static void addPress(List<ActionPoint> actionPoints, double time, BmsHitObject hitObject)
    {
        if (BmsKeyBindingConfiguration.ActionForColumn(BmsLayoutVariant.Bme7K, hitObject.Column) is not { } action)
            return;

        actionPoints.Add(new ActionPoint(time, action, true));
        actionPoints.Add(new ActionPoint(time + 20, action, false));
    }

    private static bool tryAddLongNoteScenario(List<ActionPoint> actionPoints, BmsHitObject hitObject, BmsAction action)
    {
        var index = (int)Math.Round((hitObject.StartTime - LN_SCENARIO_START_TIME) / LN_SCENARIO_SPACING);

        if (index is < 0 or > (int)LnScenario.EarlyRelease)
            return false;

        var expectedStartTime = LN_SCENARIO_START_TIME + index * LN_SCENARIO_SPACING;

        if (Math.Abs(hitObject.StartTime - expectedStartTime) > 0.001)
            return false;

        switch ((LnScenario)index)
        {
            case LnScenario.EarlyPress:
                addHold(actionPoints, action, hitObject.StartTime - 150, hitObject.EndTime + 20);
                break;

            case LnScenario.LatePress:
                addHold(actionPoints, action, hitObject.StartTime + 150, hitObject.EndTime + 20);
                break;

            case LnScenario.NoPress:
                break;

            case LnScenario.MidRelease:
                addHold(actionPoints, action, hitObject.StartTime, hitObject.StartTime + hitObject.Duration / 2);
                break;

            case LnScenario.NoRelease:
                actionPoints.Add(new ActionPoint(hitObject.StartTime, action, true));
                break;

            case LnScenario.LateRelease:
                addHold(actionPoints, action, hitObject.StartTime, hitObject.EndTime + 150);
                break;

            case LnScenario.EarlyRelease:
                addHold(actionPoints, action, hitObject.StartTime, hitObject.EndTime - 150);
                break;
        }

        return true;
    }

    private static void addHold(List<ActionPoint> actionPoints, BmsAction action, double pressTime, double releaseTime)
    {
        actionPoints.Add(new ActionPoint(pressTime, action, true));
        actionPoints.Add(new ActionPoint(releaseTime, action, false));
    }

    public enum LnScenario
    {
        EarlyPress,
        LatePress,
        NoPress,
        MidRelease,
        NoRelease,
        LateRelease,
        EarlyRelease,
    }

    /// <summary>
    /// Generates perfect autoplay replay frames for a beatmap using <see cref="BmsAutoGenerator"/>.
    /// </summary>
    public static IList<ReplayFrame> CreateAutoPlayFrames(BmsBeatmap beatmap)
        => new BmsAutoGenerator(beatmap).Generate().Frames;

    /// <summary>
    /// Generates autoplay replay frames with a uniform time offset applied to every frame.
    /// Useful for producing non-perfect judgements from an otherwise perfect autoplay.
    /// </summary>
    public static IList<ReplayFrame> CreateOffsetAutoPlayFrames(BmsBeatmap beatmap, double offset)
        => new BmsAutoGenerator(beatmap).Generate().Frames
            .OfType<BmsReplayFrame>()
            .Select(f => new BmsReplayFrame(f.Time + offset, f.Actions.ToArray()))
            .Cast<ReplayFrame>()
            .ToList();

    public enum SkinKind { Argon, Classic }

    /// <summary>
    /// Creates an <see cref="ISkinSource"/> for the given <see cref="SkinKind"/>.
    /// </summary>
    public static ISkinSource CreateSkinSource(SkinKind kind, IStorageResourceProvider resources)
        => new SkinProvidingContainer(kind == SkinKind.Classic
            ? new DefaultLegacySkin(resources)
            : new ArgonSkin(resources));

    /// <summary>
    /// Generates an empty replay so notes scroll without being hit — useful for
    /// visual inspection of the playfield and skin without gameplay interference.
    /// </summary>
    public static IList<ReplayFrame> CreateWatchOnlyFrames(BmsBeatmap beatmap)
        => new List<ReplayFrame> { new BmsReplayFrame(0) };

    /// <summary>
    /// A <see cref="TestPlayer"/> that injects an <see cref="ISkinSource"/> and optionally
    /// uses a caller-supplied function to generate replay frames.
    /// Pass <c>null</c> for <paramref name="createReplay"/> to disable replay entirely
    /// (notes scroll without being hit, manual input still possible).
    /// </summary>
    public partial class SkinnedTestPlayer(
        ISkinSource skinSource,
        Func<BmsBeatmap, IList<ReplayFrame>>? createReplay)
        : TestPlayer(false, false)
    {
        [Cached(typeof(ISkinSource))]
        private readonly ISkinSource skinSource = skinSource;

        protected override void PrepareReplay()
        {
            if (createReplay == null) return;

            var beatmap = (BmsBeatmap)GameplayState.Beatmap;

            DrawableRuleset?.SetReplayScore(new Score
            {
                Replay = new Replay { Frames = createReplay(beatmap).ToList() },
            });
        }
    }

    #region Setup step helpers

    /// <summary>
    /// Adds a setup step with the given description and action.
    /// </summary>
    public static void AddSetupStep(this TestScene test, string description, Action action)
        => test.AddStep(new SingleStepButton
        {
            Text = description,
            IsSetupStep = true,
            Action = action,
        });

    /// <summary>
    /// Adds a setup step that waits until the assertion is true.
    /// </summary>
    public static void AddSetupUntilStep(this TestScene test, string description, Func<bool> assertion)
        => test.AddStep(new UntilStepButton
        {
            Text = description,
            IsSetupStep = true,
            CallStack = new StackTrace(1, true),
            Assertion = assertion,
        });

    /// <summary>
    /// Adds a setup assertion step.
    /// </summary>
    public static void AddSetupAssert(this TestScene test, string description, Func<bool> assertion)
        => test.AddStep(new AssertButton
        {
            Text = description,
            IsSetupStep = true,
            CallStack = new StackTrace(1, true),
            Assertion = assertion,
        });

    /// <summary>
    /// Adds a setup assertion step with a value and constraint.
    /// </summary>
    public static void AddSetupAssert<T>(this TestScene test, string description, Func<T> actualValue, Constraint constraint)
        => test.AddSetupAssert(description, () => ((IResolveConstraint)constraint).Resolve().ApplyTo(actualValue()).IsSuccess);

    #endregion

}
