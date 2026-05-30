using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Framework.Testing.Drawables.Steps;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.Replays;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsSkins : PlayerTestScene, IStorageResourceProvider
{
    private const double initial_health = 0.2;
    private const double first_note_time = 2500;
    private const double ln_scenario_start_time = first_note_time + 12000;
    private const double ln_scenario_spacing = 1400;
    private const double ln_scenario_duration = 800;

    private SkinDefinition skinDefinition;

    [Resolved]
    private GameHost host { get; set; } = null!;

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => new SkinProvidingPlayer(new SkinProvidingContainer(skinDefinition.CreateSkin(this)));

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = createBeatmap();

        beatmap.BeatmapInfo.Ruleset = ruleset;
        beatmap.BeatmapInfo.Difficulty.CircleSize = beatmap.TotalColumns;
        beatmap.BeatmapInfo.Difficulty.OverallDifficulty = 6;
        beatmap.BeatmapInfo.Difficulty.DrainRate = 5;
        beatmap.BeatmapInfo.BPM = 130;
        beatmap.BeatmapInfo.Length = (int)(beatmap.HitObjects.Max(h => h.EndTime) + 2500);

        return beatmap;
    }

    private void createSkinScene(SkinDefinition definition)
    {
        addSetupStep($"use {definition.Name} skin", () => skinDefinition = definition);
        addSetupStep($"load {definition.Name} player", LoadPlayer);
        addSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        addSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        addSetupAssert("loaded bms drawable ruleset", () => Player.DrawableRuleset, Is.TypeOf<BmsDrawableRuleset>());
        addSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());
        addSetupUntilStep("gameplay hud loaded", () => Player.HUDOverlay.IsLoaded);
        addSetupUntilStep("hud skin components loaded", () => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>().All(c => c.ComponentsLoaded));
        addSetupUntilStep("bms stage loaded", () => ((BmsPlayfield)Player.DrawableRuleset.Playfield).Stage.IsLoaded);
        addSetupAssert("bms health display present", () => Player.DrawableRuleset.Playfield.ChildrenOfType<BmsHealthDisplay>().Any());

        addSetupUntilStep("all hit results produced", () => BmsRuleset.STATIC_VALID_HIT_RESULTS.All(result => Player.ScoreProcessor.Statistics.GetValueOrDefault(result) > 0));
        addSetupAssert("judgement display active", () => ((BmsPlayfield)Player.DrawableRuleset.Playfield).Stage.JudgementArea.Count, Is.GreaterThan(0));
        addSetupAssert("health changed", () => Math.Abs(Player.HealthProcessor.Health.Value - initial_health), Is.GreaterThan(0.01));
        addSetupAssert("score changed", () => Player.ScoreProcessor.TotalScore.Value, Is.GreaterThan(0));
        AddStep("skin scene complete", () => { });
    }

    private void addSetupStep(string description, Action action)
        => AddStep(new SingleStepButton
        {
            Text = description,
            IsSetupStep = true,
            Action = action,
        });

    private void addSetupUntilStep(string description, Func<bool> assertion)
        => AddStep(new UntilStepButton
        {
            Text = description,
            IsSetupStep = true,
            CallStack = new StackTrace(1, true),
            Assertion = assertion,
        });

    private void addSetupAssert(string description, Func<bool> assertion)
        => AddStep(new AssertButton
        {
            Text = description,
            IsSetupStep = true,
            CallStack = new StackTrace(1, true),
            Assertion = assertion,
        });

    private void addSetupAssert<T>(string description, Func<T> actualValue, IResolveConstraint constraint)
        => addSetupAssert(description, () => constraint.Resolve().ApplyTo(actualValue()).IsSuccess);

    private static BmsBeatmap createBeatmap()
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
                StartTime = first_note_time + i * 125,
                Column = pattern[i],
            });
        }

        int[] lnScenarioColumns = [0, 7, 3, 5, 2, 6, 1];

        for (var i = 0; i < lnScenarioColumns.Length; i++)
        {
            beatmap.HitObjects.Add(new BmsHitObject
            {
                StartTime = ln_scenario_start_time + i * ln_scenario_spacing,
                Column = lnScenarioColumns[i],
                IsLongNote = true,
                Duration = ln_scenario_duration,
            });
        }

        beatmap.HitObjects.Add(new BmsHitObject { StartTime = first_note_time + 6000, Column = 6, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = first_note_time + 7000, Column = 1, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = first_note_time + 8250, Column = 4, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = first_note_time + 9500, Column = 0, IsMine = true, LandmineDamagePercent = 2.5 });
        beatmap.HitObjects.Add(new BmsHitObject { StartTime = first_note_time + 11000, Column = 7, IsMine = true, LandmineDamagePercent = 2.5 });

        return beatmap;
    }

    public IRenderer Renderer => host.Renderer;

    public AudioManager AudioManager => Audio;

    public IResourceStore<byte[]> Files => null!;

    public new IResourceStore<byte[]> Resources => base.Resources;

    public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => host.CreateTextureLoaderStore(underlyingStore);

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

    private readonly record struct SkinDefinition(string Name, Func<IStorageResourceProvider, ISkin> CreateSkin);

    private partial class SkinProvidingPlayer(ISkinSource skinSource) : TestPlayer(false, false)
    {
        [Cached(typeof(ISkinSource))]
        private readonly ISkinSource skinSource = skinSource;

        private readonly record struct ActionPoint(double Time, BmsAction Action, bool Press);

        protected override void PrepareReplay()
        {
            var beatmap = (BmsBeatmap)GameplayState.Beatmap;

            DrawableRuleset?.SetReplayScore(new Score
            {
                Replay = new Replay { Frames = createReplayFrames(beatmap).Cast<ReplayFrame>().ToList() },
            });
        }

        private static IEnumerable<BmsReplayFrame> createReplayFrames(BmsBeatmap beatmap)
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
            yield return new BmsReplayFrame(0);

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

                yield return new BmsReplayFrame(group.Key, activeActions.ToArray());
            }

            static void addPress(List<ActionPoint> actionPoints, double time, BmsHitObject hitObject)
            {
                if (BmsKeyBindingConfiguration.ActionForColumn(BmsLayoutVariant.Bme7K, hitObject.Column) is not { } action)
                    return;

                actionPoints.Add(new ActionPoint(time, action, true));
                actionPoints.Add(new ActionPoint(time + 20, action, false));
            }

            static bool tryAddLongNoteScenario(List<ActionPoint> actionPoints, BmsHitObject hitObject, BmsAction action)
            {
                var index = (int)Math.Round((hitObject.StartTime - ln_scenario_start_time) / ln_scenario_spacing);

                if (index < 0 || index > (int)LnScenario.EarlyRelease)
                    return false;

                var expectedStartTime = ln_scenario_start_time + index * ln_scenario_spacing;

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

            static void addHold(List<ActionPoint> actionPoints, BmsAction action, double pressTime, double releaseTime)
            {
                actionPoints.Add(new ActionPoint(pressTime, action, true));
                actionPoints.Add(new ActionPoint(releaseTime, action, false));
            }
        }

        private enum LnScenario
        {
            EarlyPress,
            LatePress,
            NoPress,
            MidRelease,
            NoRelease,
            LateRelease,
            EarlyRelease,
        }
    }

    [Test]
    public void TestArgonSkin()
    {
        createSkinScene(new SkinDefinition("Argon", resources => new ArgonSkin(resources)));
    }

    [Test]
    public void TestClassicSkin()
    {
        createSkinScene(new SkinDefinition("Classic", resources => new DefaultLegacySkin(resources)));
    }
}
