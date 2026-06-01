#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsImportedPlayer : BmsPlayerTestScene
{
    private BeatmapManager beatmapManager = null!;
    private RealmRulesetStore rulesets = null!;
    private BeatmapInfo importedBeatmap = null!;
    private BmsHitObject firstKeyNote = null!;
    private BmsHitObject offsetTarget = null!;
    private double initialHealth;

    protected override bool UseFreshStoragePerRun => true;

    [BackgroundDependencyLoader]
    private void load(GameHost host, AudioManager audio)
    {
        Dependencies.Cache(rulesets = new RealmRulesetStore(Realm));
        Dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, audio, Resources, host, Beatmap.Default));
        Dependencies.Cache(new ScoreManager(rulesets, () => beatmapManager, LocalStorage, Realm, API));
        Dependencies.Cache(Realm);
    }

    /// <summary>
    /// Locates the embedded <c>bms_test_songs</c> root by trusting
    /// <see cref="BmsEmbeddedSongDecoderTest.TestSongsRoot"/> first and falling back
    /// to a parent-walk if that path no longer holds the canonical Aleph-0 chart.
    /// </summary>
    private static string testSongsRoot
    {
        get
        {
            const string canary = @"Aleph-0 (by LeaF)\_7NORMAL.bms";

            if (File.Exists(Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, canary)))
                return BmsEmbeddedSongDecoderTest.TestSongsRoot;

            foreach (var root in candidateRoots())
            {
                if (File.Exists(Path.Combine(root, canary)))
                    return root;
            }

            return BmsEmbeddedSongDecoderTest.TestSongsRoot;
        }
    }

    private static IEnumerable<string> candidateRoots()
    {
        var seeds = new[]
        {
            TestContext.CurrentContext.WorkDirectory,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var seed in seeds)
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(seed)); dir != null; dir = dir.Parent)
            {
                foreach (var candidate in new[]
                         {
                             Path.Combine(dir.FullName, "bms_test_songs"),
                             Path.Combine(dir.FullName, "osu.Game.Rulesets.BmsRuleset.Tests", "bms_test_songs"),
                         })
                {
                    if (seen.Add(candidate))
                        yield return candidate;
                }
            }
        }
    }

    private void addBmsRuleset()
    {
        var info = new BmsRuleset().RulesetInfo;

        Realm.Write(r =>
        {
            var existing = r.Find<RulesetInfo>(info.ShortName);

            if (existing == null)
            {
                r.Add(new RulesetInfo(info.ShortName, info.Name, info.InstantiationInfo, info.OnlineID)
                {
                    Available = true,
                });

                return;
            }

            existing.Name = info.Name;
            existing.InstantiationInfo = info.InstantiationInfo;
            existing.OnlineID = info.OnlineID;
            existing.Available = true;
        });
    }

    private void importRealBms()
    {
        var chartPath = Path.Combine(testSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
        Assert.That(File.Exists(chartPath), Is.True, $"Missing test chart at {chartPath}");
        new BmsFileImporter(Realm, LocalStorage).Import(chartPath).WaitSafely();
    }

    private void selectImportedBeatmap(Mod[]? mods = null)
    {
        importedBeatmap = Realm.Run(r => r.All<BeatmapSetInfo>()
            .AsEnumerable()
            .Single(s => !s.DeletePending && s.Beatmaps.Any(b => b.Ruleset.ShortName == "bms"))
            .Beatmaps.Single()
            .Detach());

        Ruleset.Value = importedBeatmap.Ruleset;
        Beatmap.Value = beatmapManager.GetWorkingBeatmap(importedBeatmap, true);
        SelectedMods.Value = mods ?? Array.Empty<Mod>();
    }

    /// <summary>
    /// Resolves the physical key bound by default to the given column of the given layout,
    /// so input simulation is not tied to a particular keyboard layout hardcoded into a test.
    /// </summary>
    private static Key defaultKeyForColumn(BmsLayoutVariant variant, int column)
    {
        var action = BmsKeyBindingConfiguration.ActionForColumn(variant, column)
                     ?? throw new InvalidOperationException($"Column {column} of {variant} has no action mapping.");

        var binding = BmsKeyBindingConfiguration.GetDefaultKeyBindings((int)variant)
            .First(b => b.Action is BmsAction a && a == action);

        var inputKey = binding.KeyCombination.Keys.Single();

        // The InputKey enum mirrors the underlying osuTK Key values for the keyboard
        // range the BMS bindings live in (letters, keypad, arrows, modifiers); a direct
        // numeric cast is reliable here and avoids string parsing.
        return (Key)(int)inputKey;
    }

    [Test]
    public void TestAutoplayWithJudgementsAndHealth()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", importRealBms);
        AddStep("select imported beatmap", () => selectImportedBeatmap());
        AddStep("load player", () => LoadScreen(Player = CreateBmsPlayer(BmsTestReplays.CreateAutoPlayFrames)));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);

        AddStep("capture initial health", () => initialHealth = Player.HealthProcessor.Health.Value);
        AddStep("seek to gameplay", () => Player.GameplayClockContainer.Seek(Player.DrawableRuleset.Objects.First().StartTime - 250));
        AddUntilStep("judgements produced", () => Player.Results.Count, () => Is.GreaterThanOrEqualTo(10));
        AddAssert("autoplay produces perfects", () => Player.Results.Count(r => r.Type == HitResult.Perfect), () => Is.GreaterThanOrEqualTo(10));
        AddAssert("health increased from initial", () => Player.HealthProcessor.Health.Value, () => Is.GreaterThan(initialHealth));
    }

    [Test]
    public void TestImportedRealBmsLoadsPlayer()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", importRealBms);
        AddStep("select imported beatmap", () => selectImportedBeatmap());

        AddAssert("beatmap path stored", () => Beatmap.Value.BeatmapInfo.Path, () => Is.EqualTo("_7NORMAL.bms"));
        AddAssert("stored file resolves", () => Beatmap.Value.BeatmapInfo.BeatmapSet?.GetPathForFile(Beatmap.Value.BeatmapInfo.Path!), () => Is.Not.Null.And.Not.Empty);
        AddAssert("working beatmap decodes", () => Beatmap.Value.Beatmap.HitObjects.OfType<BmsHitObject>().Count(), () => Is.GreaterThan(100));

        AddStep("load player", () => LoadScreen(Player = CreateBmsPlayer(BmsTestReplays.CreateAutoPlayFrames)));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("player has imported objects", () => Player.DrawableRuleset.Objects.Count(), () => Is.GreaterThan(100));

        AddStep("seek before first key note", () =>
        {
            firstKeyNote = Player.DrawableRuleset.Objects.OfType<BmsHitObject>().First(o => o.Column == 1);
            Player.GameplayClockContainer.Seek(firstKeyNote.StartTime - 100);
        });

        AddUntilStep("target key note alive", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Any(d => d.HitObject == firstKeyNote));
        AddStep("press key for column 1", () =>
        {
            var variant = (BmsLayoutVariant)((BmsDrawableRuleset)Player.DrawableRuleset).Variant;
            InputManager.Key(defaultKeyForColumn(variant, 1));
        });
        AddUntilStep("hit judgement produced", () => Player.Results.Any(r => r.IsHit && r.Type != HitResult.Miss));
    }

    [Test]
    public void TestOffsetAutoplayProducesVariedJudgements()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", importRealBms);
        AddStep("select imported beatmap", () => selectImportedBeatmap());

        AddStep("load player with offset replay", () => LoadScreen(Player = CreateBmsPlayer(beatmap => BmsTestReplays.CreateOffsetAutoPlayFrames(beatmap, 25))));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);

        AddStep("seek to first offset note", () =>
        {
            var ruleset = (BmsDrawableRuleset)Player.DrawableRuleset;
            offsetTarget = ruleset.Objects.OfType<BmsHitObject>().First(o => BmsKeyBindingConfiguration.ActionForColumn((BmsLayoutVariant)ruleset.Variant, o.Column) != null);
            Player.GameplayClockContainer.Seek(offsetTarget.StartTime - 250);
        });

        AddUntilStep("great judgement produced", () => Player.Results.Any(r => r.Type == HitResult.Great));
        AddAssert("no perfect-only replay", () => Player.Results.Any(r => r.Type != HitResult.Perfect));
    }
}
