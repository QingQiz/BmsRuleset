// TODO FIXME not work

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
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsVisualPlayer : PlayerTestScene
{
    private BeatmapManager beatmapManager = null!;
    private RealmRulesetStore rulesets = null!;
    private BeatmapInfo importedBeatmap = null!;

    private static string testSongsRoot
    {
        get
        {
            foreach (var root in candidateTestSongRoots())
            {
                if (File.Exists(Path.Combine(root, "Aleph-0 (by LeaF)", "_7NORMAL.bms")))
                    return root;
            }

            return BmsEmbeddedSongDecoderTest.TestSongsRoot;
        }
    }

    private static string[] candidateTestSongRoots()
    {
        var roots = new[]
        {
            BmsEmbeddedSongDecoderTest.TestSongsRoot,
            TestContext.CurrentContext.WorkDirectory,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        return roots.SelectMany(candidateRootsFrom).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] candidateRootsFrom(string start)
    {
        var candidates = new List<string>();
        var directory = new DirectoryInfo(Path.GetFullPath(start));

        while (directory != null)
        {
            candidates.Add(Path.Combine(directory.FullName, "bms_test_songs"));
            candidates.Add(Path.Combine(directory.FullName, "osu.Game.Rulesets.BmsRuleset.Tests", "bms_test_songs"));
            directory = directory.Parent;
        }

        return candidates.ToArray();
    }

    protected override bool HasCustomSteps => true;

    protected override bool UseFreshStoragePerRun => true;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    [BackgroundDependencyLoader]
    private void load(GameHost host, AudioManager audio)
    {
        Dependencies.Cache(rulesets = new RealmRulesetStore(Realm));
        Dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, audio, Resources, host, Beatmap.Default));
        Dependencies.Cache(new ScoreManager(rulesets, () => beatmapManager, LocalStorage, Realm, API));
        Dependencies.Cache(Realm);
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

    [Test]
    public void TestImportedRealBmsAutoplayWithBeatmapSkin()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", () =>
        {
            var chartPath = Path.Combine(testSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
            Assert.That(File.Exists(chartPath), Is.True, $"Missing test chart at {chartPath}");
            new BmsFileImporter(Realm, LocalStorage).Import(chartPath).WaitSafely();
        });

        AddStep("select imported beatmap", () =>
        {
            importedBeatmap = Realm.Run(r => r.All<BeatmapSetInfo>()
                .AsEnumerable()
                .Single(s => !s.DeletePending && s.Beatmaps.Any(b => b.Ruleset.ShortName == "bms"))
                .Beatmaps.Single()
                .Detach());

            Ruleset.Value = importedBeatmap.Ruleset;
            Beatmap.Value = beatmapManager.GetWorkingBeatmap(importedBeatmap, true);
            SelectedMods.Value = new Mod[] { new BmsModAutoplay() };
        });

        AddAssert("beatmap skin resource imported", () => Beatmap.Value.BeatmapInfo.BeatmapSet!.Files.Any(f => f.Filename == "_title.png"));
        AddAssert("background samples imported", () => Beatmap.Value.BeatmapInfo.BeatmapSet!.Files.Any(f => f.Filename.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)));

        AddStep("load player", () => LoadScreen(Player = new TestPlayer(false, false)));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("autoplay replay attached", () => Player.DrawableRuleset.ReplayScore, () => Is.Not.Null);
        AddAssert("player has imported objects", () => Player.DrawableRuleset.Objects.Count(), () => Is.GreaterThan(100));

        AddStep("seek to gameplay", () => Player.GameplayClockContainer.Seek(Player.DrawableRuleset.Objects.First().StartTime - 500));
        AddUntilStep("autoplay produces judgements", () => Player.Results.Count, () => Is.GreaterThan(0));
    }
}
