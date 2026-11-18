#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Course;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Difficulty;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.LongNoteMode;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Practice;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.IO.Input;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.BeatmapTest.Decoding;
using osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects.Presentation;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Result.Statistic;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Course;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;
using osuTK.Input;

// Partial fixtures keep Mania coverage together while sharing each subsystem's setup and teardown.
namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize
{
    [Category("ManiaProfile")]
    public partial class TestSceneBmsManiaSettings : OsuManualInputManagerTestScene
    {
        [Test]
        public void DefaultsFollowBeatmapUntilOverridden()
        {
            var mod = new BmsModManiaProfile();
            AddStep("load Mania settings", () =>
            {
                Beatmap.Value = CreateWorkingBeatmap(new Beatmap());
                Child = new FillFlowContainer
                {
                    Width = 300, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                    ChildrenEnumerable = mod.CreateSettingsControls(),
                };
            });
            checkValues(8, 8);
            setBeatmap(6, 4);
            checkValues(6, 4);
            AddStep("override OD with explicit eight", () => slider(BmsStrings.ManiaOverallDifficulty.ToString()).Current.Value = 8);
            AddAssert("only OD is overridden", () => mod.OverallDifficulty.Value == 8 && mod.DrainRate.Value == null);
            setBeatmap(3, 9);
            checkValues(8, 9);
            AddStep("reset OD", () => mod.OverallDifficulty.SetDefault());
            checkValues(3, 9);
            AddStep("select BMS", () => Beatmap.Value = CreateWorkingBeatmap(new Beatmap()));
            checkValues(8, 8);
            AddAssert("defaults stay implicit", () => mod.UsesDefaultConfiguration);

            void checkValues(float od, float hp) => AddStep("sliders show effective defaults", () =>
            {
                Assert.That(slider(BmsStrings.ManiaOverallDifficulty.ToString()).Current.Value, Is.EqualTo(od));
                Assert.That(slider(BmsStrings.ManiaDrainRate.ToString()).Current.Value, Is.EqualTo(hp));
            });

            RoundedSliderBar<float> slider(string label) => this.ChildrenOfType<DifficultyAdjustSettingsControl>().Single(control => control.LabelText.ToString() == label)
                .ChildrenOfType<RoundedSliderBar<float>>().Single();

            void setBeatmap(float od, float hp) => AddStep("select mania source", () => Beatmap.Value = CreateWorkingBeatmap(new Beatmap
            {
                BeatmapInfo = new BeatmapInfo
                {
                    Ruleset = new RulesetInfo { OnlineID = 3 },
                    Difficulty = new BeatmapDifficulty { CircleSize = 7, OverallDifficulty = od, DrainRate = hp },
                },
            }));
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Mods
{
    [TestFixture]
    [Category("ManiaProfile")]
    public class BmsModManiaProfileTest
    {
        [TestCase(false, null, null, 8, 8)]
        [TestCase(true, null, null, 6.3, 4.7)]
        [TestCase(true, 8f, null, 8, 4.7)]
        [TestCase(true, null, 0f, 6.3, 0)]
        [TestCase(false, 0f, 10f, 0, 10)]
        public void DifficultyDefaultsAndOverridesFollowSource(bool converted, float? od, float? hp, double expectedOd, double expectedHp)
        {
            var mod = new BmsModManiaProfile { OverallDifficulty = { Value = od }, DrainRate = { Value = hp } };
            var ruleset = new BmsRuleset();
            var decoded = new BmsDecodedBeatmap { RawLines = ["#BPM 120", "#00111:01"] };
            decoded.CopyFrom(BmsChartParser.Parse(decoded.RawLines));
            var source = converted ? createManiaSource() : (IBeatmap)decoded;
            var beatmap = (BmsBeatmap)ruleset.CreateBeatmapConverter(source).Convert();
            var adjusted = ruleset.GetAdjustedDisplayDifficulty(source.BeatmapInfo, [mod]);
            mod.ApplyToBeatmap(beatmap);
            var profile = GameplayProfile.FromMods([mod]);
            var chart = new BmsEngineChart(beatmap, profile);
            var expected = RhythmEngine.Profiles.Mania.ManiaProfiles.CreateLazer(
                new KeyChart(8, [new KeyNote(0, 1, 1000000, 1000000)]),
                new RhythmEngine.Profiles.Mania.ManiaOptions(expectedOd, expectedHp));
            var expectedTiming = ((INoteTiming)expected.Chart.Notes[0].Rule).Timing;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(mod.UseLazer.Value, Is.True);
                Assert.That(adjusted.OverallDifficulty, Is.EqualTo(expectedOd).Within(0.0001));
                Assert.That(adjusted.DrainRate, Is.EqualTo(expectedHp).Within(0.0001));
                Assert.That(chart.TimingFor(chart.Objects[0]).HeadWindows.Entries, Is.EqualTo(expectedTiming.HeadWindows.Entries));
                Assert.That(mod.OverallDifficulty.Value, Is.EqualTo(od));
                Assert.That(mod.DrainRate.Value, Is.EqualTo(hp));
            }

            var practice = BmsPracticeBeatmap.Create(beatmap, BmsPracticeSettings.CreateDefault(beatmap));
            var reconverted = (BmsBeatmap)ruleset.CreateBeatmapConverter(beatmap).Convert();
            foreach (var copy in new[] { practice, reconverted })
            {
                var compiled = new BmsEngineChart(copy, profile);
                Assert.That(compiled.TimingFor(compiled.Objects[0]).HeadWindows.Entries, Is.EqualTo(expectedTiming.HeadWindows.Entries));
            }

            if (converted)
            {
                Assert.That(beatmap.BeatmapInfo.Difficulty.OverallDifficulty, Is.EqualTo(6.3f));
                Assert.That(beatmap.BeatmapInfo.Difficulty.DrainRate, Is.EqualTo(4.7f));
                Assert.That(source.BeatmapInfo.Difficulty.CircleSize, Is.EqualTo(7));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SettingsSurviveCloneAndApiRoundTrip(bool lazer)
        {
            var mod = new BmsModManiaProfile
            {
                OverallDifficulty = { Value = 8 }, DrainRate = { Value = 0 }, UseLazer = { Value = lazer },
            };
            var restored = (BmsModManiaProfile)new APIMod(mod).ToMod(new BmsRuleset());
            var clone = (BmsModManiaProfile)mod.DeepClone();
            foreach (var copy in new[] { restored, clone })
            {
                Assert.That(copy.OverallDifficulty.Value, Is.EqualTo(8));
                Assert.That(copy.DrainRate.Value, Is.Zero);
                Assert.That(copy.UseLazer.Value, Is.EqualTo(lazer));
                Assert.That(copy, Is.EqualTo(mod));
            }

            clone.OverallDifficulty.Value = 2;
            Assert.That(mod.OverallDifficulty.Value, Is.EqualTo(8));
            mod.ResetSettingsToDefaults();
            Assert.That(mod.OverallDifficulty.Value, Is.Null);
            Assert.That(mod.DrainRate.Value, Is.Null);
            Assert.That(mod.UseLazer.Value, Is.True);
        }

        [TestCase(true, 305, 2, 2)]
        [TestCase(false, 300, 1, 10)]
        public void ModeSwitchSelectsCompleteHoldRules(bool lazer, int accuracyValue, int opportunities, int combo)
        {
            var mod = new BmsModManiaProfile { UseLazer = { Value = lazer } };
            using var play = new Play([mod], new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1 });
            play.Press(1000);
            play.Release(2000);
            play.Advance(3000);
            var score = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = [mod] };
            play.Score.PopulateScore(score);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(play.Score.GetBaseScoreForResult(HitResult.Perfect), Is.EqualTo(accuracyValue));
                Assert.That(play.Score.ScoringJudgementEventCount, Is.EqualTo(opportunities));
                Assert.That(score.GetMaximumAchievableCombo(), Is.EqualTo(lazer ? combo : 12));
                Assert.That(play.Score.MaximumCombo, Is.EqualTo(lazer ? combo : 12));
                Assert.That(score.MaxCombo, Is.EqualTo(combo));
                Assert.That(score.TotalScore, Is.EqualTo(1000000));
                Assert.That(score.Accuracy, Is.EqualTo(1));
                Assert.That(BmsLampCalculator.Calculate(score), Is.EqualTo(BmsLamp.Max));
            }

            var replay = new BmsModAutoplay().CreateReplayData(play.Beatmap, [mod]).Replay;
            replay.Frames.Add(new BmsReplayFrame(3000));
            var states = new List<ScoreState>();
            BmsReplaySimulation.Run(play.Beatmap, replay, score, onScore: states.Add);
            Assert.That(states[^1].Score, Is.EqualTo(score.TotalScore));
            Assert.That(states[^1].MaximumCombo, Is.EqualTo(combo));
            play.Engine.Reset();
            play.Score.ResetReplayState();
            play.Health.ResetReplayState();
            play.Press(1000);
            play.Release(2000);
            play.Advance(3000);
            Assert.That(play.Score.TotalScore.Value, Is.EqualTo(score.TotalScore));
            Assert.That(play.Score.Combo.Value, Is.EqualTo(combo));
        }

        [TestCase(true, 8f)]
        [TestCase(false, 0f)]
        [TestCase(false, 10f)]
        public void HealthUsesConfiguredDrain(bool lazer, float hp)
        {
            using var play = new Play([new BmsModManiaProfile { DrainRate = { Value = hp }, UseLazer = { Value = lazer } }],
                new BmsNote { StartTime = 1000, Column = 1 });
            play.Advance(2000);
            Assert.That(play.Health.CurrentHealth.Value, Is.EqualTo(1 - (hp + 1) * 0.0075).Within(0.000001));
            Assert.That(play.Health.DisplayProfile.Value.FillColour, Is.EqualTo((osuTK.Graphics.Color4)Colour4.White));
            Assert.That(play.Health.CaptureHistory(2000)!.Layers.Single().Colour, Is.EqualTo((osuTK.Graphics.Color4)Colour4.White));
        }

        [TestCase(-164.501, HitResult.None)]
        [TestCase(-164.5, HitResult.Miss)]
        [TestCase(-127.501, HitResult.Miss)]
        [TestCase(-127.5, HitResult.Meh)]
        [TestCase(127.5, HitResult.Meh)]
        [TestCase(127.501, HitResult.Miss)]
        public void LazerTapDistinguishesEarlyMissWindowFromLateExpiry(double offset, HitResult expected)
        {
            using var play = new Play([new BmsModManiaProfile { OverallDifficulty = { Value = 8 } }],
                new BmsNote { StartTime = 1000, Column = 1 });
            play.Press(1000 + offset);

            Assert.That(play.Score.JudgementEvents.LastOrDefault()?.Result ?? HitResult.None, Is.EqualTo(expected));
            if (expected != HitResult.None)
                Assert.That(play.Score.JudgementEvents, Has.Count.EqualTo(1));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HoldMissDamageFollowsTheJudgedEndpoints(bool lazer)
        {
            using var play = new Play([new BmsModManiaProfile { DrainRate = { Value = 5 }, UseLazer = { Value = lazer } }],
                new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1 },
                new BmsNote { StartTime = 3000, Column = 1 });
            play.Advance(1500);
            Assert.That(play.Health.CurrentHealth.Value, Is.EqualTo(lazer ? 1 - 6 * 0.00375 : 1).Within(1e-12));
            play.Advance(2500);
            Assert.That(play.Health.CurrentHealth.Value, Is.EqualTo(1 - 6 * 0.0075).Within(1e-12));
            play.Advance(4000);
            Assert.That(play.Health.CurrentHealth.Value, Is.EqualTo(1 - 12 * 0.0075).Within(1e-12));
        }

        [Test]
        public void StableHoldBreakAndLateReleaseSurviveReplay()
        {
            var mod = new BmsModManiaProfile { UseLazer = { Value = false } };
            using var play = new Play([mod], new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1 });
            play.Press(1000);
            play.Release(1500);
            Assert.That(play.Score.Combo.Value, Is.Zero);
            play.Press(1600);
            play.Release(2010);
            play.Advance(3000);
            var score = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = [mod] };
            play.Score.PopulateScore(score);
            Assert.That(BmsLampCalculator.Calculate(score), Is.EqualTo(BmsLamp.Clear));
            Assert.That(score.HitEvents.Single().TimeOffset, Is.EqualTo(10));
            var replay = new Score
            {
                ScoreInfo = score, Replay =
                {
                    Frames =
                    [
                        new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1500),
                        new BmsReplayFrame(1600, BmsAction.Key1), new BmsReplayFrame(2010), new BmsReplayFrame(3000)
                    ]
                }
            };
            var archived = BmsTestReplayArchive.RoundTrip(replay);
            var states = new List<ScoreState>();
            BmsReplaySimulation.Run(play.Beatmap, archived.Replay, archived.ScoreInfo, onScore: states.Add);
            Assert.That(states[^1].Score, Is.EqualTo(score.TotalScore));
            Assert.That(states[^1].Accuracy, Is.EqualTo(score.Accuracy));
            Assert.That(states[^1].MaximumCombo, Is.EqualTo(score.MaxCombo));
        }

        [Test]
        public void StableHoldCanReachItsMaximumTickCombo()
        {
            var mod = new BmsModManiaProfile { UseLazer = { Value = false } };
            using var play = new Play([mod], new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1 });
            play.Press(850);
            play.Release(2001);
            play.Advance(3000);
            Assert.That(play.Score.HighestCombo.Value, Is.EqualTo(play.Score.MaximumCombo));
        }

        [Test]
        public void ProfileSnapshotsIncludeSettings()
        {
            var mod = new BmsModManiaProfile();
            var initial = GameplayProfile.FromMods([mod]);
            mod.UseLazer.Value = false;
            mod.OverallDifficulty.Value = 6;
            var selected = GameplayProfile.FromMods([mod]);
            var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
            beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, StartTime = 1000, Column = 1 });
            Assert.That(new BmsEngineChart(beatmap, initial).Configuration.ProfileId, Is.EqualTo("osu-lazer-mania-standardised"));
            Assert.That(new BmsEngineChart(beatmap, selected).Configuration.ProfileId, Is.EqualTo("osu-stable-mania-score-v1"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ManiaScoreSelectionAcceptsUnreducedScoresAndIgnoresSettings(bool lazer)
        {
            var mod = new BmsModManiaProfile();
            var native = new ScoreInfo { Rank = ScoreRank.A, TotalScore = 2000, Statistics = new Dictionary<HitResult, int> { [HitResult.Perfect] = 1000 } };
            var mania = new ScoreInfo { Rank = ScoreRank.A, TotalScore = 900000, TotalScoreWithoutMods = 900000,
                Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 6 }, DrainRate = { Value = 4 }, UseLazer = { Value = lazer } }],
                Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = 1000, [HitResult.Miss] = 1 } };

            Assert.Multiple(() =>
            {
                Assert.That(BmsLampScoreSelector.SelectBest([native, mania], [mod]), Is.SameAs(native));
                Assert.That(BmsLampScoreSelector.SelectBest([mania], []), Is.Null);
                Assert.That(BmsLampScoreSelector.SelectBest([native, mania], [mod], 1000000), Is.SameAs(mania));
                Assert.That(BmsLampScoreSelector.SelectBest([native], [mod], 1000000), Is.SameAs(native));
                Assert.That(BmsLampScoreSelector.SelectBest([native, mania], [], 2000), Is.SameAs(native));
            });
        }

        [TestCase(false, 1.5, true)]
        [TestCase(true, 1.0, true)]
        [TestCase(true, 1.5, true)]
        [TestCase(true, 1.6, false)]
        public void ManiaReductionMatchingKeepsFasterScores(bool scoreHasMania, double selectedRate, bool expectedMatch)
        {
            var scoreMods = new List<Mod> { new BmsModDoubleTime() };
            if (scoreHasMania)
                scoreMods.Add(new BmsModManiaProfile { OverallDifficulty = { Value = 6 }, DrainRate = { Value = 4 }, UseLazer = { Value = false } });

            var selectedMods = new List<Mod> { new BmsModManiaProfile() };
            if (selectedRate > 1)
                selectedMods.Add(new BmsModDoubleTime { SpeedChange = { Value = selectedRate } });

            var score = new ScoreInfo { Rank = ScoreRank.A, TotalScore = 900000, TotalScoreWithoutMods = 900000, Mods = scoreMods.ToArray(),
                Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = 1000, [HitResult.Miss] = 1 } };

            Assert.Multiple(() =>
            {
                Assert.That(BmsLampScoreSelector.SelectBest([score], selectedMods), Is.SameAs(expectedMatch ? score : null));
                Assert.That(BmsLampScoreSelector.SelectBest([score], selectedMods, 1000000), Is.SameAs(expectedMatch ? score : null));
            });
        }

        [TestCase(HitResult.Perfect, LegacyManiaSkinConfigurationLookups.Hit300g)]
        [TestCase(HitResult.Great, LegacyManiaSkinConfigurationLookups.Hit300)]
        [TestCase(HitResult.Good, LegacyManiaSkinConfigurationLookups.Hit200)]
        [TestCase(HitResult.Ok, LegacyManiaSkinConfigurationLookups.Hit100)]
        [TestCase(HitResult.Meh, LegacyManiaSkinConfigurationLookups.Hit50)]
        [TestCase(HitResult.Miss, LegacyManiaSkinConfigurationLookups.Hit0)]
        public void ManiaPresentationUsesProfileColoursAndImages(HitResult result, LegacyManiaSkinConfigurationLookups image)
        {
            var profile = GameplayProfile.FromMods([new BmsModManiaProfile()]);
            Assert.Multiple(() =>
            {
                Assert.That(profile.JudgementColour(result), Is.EqualTo(new osu.Game.Graphics.OsuColour().ForHitResult(result)));
                Assert.That(profile.JudgementImage(result), Is.EqualTo(image));
                Assert.That(BmsReplayVisualizer.ColourFor(result, profile), Is.EqualTo((Colour4)profile.JudgementColour(result)));
                Assert.That(profile.CreateJudgementDrawable(result), Is.TypeOf<SkinnableDrawable>());
            });
        }

        private static Beatmap createManiaSource() => new()
        {
            BeatmapInfo = new BeatmapInfo
            {
                Ruleset = new RulesetInfo { OnlineID = 3 },
                Difficulty = new BeatmapDifficulty { CircleSize = 7, OverallDifficulty = 6.3f, DrainRate = 4.7f },
            },
            HitObjects = [new ManiaSourceNote { StartTime = 1000, X = 0 }],
        };

        private class ManiaSourceNote : HitObject, IHasXPosition
        {
            public float X { get; set; }
        }

        [TestCase(BmsLongNoteMode.ChargeNote, false)]
        [TestCase(BmsLongNoteMode.ChargeNote, true)]
        [TestCase(BmsLongNoteMode.HellChargeNote, false)]
        [TestCase(BmsLongNoteMode.HellChargeNote, true)]
        public void TestManiaAndInvertUseHoldRulesInEitherOrder(BmsLongNoteMode mode, bool maniaFirst)
        {
            var decoded = new BmsDecodedBeatmap { RawLines = ["#BPM 120", $"#LNMODE {(int)mode}", "#00111:0101"] };
            decoded.CopyFrom(BmsChartParser.Parse(decoded.RawLines));
            var beatmap = (BmsBeatmap)new BmsBeatmapConverter(decoded, new BmsRuleset()).Convert();
            Mod[] mods = maniaFirst ? [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }, new BmsModInvert()] : [new BmsModInvert(), new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
            foreach (var mod in mods.OfType<IApplicableAfterBeatmapConversion>())
                mod.ApplyToBeatmap(beatmap);

            using var score = new BmsScoreProcessor();
            score.Mods.Value = mods;
            score.ApplyBeatmap(beatmap);
            var replay = new BmsModAutoplay().CreateReplayData(beatmap, mods).Replay;
            replay.Frames.Add(new BmsReplayFrame(5000));
            var states = new List<ScoreState>();
            var result = BmsReplaySimulation.Run(beatmap, replay, new ScoreInfo { Mods = mods }, onScore: states.Add);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(beatmap.HitObjects.OfType<BmsLongNote>().Single().Kind, Is.EqualTo(KeyNoteKind.Hold));
                Assert.That(states[^1].Score, Is.EqualTo(score.EngineChart.MaximumScore));
                Assert.That(states[^1].JudgedOpportunities, Is.EqualTo(3));
                Assert.That(result.Judgements.All(judgement => judgement.Result == HitResult.Perfect), Is.True);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestSelectedProfileAutoplayProducesMaximumScore(bool mania)
        {
            using var play = new Play(mania, new BmsLongNote { StartTime = 1000, Duration = 500, Column = 0, Kind = KeyNoteKind.ReleaseHold },
                new BmsNote { StartTime = 1500, Column = 1 },
                new BmsLongNote { StartTime = 2000, Duration = 500, Column = 1, Kind = KeyNoteKind.SustainHold });
            var replay = new BmsModAutoplay().CreateReplayData(play.Beatmap, play.Score.Mods.Value).Replay;
            replay.Frames.Add(new BmsReplayFrame(3000));
            var states = new List<ScoreState>();
            var result = BmsReplaySimulation.Run(play.Beatmap, replay, new ScoreInfo { Mods = play.Score.Mods.Value.ToArray() }, onScore: states.Add);
            Assert.That(replay.Frames.OfType<BmsReplayFrame>().Any(frame => frame.Actions.Contains(BmsAction.ScratchReverse)), Is.EqualTo(!mania));
            Assert.That(states[^1].Score, Is.EqualTo(play.Score.EngineChart.MaximumScore));
            Assert.That(states[^1].JudgedOpportunities, Is.EqualTo(5));
            Assert.That(states[^1].MaximumCombo, Is.EqualTo(5));
            Assert.That(result.Judgements.All(judgement => judgement.Result == HitResult.Perfect), Is.True);
        }

        [Test]
        public void TestManiaTimingStatisticsUseProfileAfterReplayAndAggregation()
        {
            using var play = new Play(true, new BmsNote { StartTime = 1000, Column = 1 },
                new BmsNote { StartTime = 2000, Column = 1 },
                new BmsLongNote { StartTime = 3000, Duration = 1000, Column = 1, Kind = KeyNoteKind.Hold });
            play.Press(1130);
            play.Release(1140);
            play.Press(3010);
            play.Release(4150);
            play.Advance(5000);
            var score = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = play.Score.Mods.Value.ToArray() };
            play.Score.PopulateScore(score);
            var live = BmsResultStatisticsData.Create(score, play.Beatmap);
            var archived = BmsTestReplayArchive.RoundTrip(new Score
            {
                ScoreInfo = score,
                Replay =
                {
                    Frames =
                    [
                        new BmsReplayFrame(0), new BmsReplayFrame(1130, BmsAction.Key1), new BmsReplayFrame(1140),
                        new BmsReplayFrame(3010, BmsAction.Key1), new BmsReplayFrame(4150), new BmsReplayFrame(5000),
                    ],
                },
            });
            var replay = BmsResultStatisticsData.Load(archived.ScoreInfo, play.Beatmap, archived.Replay, CancellationToken.None)!;
            var aggregate = BmsResultStatisticsData.Combine([live, replay]);
            var balance = new BmsResultFastSlow(GameplayProfile.FromMods(score.Mods), score.HitEvents);
            var practice = BmsPracticeAttemptResult.Create(1, score, [], play.Beatmap,
                new BmsPracticeSettings { StartTime = 0, EndTime = 5000 }, play.Score.JudgementEvents);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(score.HitEvents.First().Result, Is.EqualTo(HitResult.Meh));
                Assert.That(live.Offset.Overall.Count, Is.EqualTo(4));
                Assert.That(live.Offset.Overall.AverageOffset, Is.EqualTo((130 + 10 + 150) / 3.0).Within(0.001));
                Assert.That(replay.Offset.Overall.AverageOffset, Is.EqualTo(live.Offset.Overall.AverageOffset));
                Assert.That(aggregate.Offset.Overall.Count, Is.EqualTo(8));
                Assert.That(aggregate.Offset.Overall.AverageOffset, Is.EqualTo(live.Offset.Overall.AverageOffset));
                Assert.That(live.Offset.Overall.BinsByResult[HitResult.Meh][^1], Is.Zero);
                Assert.That(live.Offset.Overall.BinsByResult[HitResult.Miss][^1], Is.EqualTo(1));
                Assert.That(live.Scatter.Overall.Points[0].Kind, Is.EqualTo(ResultTimingKind.Timed));
                Assert.That(live.Scatter.Overall.Points[0].DisplayOffset(150), Is.EqualTo(130));
                Assert.That(live.Scatter.Overall.Points[1].Kind, Is.EqualTo(ResultTimingKind.Untimed));
                Assert.That(live.Timeline.Judgements.Categories.Single(category => category.Label.ToString() == "MISS").Buckets.Sum(), Is.EqualTo(1));
                Assert.That(live.Timeline.Judgements.Categories.Any(category => category.Label.ToString() == "E-POOR"), Is.False);
                Assert.That(live.Timeline.FastSlow.Categories.Sum(category => category.Buckets.Sum()), Is.EqualTo(3));
                Assert.That(balance.FastCount, Is.Zero);
                Assert.That(balance.SlowCount, Is.EqualTo(3));
                Assert.That(practice.Fast, Is.Zero);
                Assert.That(practice.Slow, Is.EqualTo(3));
            }
        }

        [Test]
        public void TestMeterUsesCompiledProfileTailWindowsAndTimingKinds()
        {
            using var play = new Play(true, new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1, Kind = KeyNoteKind.Hold });
            var windows = BmsHitErrorMeter.CreateWindows(play.Score.EngineChart);
            var domain = BmsHitErrorMeter.CreateDomain(windows);
            var profile = play.Score.EngineChart.Profile;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(windows.Single(window => window.Result == HitResult.Perfect).SlowOffset, Is.EqualTo(29.25));
                Assert.That(windows.Single(window => window.Result == HitResult.Meh).SlowOffset, Is.EqualTo(204.75));
                Assert.That(windows.Single(window => window.Result == HitResult.Miss).FastOffset, Is.EqualTo(-260.25));
                Assert.That(windows.Single(window => window.Result == HitResult.Miss).SlowOffset, Is.EqualTo(204.75));
                Assert.That(windows.Any(window => window.Kind == ResultTimingKind.EmptyInput), Is.False);
                Assert.That(domain.SlowOffset, Is.EqualTo(260.25));
                Assert.That(domain.FastOffset, Is.EqualTo(-260.25));
                Assert.That(BmsHitErrorMeter.GetDisplayOffset(130, profile.TimingKindFor(HitResult.Meh), -204.75, 204.75,
                    showUntimed: false, showEmptyInput: false), Is.EqualTo(130));
                Assert.That(BmsHitErrorMeter.GetDisplayOffset(136.5, profile.TimingKindFor(HitResult.Miss), -204.75, 204.75,
                    showUntimed: true, showEmptyInput: false), Is.EqualTo(204.75));
            }
        }

        [TestCase(true, false, -164.5, 127.5)]
        [TestCase(true, true, -246.75, 191.25)]
        [TestCase(false, false, -164.5, 102.999)]
        [TestCase(false, true, -164, 127)]
        public void MeterMissRegionStopsAtThePassiveDeadline(bool lazer, bool hold, double early, double late)
        {
            using var play = new Play([new BmsModManiaProfile { OverallDifficulty = { Value = 8 }, UseLazer = { Value = lazer } }],
                hold ? new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1 } : new BmsNote { StartTime = 1000, Column = 1 });
            var windows = BmsHitErrorMeter.CreateWindows(play.Score.EngineChart);
            var miss = windows.Single(window => window.Result == HitResult.Miss);
            var meh = windows.Single(window => window.Result == HitResult.Meh);

            Assert.That(miss.FastOffset, Is.EqualTo(early));
            Assert.That(miss.SlowOffset, Is.EqualTo(late));
            Assert.That(miss.SlowOffset, Is.LessThanOrEqualTo(meh.SlowOffset));
        }

        [TestCase(false, 15, 210, 15, 210)]
        [TestCase(true, 19.5, 136.5, 29.25, 204.75)]
        public void SelectedProfilePublishesFrameworkHeadAndTailWindows(bool mania, double headPerfect, double headMiss, double tailPerfect, double tailMiss)
        {
            var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 1000, Kind = KeyNoteKind.ReleaseHold };
            using var play = new Play(mania, note);
            var compiledWindows = note.HitWindows;
            note.ApplyDefaults(play.Beatmap.ControlPointInfo, play.Beatmap.Difficulty);
            Assert.That(note.HitWindows, Is.SameAs(compiledWindows));
            Assert.That(note.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(headPerfect));
            Assert.That(note.HitWindows.WindowFor(HitResult.Miss), Is.EqualTo(headMiss));
            var results = new List<BmsJudgementResult>();
            play.Score.NewJudgement += result => results.Add((BmsJudgementResult)result);
            play.Press(1000);
            play.Advance(2100);
            var presentation = play.Score.EngineChart.Profile.LongNotePresentation(note, play.Engine.GetColumn(1).GetNote(note),
                new BmsLongNotePresentationContext(2100, true, play.Score.EngineChart.TimingFor(note)));
            Assert.That(presentation.KeepVisibleAfterHit, Is.True);
            if (mania)
                Assert.That(presentation.KeepAlive, Is.True);
            Assert.That(presentation.LifetimeEnd, Is.GreaterThan(2000 + tailMiss));
            play.Release(2150);
            Assert.That(results, Has.Count.EqualTo(2));
            var tail = results[1];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(tail.HitObject.StartTime, Is.EqualTo(2000));
                Assert.That(((BmsHitObject)tail.HitObject).Column, Is.EqualTo(1));
                Assert.That(((BmsHitObject)tail.HitObject).Beatmap, Is.SameAs(play.Beatmap));
                Assert.That(tail.HitObject.HitWindows.WindowFor(HitResult.Perfect), Is.EqualTo(tailPerfect));
                Assert.That(tail.HitObject.HitWindows.WindowFor(HitResult.Miss), Is.EqualTo(tailMiss));
                Assert.That(tail.TimeOffset, Is.EqualTo(150));
                Assert.That(tail.EventTime, Is.EqualTo(2150));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BrokenLongNoteCannotRestorePinOrBrightness(bool useLazer)
        {
            var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 1000, Kind = KeyNoteKind.Hold };
            using var play = new Play([
                new BmsModManiaProfile
                {
                    OverallDifficulty = { Value = 5 },
                    DrainRate = { Value = 5 },
                    UseLazer = { Value = useLazer },
                }
            ], note);
            var profile = play.Score.EngineChart.Profile;

            play.Press(1000);
            play.Release(1200);

            var broken = play.Engine.GetColumn(1).GetNote(note);
            var brokenBody = broken.Parts.Single(part => part.Part == NoteParts.BODY);
            Assert.That(brokenBody.Active, Is.False);
            var releasedPresentation = profile.LongNotePresentation(note, broken,
                new BmsLongNotePresentationContext(1200, broken.Held, play.Score.EngineChart.TimingFor(note)));
            Assert.That(releasedPresentation.ReleasedStyle, Is.True);
            Assert.That(releasedPresentation.HoldLightActive || releasedPresentation.AnimateBody, Is.False);

            play.Press(1600);

            var recovered = play.Engine.GetColumn(1).GetNote(note);
            var recoveredBody = recovered.Parts.Single(part => part.Part == NoteParts.BODY);
            Assert.That(recoveredBody.Active, Is.True);
            var recoveredPresentation = profile.LongNotePresentation(note, recovered,
                new BmsLongNotePresentationContext(1600, recovered.Held, play.Score.EngineChart.TimingFor(note)));
            Assert.That(recoveredPresentation.HoldingBody, Is.False);
            Assert.That(recoveredPresentation.ReleasedStyle, Is.True);
            Assert.That(recoveredPresentation.HoldLightActive, Is.True);
            Assert.That(recoveredPresentation.AnimateBody, Is.True);

            play.Release(1700);
            play.Press(1800);
            var repeated = play.Engine.GetColumn(1).GetNote(note);
            var repeatedPresentation = profile.LongNotePresentation(note, repeated,
                new BmsLongNotePresentationContext(1800, repeated.Held, play.Score.EngineChart.TimingFor(note)));
            Assert.That(repeatedPresentation.HoldingBody, Is.False);
            Assert.That(repeatedPresentation.ReleasedStyle, Is.True);

            play.Engine.Reset();
            play.Score.ResetReplayState();
            play.Health.ResetReplayState();
            play.Press(1000);
            var restarted = play.Engine.GetColumn(1).GetNote(note);
            var restartedPresentation = profile.LongNotePresentation(note, restarted,
                new BmsLongNotePresentationContext(1000, restarted.Held, play.Score.EngineChart.TimingFor(note)));
            Assert.That(restartedPresentation.HoldingBody, Is.True);
            Assert.That(restartedPresentation.ReleasedStyle, Is.False);
        }

        [TestCase(true, 0, 1999.999, true, true, HitResult.None)]
        [TestCase(true, 0, 2000, true, false, HitResult.None)]
        [TestCase(true, 0, 2000.001, true, false, HitResult.None)]
        [TestCase(true, 50, 1876.499, false, true, HitResult.None)]
        [TestCase(true, 50, 1876.5, true, true, HitResult.None)] // Early Miss on the next note keeps the old tail.
        [TestCase(true, 50, 1913.5, true, false, HitResult.Miss)]
        [TestCase(true, 50, 2000, true, false, HitResult.Miss)]
        [TestCase(true, 50, 2049.999, true, false, HitResult.Miss)]
        [TestCase(true, 50, 2050, true, false, HitResult.Miss)]
        [TestCase(false, 0, 1999.999, false, true, HitResult.None)]
        [TestCase(false, 0, 2000, false, true, HitResult.None)]
        [TestCase(false, 0, 2000.001, false, true, HitResult.None)]
        [TestCase(false, 50, 2050, false, true, HitResult.None)]
        [TestCase(false, 100, 2111.999, false, true, HitResult.None)]
        [TestCase(false, 100, 2112, true, false, HitResult.Miss)]
        public void ManiaEarlyReleaseInputPriority(bool useLazer, double nextOffset, double pressTime, bool nextJudged, bool oldHolding, HitResult expectedTail)
        {
            var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 1000 };
            var next = new BmsNote { Column = 1, StartTime = 2000 + nextOffset };
            using var play = new Play([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, UseLazer = { Value = useLazer } }], note, next);
            play.Press(1000);
            play.Release(1200);
            Assert.That(play.Engine.GetColumn(1).GetNote(note).Completed, Is.False);

            play.Press(pressTime);

            var column = play.Engine.GetColumn(1);
            var old = column.GetNote(note);
            var tail = old.Parts.Single(part => part.Part == NoteParts.TAIL);
            var profile = play.Score.EngineChart.Profile;
            Assert.That(column.GetNote(next).Parts.Single(part => part.Part == NoteParts.TAP).Resolved, Is.EqualTo(nextJudged));
            Assert.That(old.Parts.Single(part => part.Part == NoteParts.BODY).Active, Is.EqualTo(oldHolding));
            Assert.That(profile.HitResultFor(tail.Result), Is.EqualTo(expectedTail));

            if (useLazer && oldHolding)
            {
                play.Release(Math.Max(note.EndTime, pressTime + 1));
                Assert.That(play.Score.Statistics.GetValueOrDefault(HitResult.Meh), Is.EqualTo(1));
            }
        }

        [TestCase(true, 1739.749, false, HitResult.None)]
        [TestCase(true, 1739.75, true, HitResult.Miss)]
        [TestCase(true, 1795.249, true, HitResult.Miss)]
        [TestCase(true, 1795.25, true, HitResult.Meh)]
        [TestCase(true, 1831.25, true, HitResult.Ok)]
        [TestCase(true, 1900, true, HitResult.Good)]
        [TestCase(true, 1970.75, true, HitResult.Perfect)]
        [TestCase(false, 1863.999, false, HitResult.None)]
        [TestCase(false, 1864, true, HitResult.Good)]
        [TestCase(false, 1900, true, HitResult.Great)]
        [TestCase(false, 1946.1, true, HitResult.Great)]
        [TestCase(false, 1962, true, HitResult.Perfect)]
        [TestCase(false, 2000, true, HitResult.Perfect)]
        public void ManiaEarlyTailWindowVisibility(bool useLazer, double releaseTime, bool resolved, HitResult expected)
        {
            var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 1000 };
            using var play = new Play([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, UseLazer = { Value = useLazer } }], note);
            play.Press(1000);
            play.Release(releaseTime);

            var snapshot = play.Engine.GetColumn(1).GetNote(note);
            var tail = snapshot.Parts.Single(part => part.Part == NoteParts.TAIL);
            var profile = play.Score.EngineChart.Profile;
            var presentation = profile.LongNotePresentation(note, snapshot,
                new BmsLongNotePresentationContext(releaseTime, false, play.Score.EngineChart.TimingFor(note)));
            Assert.That(tail.Resolved, Is.EqualTo(resolved));
            Assert.That(profile.HitResultFor(tail.Result), Is.EqualTo(expected));
            Assert.That(presentation.KeepAlive && presentation.KeepVisibleAfterHit, Is.EqualTo(!resolved));
            Assert.That(presentation.Retired, Is.EqualTo(resolved));
            Assert.That(presentation.HoldingBody, Is.False);
        }

        [TestCase(true, 1739.75)]
        [TestCase(true, 1795.25)]
        [TestCase(true, 1900)]
        [TestCase(false, 1864)]
        [TestCase(false, 1962)]
        public void ManiaSettledTailDoesNotBlockTheNextNote(bool useLazer, double releaseTime)
        {
            var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 1000 };
            var next = new BmsNote { Column = 1, StartTime = 2050 };
            using var play = new Play([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, UseLazer = { Value = useLazer } }], note, next);
            play.Press(1000);
            play.Release(releaseTime);
            Assert.That(play.Engine.GetColumn(1).GetNote(note).Completed, Is.True);

            play.Press(2000);

            Assert.That(play.Engine.GetColumn(1).GetNote(next).Parts.Single(part => part.Part == NoteParts.TAP).Resolved, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissedHeadCannotRestorePinOrBrightness(bool useLazer)
        {
            var note = new BmsLongNote { Column = 1, StartTime = 1000, Duration = 2000, Kind = KeyNoteKind.Hold };
            using var play = new Play([
                new BmsModManiaProfile
                {
                    OverallDifficulty = { Value = 5 },
                    DrainRate = { Value = 5 },
                    UseLazer = { Value = useLazer },
                }
            ], note);
            var profile = play.Score.EngineChart.Profile;

            play.Advance(1200);

            play.Press(1500);

            var recovered = play.Engine.GetColumn(1).GetNote(note);
            var recoveredBody = recovered.Parts.Single(part => part.Part == NoteParts.BODY);
            Assert.That(recoveredBody.Active, Is.True);
            var recoveredPresentation = profile.LongNotePresentation(note, recovered,
                new BmsLongNotePresentationContext(1500, recovered.Held, play.Score.EngineChart.TimingFor(note)));
            Assert.That(recoveredPresentation.HoldingBody, Is.False);
            Assert.That(recoveredPresentation.ReleasedStyle, Is.True);
            Assert.That(recoveredPresentation.HoldLightActive, Is.True);
        }

        [TestCase(false, 1, "bms-ruleset-v1")]
        [TestCase(true, 2, "osu-lazer-mania-standardised")]
        public void TestOneModSelectsWholeProfile(bool mania, int scoringOpportunities, string profileId)
        {
            using var play = new Play(mania, new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1, Kind = KeyNoteKind.Hold });
            Assert.That(play.Score.EngineChart.Configuration.ProfileId, Is.EqualTo(profileId));
            Assert.That(play.Score.EngineChart.Chart.ScorePlan.Opportunities, Has.Length.EqualTo(scoringOpportunities));
            play.Press(1000);
            play.Release(2000);
            play.Advance(3000);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(play.Score.TotalScore.Value, Is.EqualTo(1_000_000));
                Assert.That(play.Score.Accuracy.Value, Is.EqualTo(1));
                Assert.That(play.Score.Combo.Value, Is.EqualTo(scoringOpportunities));
                Assert.That(play.Score.ScoringJudgementEventCount, Is.EqualTo(scoringOpportunities));
                Assert.That(play.Health.CurrentHealth.Layers.IsEmpty, Is.EqualTo(mania));
                Assert.That(play.Health.Outcome, Is.EqualTo(SessionOutcome.Cleared));
                Assert.That(play.Health.HasPassedAtEnd(), Is.True);
            }
        }

        [TestCase(false, SessionOutcome.Failed)]
        [TestCase(true, SessionOutcome.Cleared)]
        public void TestClearUsesSelectedHealthRules(bool mania, SessionOutcome outcome)
        {
            using var play = new Play(mania, new BmsNote { StartTime = 1000, Column = 1 });
            play.Advance(2000);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(play.Score.ScoringJudgementEventCount, Is.EqualTo(1));
                Assert.That(play.Score.TotalScore.Value, Is.Zero);
                Assert.That(play.Score.Accuracy.Value, Is.Zero);
                Assert.That(play.Health.Outcome, Is.EqualTo(outcome));
                Assert.That(play.Health.HasPassedAtEnd(), Is.EqualTo(outcome == SessionOutcome.Cleared));
                Assert.That(play.Score.Statistics.GetValueOrDefault(mania ? HitResult.Miss : HitResult.Meh), Is.EqualTo(1));
            }
        }

        [Test]
        public void TestManiaBodyBreakUpdatesComboAndRebuildRestoresProfile()
        {
            using var play = new Play(true, new BmsLongNote { StartTime = 1000, Duration = 1000, Column = 1, Kind = KeyNoteKind.SustainHold });
            play.Press(1000);
            Assert.That(play.Score.Combo.Value, Is.EqualTo(1));
            play.Release(1500);
            Assert.That(play.Score.Combo.Value, Is.Zero);
            Assert.That(play.Score.ScoringJudgementEventCount, Is.EqualTo(1));
            play.Press(1600);
            play.Release(2000);
            play.Advance(3000);
            var score = play.Score.TotalScore.Value;
            var accuracy = play.Score.Accuracy.Value;
            var health = play.Health.CurrentHealth;
            var saved = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = play.Score.Mods.Value.ToArray() };
            play.Score.PopulateScore(saved);
            Assert.That(BmsLampCalculator.Calculate(saved), Is.EqualTo(BmsLamp.Clear));
            Assert.That(play.Score.Statistics.GetValueOrDefault(HitResult.Meh), Is.EqualTo(1));

            play.Engine.Reset();
            play.Score.ResetReplayState();
            play.Health.ResetReplayState();
            play.Press(1000);
            play.Release(1500);
            play.Press(1600);
            play.Release(2000);
            play.Advance(3000);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(play.Score.TotalScore.Value, Is.EqualTo(score));
                Assert.That(play.Score.Accuracy.Value, Is.EqualTo(accuracy));
                Assert.That(play.Health.CurrentHealth, Is.EqualTo(health));
                Assert.That(play.Health.Outcome, Is.EqualTo(SessionOutcome.Cleared));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestLiveStoredAndReplayScoresUseSelectedProfile(bool mania)
        {
            using var play = new Play(mania, new BmsNote { StartTime = 1000, Column = 1 },
                new BmsNote { StartTime = 2000, Column = 1 }, new BmsNote { StartTime = 3000, Column = 1 });
            play.Press(1000);
            var first = play.Score.EngineScore.Score;
            play.Release(1100);
            play.Press(2060);
            var second = play.Score.EngineScore.Score;
            play.Release(2100);
            play.Advance(4000);
            var source = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = play.Score.Mods.Value.ToArray() };
            play.Score.PopulateScore(source);
            var archived = BmsTestReplayArchive.RoundTrip(new Score
            {
                ScoreInfo = source,
                Replay =
                {
                    Frames =
                    [
                        new BmsReplayFrame(0), new BmsReplayFrame(1000, BmsAction.Key1), new BmsReplayFrame(1100),
                        new BmsReplayFrame(2060, BmsAction.Key1), new BmsReplayFrame(2100), new BmsReplayFrame(4000),
                    ],
                },
            });
            var states = new List<ScoreState>();
            BmsReplaySimulation.Run(play.Beatmap, archived.Replay, archived.ScoreInfo, onScore: states.Add);
            var profile = GameplayProfile.FromMods(archived.ScoreInfo.Mods);
            var value = profile.StoredScore(archived.ScoreInfo)!.Value;
            var aggregate = BmsScoreAggregation.CreateScore([source, source], play.Beatmap.BeatmapInfo, source.Mods, true);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(value, Is.EqualTo(play.Score.EngineScore.Score));
                Assert.That(states[^1].Score, Is.EqualTo(value));
                Assert.That(BmsScoreGraph.ScoreAtProgress(value, states, 1, 3), Is.EqualTo(first));
                Assert.That(BmsScoreGraph.ScoreAtProgress(value, states, 2, 3), Is.EqualTo(second));
                Assert.That(BmsScoreGraph.ScoreAtProgress(value, states, 0, 3), Is.Zero);
                Assert.That(profile.StoredScore(aggregate), Is.EqualTo(2 * value));
                Assert.That(profile.StoredMaximumScore(aggregate), mania ? Is.Null : Is.EqualTo(12));
                Assert.That(profile.JudgementLabel(HitResult.Miss).ToString(), Is.EqualTo(mania ? "MISS" : "E-POOR"));
                Assert.That(profile.RankLabel(ScoreRank.S).ToString(), Is.EqualTo(mania ? "S" : "AAA"));

                if (mania)
                {
                    Assert.That(value, Is.GreaterThan(10000));
                    Assert.That(BmsLampCalculator.Calculate(archived.ScoreInfo), Is.EqualTo(BmsLamp.Clear));
                }
            }
        }

        [TestCase(0, BmsLamp.Max)]
        [TestCase(30, BmsLamp.Perfect)]
        [TestCase(60, BmsLamp.FullCombo)]
        [TestCase(100, BmsLamp.FullCombo)]
        [TestCase(130, BmsLamp.FullCombo)]
        [TestCase(200, BmsLamp.Clear)]
        public void TestManiaLampUsesAllConsumingJudgements(int offset, BmsLamp expected)
        {
            using var play = new Play(true, new BmsNote { StartTime = 1000, Column = 1 });
            play.Press(1000 + offset);
            play.Advance(2000);
            var source = new ScoreInfo { Ruleset = new BmsRuleset().RulesetInfo, Mods = play.Score.Mods.Value.ToArray() };
            play.Score.PopulateScore(source);
            Assert.That(BmsLampCalculator.Calculate(source), Is.EqualTo(expected));
        }

        [TestCase(ScoreRank.C)]
        [TestCase(ScoreRank.B)]
        [TestCase(ScoreRank.A)]
        [TestCase(ScoreRank.S)]
        [TestCase(ScoreRank.X)]
        public void ScoreRankDoesNotHaveALinearScoreThreshold(ScoreRank rank)
        {
            Assert.That(GameplayProfile.FromMods([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]).ScoreForRank(rank, 1_000_000), Is.Null);
        }

        [TestCase(HitResult.Meh)]
        [TestCase(HitResult.Ok)]
        [TestCase(HitResult.Good)]
        [TestCase(HitResult.Great)]
        [TestCase(HitResult.Perfect)]
        [TestCase(HitResult.Miss)]
        public void HitEffectsUseManiaResults(HitResult result)
        {
            Assert.That(GameplayProfile.FromMods([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]).ShowHitEffect(result), Is.EqualTo(result != HitResult.Miss));
        }

        private sealed class Play : IDisposable
        {
            internal BmsScoreProcessor Score { get; } = new();
            internal BmsHealthProcessor Health { get; } = new();
            internal BmsJudgementEngine Engine { get; }
            internal BmsBeatmap Beatmap { get; }
            private readonly ManualClock clock = new();
            private readonly FramedClock frameClock;

            internal Play(bool mania, params BmsHitObject[] notes)
                : this(mania ? [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }] : [], notes)
            {
            }

            internal Play(Mod[] mods, params BmsHitObject[] notes)
            {
                var beatmap = Beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
                beatmap.HitObjects.AddRange(notes);
                foreach (var note in notes)
                    note.Beatmap = beatmap;
                foreach (var mod in mods.OfType<IApplicableAfterBeatmapConversion>())
                    mod.ApplyToBeatmap(beatmap);
                frameClock = new FramedClock(clock);
                Score.Clock = Health.Clock = frameClock;
                Score.Mods.Value = mods;
                Health.Mods = mods;
                Score.ApplyBeatmap(beatmap);
                Health.ApplyBeatmap(beatmap);
                foreach (var mod in mods.OfType<IApplicableToHealthProcessor>())
                    mod.ApplyToHealthProcessor(Health);
                var output = new BmsReplaySimulation.SimulationOutput(Score, Health, _ => { });
                Engine = new BmsJudgementEngine(Score.EngineChart,
                    Enumerable.Range(0, 8).Select(column => new BmsJudgementColumnDefinition(column, output)).ToArray(), Health.CreateEngineHealth,
                    session =>
                    {
                        Score.ApplyEngineState(session);
                        Health.ApplyEngineState(session);
                    });
            }

            internal void Press(double time)
            {
                Engine.QueueInput(time, 1, true, false);
                Advance(time);
            }

            internal void Release(double time)
            {
                Engine.QueueInput(time, 1, false, false);
                Advance(time);
            }

            internal void Advance(double time)
            {
                clock.CurrentTime = time;
                frameClock.ProcessFrame();
                Engine.ProcessFrame(new BmsFrameContext(time, 1), 0);
            }

            public void Dispose()
            {
                Score.Dispose();
                Health.Dispose();
            }
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Audio
{
    public partial class BmsKeySoundPolicyTest
    {
        [TestCase(0)]
        [TestCase(.5)]
        [Category("ManiaProfile")]
        public void StableHeadSampleRangeCoversRecoveryUntilItsDeadline(double fractionalEnd)
        {
            var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, TotalColumns = 8 };
            var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 9000 + fractionalEnd, SampleKey = 1 };
            beatmap.HitObjects.Add(hold);
            var mod = new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 }, UseLazer = { Value = false } };
            var compiled = new BmsEngineChart(beatmap, GameplayProfile.FromMods([mod]));
            var feedback = new List<string>();
            var output = new BmsRecordingJudgementOutput { Feedback = feedback.Add };
            var engine = new BmsJudgementEngine(compiled, [new(1, output)]);
            var deadline = hold.EndTime + 112;
            var lastPress = deadline - .001;
            engine.QueueInput(lastPress, 1, true, false);
            engine.ProcessFrame(new BmsFrameContext(lastPress, 1), 0);
            Assert.That(feedback, Does.Contain("keysound:1:100"));
            var usage = BmsGameplayAudioController.GetSampleUsages(beatmap, compiled).Single();
            Assert.That(usage.LatestTriggerTime, Is.GreaterThanOrEqualTo(lastPress));
            engine.Reset();
            output.Reset();
            feedback.Clear();
            engine.QueueInput(deadline, 1, true, false);
            engine.ProcessFrame(new BmsFrameContext(deadline, 1), 0);
            Assert.That(feedback, Does.Not.Contain("keysound:1:100"));
        }

        [TestCase(false, 835, 3210, 3210)]
        [TestCase(true, 826.5, 3136.5, 3204.75)]
        [Category("ManiaProfile")]
        public void SelectedProfileControlsSamplePreparationAndExpiry(bool mania, double nextReady, double headExpiry, double tailExpiry)
        {
            var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
            var tap = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000, SampleKey = 1 };
            var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 2000, Duration = 1000, Kind = KeyNoteKind.ReleaseHold, SampleKey = 2, TailSampleKey = 3 };
            beatmap.HitObjects.AddRange([tap, hold]);
            using var score = new BmsScoreProcessor();

            if (mania)
            {
                var mod = new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } };
                mod.ApplyToBeatmap(beatmap);
                score.Mods.Value = [mod];
            }

            score.ApplyBeatmap(beatmap);
            var compiled = score.EngineChart;
            var usages = BmsGameplayAudioController.GetSampleUsages(beatmap, compiled).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(usages.Single(usage => usage.SampleKey == 2).EarliestTriggerTime, Is.EqualTo(nextReady));
                Assert.That(usages.Single(usage => usage.SampleKey == 2).LatestTriggerTime, Is.EqualTo(headExpiry));
                Assert.That(usages.Single(usage => usage.SampleKey == 3).EarliestTriggerTime, Is.EqualTo(nextReady + 1000));
                Assert.That(usages.Single(usage => usage.SampleKey == 3).LatestTriggerTime, Is.EqualTo(tailExpiry));
            });

            var session = new JudgementSession(compiled.Chart, compiled.Configuration);
            session.AdvanceTo(Time.FromMilliseconds(hold.StartTime + compiled.TimingFor(hold).HeadDeadline / 1000.0 + 0.001));
            Assert.That(session.GetNote(compiled.Ids[hold]).Parts.Single(part => part.Part == NoteParts.HEAD).Resolved, Is.True);
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Bga
{
    public partial class BmsBgaPoorLayerTest
    {
        [Test]
        [Category("ManiaProfile")]
        public void TestManiaMehTriggersPlayAnimation()
        {
            scoreProcessor.Mods.Value = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
            scoreProcessor.ApplyBeatmap(beatmap);
            var bytes = BmsonParserTest.Chart(events: """
                                                      ,"bga":{"bga_header":[{"id":1,"name":"layer.png"}],
                                                      "layer_events":[{"y":0,"id":1,"condition":"play"}]}
                                                      """);
            setTimeline(BmsonParser.Parse(bytes).Bga);
            advanceTo(0);
            advanceTo(1130);
            var compiled = scoreProcessor.EngineChart;
            var session = new JudgementSession(compiled.Chart, compiled.Configuration);
            var engineResult = session.AdvanceTo(1130000, [new InputEvent(1130000, 1, true)]).Transitions.Single().Judgements.Single();
            var note = beatmap.HitObjects[0];
            var result = new BmsJudgementResult(note, note.CreateJudgement())
            {
                Type = compiled.Profile.HitResultFor(engineResult.Event.Result),
                EngineResult = engineResult,
            };
            result.SetTiming(1130, 1);
            scoreProcessor.ApplyResult(result);

            Assert.That(result.Type, Is.EqualTo(HitResult.Meh));
            var animation = getDisplayField<Dictionary<BmsBgaLayer, BmsBgaAnimationPlayback>>("animations").Values.Single();
            Assert.That(animation.GetFrame(1130).Key, Is.EqualTo(1));
            Assert.That(scoreProcessor.Combo.Value, Is.EqualTo(1));
            Assert.That(layer(BmsBgaLayer.Poor).Alpha, Is.Zero);
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal
{
    public partial class BmsRulesetTest
    {
        [Test]
        [Category("ManiaProfile")]
        public void TestManiaProfileAttributesUseItsRulesWithoutChangingImportedMetadata()
        {
            var beatmapInfo = new BeatmapInfo();
            new BmsDifficultyInfo { Rank = 0, Total = 800, LayoutVariant = BmsLayoutVariant.Bme7K, LockedLongNoteMode = BmsLongNoteMode.HellChargeNote }
                .WriteToOsuDifficulty(beatmapInfo);
            Mod[] mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];

            var attributes = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, mods).ToArray();
            var adjusted = ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, mods);
            var original = BmsDifficultyInfo.FromOsuDifficulty(beatmapInfo.Difficulty);

            Assert.Multiple(() =>
            {
                Assert.That(attributes.Select(attribute => attribute.Acronym), Is.EqualTo((string[])["OD", "HP"]));
                Assert.That(attributes.Select(attribute => attribute.AdjustedValue), Is.EqualTo((float[])[5, 5]));
                Assert.That(attributes.All(attribute => attribute.Description.HasValue), Is.True);
                Assert.That(attributes[0].AdditionalMetrics, Has.Length.EqualTo(6));
                Assert.That(adjusted.OverallDifficulty, Is.EqualTo(5));
                Assert.That(adjusted.DrainRate, Is.EqualTo(5));
                Assert.That(original.Rank, Is.Zero);
                Assert.That(original.Total, Is.EqualTo(800));
                Assert.That(original.LockedLongNoteMode, Is.EqualTo(BmsLongNoteMode.HellChargeNote));
            });
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Course
{
    public partial class BmsCourseSessionTest
    {
        [TestCase(null)]
        [TestCase("ln")]
        [TestCase("gauge_lr2")]
        [Category("ManiaProfile")]
        public void TestCourseDisallowsPlayerProfileModifiers(string? constraint)
        {
            string[] constraints = constraint == null ? [] : [constraint];
            var forbidden = BmsCourseSession.ResolveForbiddenModTypes(constraints).ToArray();

            foreach (var selected in new Mod[] { new BmsModLr2(), new BmsModNoGood(), new BmsModNoGreat(), new BmsModNoBad(), new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } } })
            {
                Assert.That(forbidden.Any(type => type.IsInstanceOfType(selected)), Is.True, selected.Acronym);
                var gaugeType = BmsCourseSession.ResolveCourseGaugeType([selected]);
                var mods = BmsCourseSession.CreateCourseMods([selected], gaugeType, constraints);
                Assert.That(mods.OfType<IApplicableToGameplayProfile>(), Is.Empty, selected.Acronym);
                Assert.That(mods, Has.One.TypeOf<BmsModClassGauge>());
            }
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Difficulty
{
    public partial class BmsStarRatingProcessorTest
    {
        [Test]
        [Category("ManiaProfile")]
        public void ManiaRatingUsesBaseWindowsAndIncludesHoldTicks([Values] bool useLazer, [Values] bool timed, [Values] bool holdFirst)
        {
            var beatmap = createSimpleBeatmap();
            BmsHitObject hold = new BmsLongNote { Column = holdFirst ? 1 : 2, StartTime = holdFirst ? 1000 : 2500, Duration = 1000 };
            var tap = new BmsNote { Column = holdFirst ? 2 : 1, StartTime = holdFirst ? 2500 : 1000 };
            beatmap.HitObjects = holdFirst ? [hold, tap] : [tap, hold];
            foreach (var note in beatmap.HitObjects)
                note.Beatmap = beatmap;
            Mod[] mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 }, UseLazer = { Value = useLazer } }];
            var working = new TestWorkingBeatmap(beatmap);
            var calculator = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, working);
            var attributes = timed ? calculator.CalculateTimed(mods).Select(result => result.Attributes).ToArray() : [calculator.Calculate(mods)];
            var maximumCombo = useLazer ? 3 : 13;
            var firstCombo = holdFirst ? maximumCombo - 1 : 1;
            using var score = new BmsScoreProcessor();
            score.Mods.Value = mods;
            score.ApplyBeatmap(working.GetPlayableBeatmap(new BmsRuleset().RulesetInfo, mods));
            Assert.Multiple(() =>
            {
                Assert.That(attributes.Select(result => result.MaxCombo), Is.EqualTo(timed ? new[] { firstCombo, maximumCombo } : [maximumCombo]));
                Assert.That(attributes[^1].MaxCombo, Is.EqualTo(score.MaximumCombo));
                Assert.That(calculator.StarRatingProcessor.HitLeniencyX, Is.EqualTo(computeExpectedHitLeniency(49.5)).Within(1e-12));
            });
        }

        [Test]
        [Category("ManiaProfile")]
        public void SelectedProfileControlsRatingAndScoringOpportunities([Values] bool timed)
        {
            var beatmap = createSimpleBeatmap();
            beatmap.HitObjects =
            [
                new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 0 },
                new BmsLongNote { Beatmap = beatmap, Column = 2, StartTime = 500, Duration = 250, Kind = KeyNoteKind.ReleaseHold },
                new BmsLongNote { Beatmap = beatmap, Column = 3, StartTime = 1000, Duration = 250 },
                new BmsLandmine { Beatmap = beatmap, Column = 4, StartTime = 1500, LandmineDamagePercent = 10 },
                new BmsNote { Beatmap = beatmap, Column = 5, StartTime = 2000 },
            ];
            var calculator = new BmsDifficultyCalculator(new BmsRuleset().RulesetInfo, new TestWorkingBeatmap(beatmap));

            foreach (var mania in new[] { false, true, false })
            {
                Mod[] mods = mania ? [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }] : [];
                var attributes = timed
                    ? calculator.CalculateTimed(mods).Select(result => result.Attributes).ToArray()
                    : [calculator.Calculate(mods)];
                int[] expectedCombos = timed ? mania ? [1, 3, 5, 6] : [1, 3, 4, 4, 5] : [mania ? 6 : 5];
                Assert.That(attributes.Select(result => result.MaxCombo), Is.EqualTo(expectedCombos));
                Assert.That(calculator.StarRatingProcessor.HitLeniencyX, Is.EqualTo(computeExpectedHitLeniency(mania ? 49.5 : 45)).Within(1e-12));
            }

            Assert.That(((BmsLongNote)beatmap.HitObjects[1]).Kind, Is.EqualTo(KeyNoteKind.ReleaseHold));
            Assert.That(beatmap.HitObjects.OfType<BmsLandmine>().Count(), Is.EqualTo(1));
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay
{
    public partial class BmsGameplayVirtualisationTest
    {
        [TestCase(false, 500, 1310, 3310)]
        [TestCase(true, 826.5, 1236.5, 3304.75)]
        [Category("ManiaProfile")]
        public void SelectedProfileControlsLifetimeBeforePlayfieldLoad(bool mania, double start, double tapEnd, double holdEnd)
        {
            var tap = new BmsNote { StartTime = 1000, Column = 1 };
            var hold = new BmsLongNote { StartTime = 2000, Duration = 1000, Column = 1, Kind = KeyNoteKind.ReleaseHold };
            var beatmap = attachBeatmap(new BmsBeatmap { Rank = 2, TotalColumns = BmsLayout.BME7_KEY_COLUMNS, LayoutVariant = BmsLayoutVariant.Bme7K, HitObjects = [tap, hold] });
            using var score = new BmsScoreProcessor();

            if (mania)
            {
                var mod = new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } };
                mod.ApplyToBeatmap(beatmap);
                score.Mods.Value = [mod];
            }

            score.ApplyBeatmap(beatmap);
            var compiled = score.EngineChart;
            var playfield = new BmsPlayfield(beatmap, () => compiled);
            playfield.ScrollController.SetConfiguredScrollSpeed(1000);
            playfield.ScrollController.ConstantScrollActive = true;
            playfield.Add(tap);
            playfield.Add(hold);
            playfield.RefreshAllLifetimes();
            var entries = playfield.Stage.Columns[1].HitObjectContainer.Entries.ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(playfield.IsLoaded, Is.False);
                Assert.That(entries.Single(entry => entry.HitObject == tap).LifetimeStart, Is.EqualTo(start));
                Assert.That(entries.Single(entry => entry.HitObject == tap).LifetimeEnd, Is.EqualTo(tapEnd));
                Assert.That(entries.Single(entry => entry.HitObject == hold).LifetimeEnd, Is.EqualTo(holdEnd));
                Assert.That(playfield.CreateNoteLifetimeEntry(tap).GetPracticeVisibilityStart(), Is.EqualTo(start));
            });
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Judgement
{
    public partial class BmsJudgementProfileCacheTest
    {
        [Test]
        [Category("ManiaProfile")]
        public void EquivalentModSelectionsPreserveTheActiveHealthModel()
        {
            var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Popn9K, TotalColumns = 9 };
            beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 });
            using var health = new BmsHealthProcessor();
            health.ApplyBeatmap(beatmap);
            health.Mods = [new BmsModLr2(), new BmsModNoBad()];
            var model = health.Model;
            health.Mods = [new BmsModNoBad(), new BmsModLr2()];
            Assert.That(health.Model, Is.SameAs(model));
            health.Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
            Assert.That(health.Model, Is.Not.SameAs(model));
            health.Mods = [];
            health.CreateEngineHealth(new BmsEngineChart(beatmap));
            Assert.That(health.Health.Value, Is.EqualTo(.3), "Returning to BMS retains the chart's PMS gauge family.");
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay.Scoring
{
    public partial class BmsScoreProcessorTest
    {
        [Test]
        [Category("ManiaProfile")]
        public void TestManiaProfileCompletionIncludesPracticeEnd()
        {
            Mod[] mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
            var practice = BmsPracticeBeatmap.Create(createThreeNoteBeatmap(),
                new BmsPracticeSettings { StartTime = 0, EndTime = 2500 });
            using var play = new CompletionPlay(practice, selectedMods: mods);

            foreach (var note in practice.HitObjects)
            {
                play.Input(note.StartTime, note.Column, true);
                play.Input(note.StartTime + 1, note.Column, false);
            }

            play.Advance(2499);
            Assert.That(play.Processor.HasCompleted.Value, Is.False);
            play.Advance(2500);
            Assert.That(play.Processor.HasCompleted.Value, Is.True);
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Practice
{
    public partial class BmsPracticeBeatmapTest
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("ManiaProfile")]
        public void PracticeWorkingBeatmapKeepsFullGaugeCountIndependentOfScoreProfile(bool mania)
        {
            var full = createBeatmap(new BmsLongNote { StartTime = 1000, Duration = 500, Column = 1 },
                new BmsNote { StartTime = 2000, Column = 1 });
            var session = new BmsPracticeSession(new TestWorkingBeatmap(full),
                new BmsPracticeSettings { StartTime = 2000, EndTime = 2001 }, mania ? [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }] : []);
            var working = new BmsPracticeWorkingBeatmap(session, null!);
            var cachedFull = session.GetFullBeatmap();
            var cachedWindows = cachedFull.HitObjects.Select(note => note.HitWindows).ToArray();
            var attempt = (BmsBeatmap)working.GetPlayableBeatmap(new BmsRuleset().RulesetInfo, session.Mods);
            using var score = new BmsScoreProcessor();
            score.Mods.Value = session.Mods;
            score.ApplyBeatmap(attempt);
            Assert.That(attempt.PracticeFullGaugeOpportunities, Is.EqualTo(2));
            Assert.That(score.EngineChart.Chart.ScorePlan.Opportunities, Has.Length.EqualTo(1));
            Assert.That(full.PracticeFullGaugeOpportunities, Is.Null);
            Assert.That(full.HitObjects, Has.Count.EqualTo(2));
            Assert.That(cachedFull.HitObjects.Select(note => note.HitWindows), Is.EqualTo(cachedWindows));
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SongSelect
{
    public partial class BmsScoreSelectorGraphTest
    {
        [Test]
        [Category("ManiaProfile")]
        public void TestManiaPersonalBestUsesStoredNonlinearScore()
        {
            var higherEx = score(600_000, 10, 0);
            higherEx.TotalScoreWithoutMods = 600_000;
            higherEx.Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
            var higherNative = score(700_000, 9, 1);
            higherNative.TotalScoreWithoutMods = 700_000;
            higherNative.Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
            Assert.That(BmsLampScoreSelector.SelectBest([higherEx, higherNative], [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }], 1_000_000), Is.SameAs(higherNative));
        }
    }
}

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize
{
    public partial class TestSceneBmsAutoScratchInput
    {
        [Test]
        [Category("ManiaProfile")]
        public void ManiaProfileAutoScratchUsesCompiledTailAndRecordsItsInputs()
        {
            load(BmsLongNoteMode.HellChargeNote, additionalMods: [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]);
            seek(3000);
            seek(3200);
            AddAssert("Mania hold is held", () => scratch.PressedDirections, () => Is.EqualTo(1));
            seek(3800);
            seek(3810);
            AddStep("Mania head and tail score through generated inputs", () =>
            {
                Assert.That(observations().Select(observation => observation.ExpectedTime), Is.EqualTo((double[])[3000, 3200, 3800]));
                Assert.That(observations().All(observation => observation.Result == HitResult.Perfect), Is.True);
                Assert.That(scratch.PressedDirections, Is.Zero);
                Assert.That(Player.Score.Replay.Frames.OfType<BmsReplayFrame>().Any(frame => frame.Actions.Contains(BmsAction.ScratchReverse)), Is.False);
            });
        }
    }

    public partial class TestSceneBmsCourseSelect
    {
        [TestCase(null)]
        [TestCase("no_good")]
        [TestCase("no_great")]
        [Category("ManiaProfile")]
        public void TestCourseProfileModsAreDisabledAndRestored(string? constraint)
        {
            Mod original = constraint == "no_good" ? new BmsModNoGreat() : new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } };
            TestParentScreen parentScreen = null!;
            AddStep("load parent screen", () => Stack.Push(parentScreen = new TestParentScreen()));
            AddUntilStep("parent screen loaded", () => Stack.CurrentScreen == parentScreen && parentScreen.IsLoaded);
            AddStep("configure course", () =>
            {
                var bmsRuleset = rulesets.AvailableRulesets.Single(ruleset => ruleset.ShortName == Constant.SHORT_NAME);
                var imported = beatmaps.Import(createBeatmapSet(bmsRuleset));
                Assert.That(imported, Is.Not.Null);
                var beatmap = imported!.Value.Beatmaps.First();

                BmsRulesetRuntime.CourseCatalog.Replace(
                [
                    new BmsCourseDefinition(
                        "constraint-test",
                        "Constraint Table",
                        "Course",
                        [new BmsCourseStage("Stage", "1", BeatmapHash: beatmap.Hash)],
                        constraint == null ? [] : [constraint]),
                ]);
            });
            AddStep("load real song select", () => parentScreen.Push(songSelect = new BmsSoloSongSelect()));
            AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
            AddUntilStep("wait for filtering", () => !carousel.IsFiltering);
            AddStep("select single-song profile", () => songSelect.Mods.Value = [original]);
            AddStep("show course mode", () => controller.ToggleMode());
            AddUntilStep("course song select loaded", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                            && current != songSelect && current.IsLoaded
                                                            && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == true);
            AddStep("capture course song select", () => songSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
            AddUntilStep("course carousel filtered", () => controller.CourseCarousel.GetCarouselItems() != null
                                                           && !controller.CourseCarousel.IsFiltering);
            AddAssert("course owns judgement modifiers", hasCourseJudgement);

            foreach (var mod in new Mod[] { new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }, new BmsModLr2(), new BmsModNoBad(), new BmsModNoGood(), new BmsModNoGreat() })
            {
                AddAssert($"{mod.Acronym} disabled in mod overlay", () => !songSelect.ModSelectOverlay.IsValidMod(mod));
                AddStep($"apply {mod.Acronym} through selection", () => songSelect.Mods.Value = [mod, new BmsModMirror()]);
                AddAssert("course judgement remains authoritative", hasCourseJudgement);
                AddAssert("allowed mirror retained", () => songSelect.Mods.Value.Any(selected => selected is BmsModMirror));
            }

            AddStep("clear course mods", () => songSelect.Mods.Value = []);
            AddAssert("required judgement survives clearing", hasCourseJudgement);
            AddStep("filter out all courses", () => controller.SearchTerm.Value = "no-matching-course");
            AddUntilStep("no course selected", () => !controller.CourseCarousel.IsFiltering && controller.SelectedCourse == null);
            AddAssert("profile remains disabled without a course", () => !songSelect.ModSelectOverlay.IsValidMod(original));
            AddStep("apply profile without a course", () => songSelect.Mods.Value = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]);
            AddAssert("empty course selection still forbids profile changes", () => !songSelect.Mods.Value.OfType<IApplicableToGameplayProfile>().Any());
            AddStep("return to single songs", () => controller.ToggleMode());
            AddUntilStep("single song select restored", () => Stack.CurrentScreen is BmsSoloSongSelect current
                                                              && current != songSelect && current.IsLoaded
                                                              && current.ChildrenOfType<BmsCourseSongSelectController>().SingleOrDefault()?.IsCourseMode == false);
            AddStep("capture restored song select", () => songSelect = (BmsSoloSongSelect)Stack.CurrentScreen);
            AddUntilStep("restored carousel presented", () => songSelect.CarouselItemsPresented && !songSelect.IsFiltering);
            AddAssert("original profile selection restored", () => songSelect.Mods.Value.Select(mod => mod.GetType()),
                () => Is.EqualTo((Type[])[original.GetType()]));
            AddAssert("profile mod enabled for single songs", () => songSelect.ModSelectOverlay.IsValidMod(original));

            bool hasCourseJudgement()
            {
                var modifiers = songSelect.Mods.Value.OfType<IApplicableToGameplayProfile>().ToArray();
                return constraint switch
                {
                    "no_good" => modifiers is [BmsModNoGood],
                    "no_great" => modifiers is [BmsModNoGreat],
                    _ => modifiers.Length == 0,
                };
            }
        }
    }

    public partial class TestSceneBmsLandmineKeyboardInput
    {
        [Test]
        [Category("ManiaProfile")]
        public void LiveRewindRestoresTheSelectedProfile([Values] bool mania, [Values] bool explicitSeek)
        {
            var snapshots = new Dictionary<double, string>();
            AddStep("load live profile", () => LoadPlayer(mania ? [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }] : [new BmsModChargeNote()]));
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddStep("attach keyboard", () =>
            {
                Player.DrawableRuleset.SetRecordTarget(Player.Score);
                typeof(GameplayClockContainer).GetMethod("StopGameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(Player.GameplayClockContainer, null);
                inputManager = Player.DrawableRuleset.ChildrenOfType<BmsInputManager>().Single(i => i.ChildrenOfType<BmsColumn>().Any());
                inputManager.UseParentInput = false;
                var keyboard = new ScheduledKeyboard(() => Player.DrawableRuleset.FrameStableClock.CurrentTime,
                [
                    (3000, new KeyboardKeyInput([new(Key.Z, true)])), (3300, new KeyboardKeyInput([new(Key.Z, false)])),
                    (3500, new KeyboardKeyInput([new(Key.Z, true)])), (5000, new KeyboardKeyInput([new(Key.Z, false)])),
                ]);
                typeof(CustomInputManager).GetMethod("AddHandler", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(inputManager, [keyboard]);
            });

            foreach (var time in new[] { 0d, 3000, 3200, 3300, 3500, 4000, 5000, 5500 })
            {
                moveTo(time);
                AddStep("capture live state", () => snapshots[time] = state());
            }

            AddAssert("input recorder contains actual key transitions", () => Player.Score.Replay.Frames.Count > 4);

            foreach (var time in new[] { 4000d, 3200, 0 })
            {
                moveTo(time);
                AddAssert("rewind restores score, health and profile counters", state, () => Is.EqualTo(snapshots[time]));
                AddStep("no replay handler is attached", assertNoReplay);
            }

            void moveTo(double time)
            {
                AddStep("move live clock " + time, () =>
                {
                    if (explicitSeek)
                        Player.GameplayClockContainer.Seek(time);
                    else
                        AdvanceReplayClock(time);
                });
                AddUntilStep("clock reached target", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - time) < .001);
            }

            string state()
            {
                var score = (BmsScoreProcessor)Player.ScoreProcessor;
                var health = (BmsHealthProcessor)Player.HealthProcessor;
                return JsonSerializer.Serialize(new
                {
                    score.EngineScore.Score, score.EngineScore.AccuracyPoints, score.EngineScore.AccuracyMaximum,
                    score.EngineScore.Combo, score.EngineScore.MaximumCombo, score.EngineScore.JudgedOpportunities,
                    score.EngineScore.Bonus, Counters = score.EngineScore.Counters.OrderBy(pair => pair.Key).ToArray(),
                    Results = score.EngineScore.ResultCounts.OrderBy(pair => pair.Key.Value).ToArray(),
                    Health = health.CurrentHealth, health.HasFailed, score.JudgementEvents,
                });
            }
        }
    }

    public partial class TestSceneBmsLeaderboardRank
    {
        [Test]
        [Category("ManiaProfile")]
        public void TestManiaLeaderboardAndTooltipUseProfileScoreAndLabels()
        {
            BmsLeaderboardScore row = null!;
            AddStep("load Mania score", () =>
            {
                var score = createScore(ScoreRank.S, "Mania");
                score.Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
                score.TotalScoreWithoutMods = 735812;
                Child = new Container
                {
                    Width = 720,
                    Height = BmsLeaderboardScore.HEIGHT,
                    Child = row = new BmsLeaderboardScore(score, false),
                };
            });
            AddUntilStep("score loaded", () => row.IsLoaded);
            AddAssert("grade uses Mania lettering", () => row.ChildrenOfType<OsuSpriteText>()
                .Single(text => text.Name == "Leaderboard grade").Text.ToString(), () => Is.EqualTo("S"));
            AddAssert("native score shown", () => row.ChildrenOfType<OsuSpriteText>()
                .Single(text => text.Name == "Leaderboard score").Text.ToString(), () => Is.EqualTo("735812"));
            AddStep("hover score", () => InputManager.MoveMouseTo(row));
            AddUntilStep("tooltip appears", () => this.ChildrenOfType<BmsLeaderboardScoreTooltip>().SingleOrDefault()?.IsPresent == true);
            AddAssert("tooltip uses selected profile", () =>
            {
                var texts = this.ChildrenOfType<BmsLeaderboardScoreTooltip>().Single().ChildrenOfType<OsuSpriteText>()
                    .Select(text => text.Text.ToString()).ToArray();
                return texts.Contains("735812") && texts.Contains("S") && texts.Contains("MISS")
                       && !texts.Contains("E-POOR") && !texts.Contains("EXSCORE") && !texts.Contains("AAA");
            });
        }
    }

    public partial class TestSceneBmsReplayRebuild
    {
        [Test]
        [Category("ManiaProfile")]
        public void ManiaProfileSurvivesRebuild()
        {
            AddStep("load mania profile replay", () =>
            {
                layout = BmsLayoutVariant.Bme7K;
                scratchReplay = false;
                disableFailure = false;
                LoadPlayer([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]);
            });
            verifyRebuilds([0, 1000, 1500, 1600, 3010, 4100]);
            AddAssert("mania configuration remains selected", () => ((BmsScoreProcessor)Player.ScoreProcessor).EngineChart.Configuration.ProfileId,
                () => Is.EqualTo("osu-lazer-mania-standardised"));
            AddAssert("playfield uses the processor's compiled timing", () =>
            {
                var chart = ((BmsScoreProcessor)Player.ScoreProcessor).EngineChart;
                var note = chart.Objects.OfType<BmsNote>().First();
                var column = (BmsColumn)((BmsPlayfield)Player.DrawableRuleset.Playfield).Stage.Columns[note.Column];
                var timing = column.Judgements.GetTiming(note);
                return ReferenceEquals(timing.HeadWindows, chart.TimingFor(note).HeadWindows) && timing.HeadDeadline == 136500;
            });
            AddAssert("single profile health remains selected", () => ((BmsHealthProcessor)Player.HealthProcessor).CurrentHealth.Layers.IsEmpty);
            AddUntilStep("Mania score reaches the HUD", () =>
            {
                var graph = Player.ChildrenOfType<BmsScoreGraph>().Single();
                var score = (BmsScoreProcessor)Player.ScoreProcessor;
                var expected = BmsStrings.ScoreGraphScore(score.EngineChart.Profile.ScoreLabel, score.EngineScore.Score).ToString();
                return graph.ChildrenOfType<OsuSpriteText>().Any(text => text.Text.ToString() == expected);
            });
            AddAssert("HUD uses Mania judgement labels without a target", () =>
            {
                var texts = Player.ChildrenOfType<BmsScoreGraph>().Single().ChildrenOfType<OsuSpriteText>()
                    .Select(text => text.Text.ToString()).ToArray();
                return texts.Contains("MISS") && !texts.Contains("E-POOR")
                    && Player.ChildrenOfType<BmsScoreGraph>().Single().ChildrenOfType<Drawable>()
                        .Where(drawable => drawable.Name is "Target score bar" or "Target score row").All(drawable => !drawable.IsPresent);
            });
            AddAssert("judgement display uses skin images", () =>
            {
                var drawables = Player.ChildrenOfType<BmsJudgementDisplay>().Single().ChildrenOfType<SkinnableDrawable>().ToArray();
                return drawables.Length == 6 && drawables.All(drawable => drawable.Drawable is BmsJudgementPiece
                    && drawable.Drawable.ChildrenOfType<Sprite>().Any());
            });
            seek(2990);
            AddStep("hide empty inputs and untimed misses", () =>
            {
                var meter = Player.ChildrenOfType<BmsHitErrorMeter>().Single();
                meter.ShowPoor.Value = meter.ShowEmptyPoor.Value = false;
                Player.ChildrenOfType<BmsJudgementDisplay>().Single().ShowEmptyPoor.Value = false;
            });
            AddAssert("profile miss window stays visible", () => Player.ChildrenOfType<BmsHitErrorMeter>().Single()
                .ChildrenOfType<Box>().Single(box => box.Name == $"{HitResult.Miss} window").Alpha == 1);
            AddStep("play through the broken hold release", () => Player.GameplayClockContainer.Start());
            AddUntilStep("timed Mania MEH reaches the meter", () => Player.ChildrenOfType<BmsHitErrorMeter>().Single().Judgements.Lines
                .Any(line => line.Colour == ((BmsScoreProcessor)Player.ScoreProcessor).EngineChart.Profile.JudgementColour(HitResult.Meh) && line.BoundaryIsFast == null));
            AddStep("stop after the release", () => Player.GameplayClockContainer.Stop());
            seek(2990);
            AddAssert("rebuild clears timing markers", () => Player.ChildrenOfType<BmsHitErrorMeter>().Single().Judgements.Lines.Count == 0);
        }

        [Test]
        [Category("ManiaProfile")]
        public void ManiaMissDisplayRemainsVisibleWhenEmptyInputsAreHidden()
        {
            AddStep("load silent Mania replay", () =>
            {
                layout = BmsLayoutVariant.Bme7K;
                scratchReplay = false;
                disableFailure = false;
                emptyReplay = true;
                LoadPlayer([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]);
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully && Player.Alpha == 1);
            seek(1000);
            AddStep("hide empty inputs and play through head deadline", () =>
            {
                Player.ChildrenOfType<BmsJudgementDisplay>().Single().ShowEmptyPoor.Value = false;
                Player.GameplayClockContainer.Start();
            });
            AddUntilStep("consuming miss is displayed", () =>
            {
                var pieces = Player.ChildrenOfType<BmsJudgementDisplay>().Single().ChildrenOfType<BmsJudgementPiece>();
                return pieces.Any(piece => piece.Parent?.Alpha > 0 && piece.Alpha > 0 && piece.ChildrenOfType<Sprite>().Any());
            });
            AddStep("stop playback", () => Player.GameplayClockContainer.Stop());
        }
    }

    public partial class TestSceneBmsReplayVisualizer
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("ManiaProfile")]
        public void TestManiaLongNoteAndMissKeepProfileResultsAcrossSeek(bool cached)
        {
            AddStep("load Mania replay", () =>
            {
                layout = BmsLayoutVariant.Bme7K;
                useReplay = true;
                LoadPlayer([new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }]);
            });
            AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddStep("add visualizer", () => Player.HUDOverlay.Add(visualizer = new BmsReplayVisualizer
            {
                Colour = cached ? Colour4.White : ColourInfo.GradientHorizontal(Colour4.Cyan, Colour4.White),
            }));
            AddUntilStep("visualizer loaded", () => visualizer.IsLoaded);
            seek(2500);
            AddUntilStep("held note shows Mania GOOD", () => noteHasColour(2, HitResult.Good));
            seek(4500);
            AddUntilStep("missed note retains Mania MISS", () => noteHasColour(3, HitResult.Miss));
            seek(2500);
            AddUntilStep("rewound hold retains Mania GOOD", () => noteHasColour(2, HitResult.Good));

            bool noteHasColour(int column, HitResult result)
            {
                var width = visualizer.DrawWidth / BmsLayout.GetTotalColumns(layout);

                if (cached)
                {
                    return cachedRectangles().Where(rectangle => rectangle.Kind is BmsReplayVisualizer.TimelineKind.Note or BmsReplayVisualizer.TimelineKind.LongNote)
                        .Where(rectangle => rectangle.X >= column * width && rectangle.X + rectangle.Width <= (column + 1) * width)
                        .Any(rectangle => rectangle.Colour == (Colour4)((BmsScoreProcessor)Player.ScoreProcessor).EngineChart.Profile.JudgementColour(result));
                }

                return primitives().Where(rectangle => rectangle.Kind == BmsReplayVisualizer.PlotElementKind.Note)
                    .Where(rectangle => rectangle.Bounds.X >= column * width && rectangle.Bounds.Right <= (column + 1) * width)
                    .Any(rectangle => rectangle.Colour == (Colour4)((BmsScoreProcessor)Player.ScoreProcessor).EngineChart.Profile.JudgementColour(result));
            }
        }
    }

    public partial class TestSceneBmsResultScreenStatistics
    {
        [TestCase(false)]
        [TestCase(true)]
        [Category("ManiaProfile")]
        public void TestResultStatisticsUseProfileScore(bool mania)
        {
            TestBmsSoloResultsScreen screen = null!;

            AddStep("load results screen", () =>
            {
                OsuScreenStack stack;
                Child = stack = new OsuScreenStack { RelativeSizeAxes = Axes.Both };
                var score = createScore();

                if (mania)
                {
                    score.Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
                    score.TotalScoreWithoutMods = 812345;
                }

                stack.Push(screen = new TestBmsSoloResultsScreen(score));
            });

            AddUntilStep("results screen loaded", () => screen.IsLoaded);
            AddUntilStep("statistics shown automatically", () => this.ChildrenOfType<BmsStatisticsPanel>().Single().State.Value == Visibility.Visible);
            AddAssert("performance statistic is absent", () => !this.ChildrenOfType<PerformanceStatistic>().Any());
            AddUntilStep("new overview loaded", () => screen.ChildrenOfType<BmsResultOverview>().SingleOrDefault()?.IsLoaded == true);
            AddAssert("score uses selected profile", () => screen.ChildrenOfType<BmsResultFittedText>()
                .Single(text => text.Name == "Score value").ChildrenOfType<SpriteText>().Single().Text.ToString(), () => Is.EqualTo(mania ? "812345" : "1542"));
            AddAssert("judgement labels use selected profile", () => screen.ChildrenOfType<BmsResultJudgementRow>()
                .Single(row => row.Result == HitResult.Miss).ChildrenOfType<SpriteText>().Any(text => text.Text.ToString() == (mania ? "MISS" : "E-POOR")));
            AddAssert("six judgement bars use score counts", () => screen.ChildrenOfType<BmsResultJudgementRow>().Select(row => row.Count),
                () => Is.EqualTo((int[])[621, 300, 41, 12, 8, 3]));
            AddAssert("judgement bar uses count proportion", () => screen.ChildrenOfType<BmsResultJudgementRow>()
                    .Single(row => row.Result == HitResult.Perfect).ChildrenOfType<BmsResultBar>().Single().Proportion,
                () => Is.EqualTo(621.0 / 985).Within(0.0001));
            AddAssert("combo bar uses maximum achievable combo", () => screen.ChildrenOfType<BmsResultBar>()
                    .Single(bar => bar.Name == "Result combo progress").Proportion,
                () => Is.EqualTo(653.0 / 985).Within(0.0001));
        }

        [Test]
        [Category("ManiaProfile")]
        public void TestManiaPerfectRankPreservesAccuracyProgress()
        {
            const double mania_accuracy = 300d / 305;
            TestBmsSoloResultsScreen screen = null!;

            AddStep("load Mania results", () =>
            {
                var stack = new OsuScreenStack
                {
                    RelativeSizeAxes = Axes.Both,
                };

                Child = stack;
                var score = createScore();
                score.Accuracy = mania_accuracy;
                score.TotalScore = 946049;
                score.TotalScoreWithoutMods = 946049;
                score.MaxCombo = 305;
                score.Mods = [new BmsModManiaProfile { OverallDifficulty = { Value = 5 }, DrainRate = { Value = 5 } }];
                score.Statistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Great] = 305,
                };
                score.MaximumStatistics.Clear();
                score.MaximumStatistics[HitResult.Perfect] = 305;
                score.HitEvents = Enumerable.Range(0, 305).Select(index => new HitEvent(30, index % 7, HitResult.Great, new BmsNote
                {
                    Column = index % 7,
                    StartTime = index * 100,
                }, null, null)).ToList();
                score.Rank = GameplayProfile.FromMods(score.Mods).RankFromScore(mania_accuracy, score.Statistics);
                stack.Push(screen = new TestBmsSoloResultsScreen(score));
            });

            AddUntilStep("Mania results screen loaded", () => screen.IsLoaded);
            AddStep("finish rating animation", () => screen.ChildrenOfType<BmsAccuracyCircle>().Single().FinishTransforms(true));
            AddAssert("perfect Mania rank preserves non-perfect accuracy", () => screen.ChildrenOfType<BmsAccuracyCircle>().Single()
                    .ChildrenOfType<CircularProgress>().Single(circle => circle.Name == "Accuracy circle").Progress,
                () => Is.EqualTo(mania_accuracy - 0.001).Within(0.0001));
        }
    }
}
