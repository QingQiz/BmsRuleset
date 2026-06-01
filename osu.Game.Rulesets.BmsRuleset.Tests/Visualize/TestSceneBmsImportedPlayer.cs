using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.ImportExport;
using osu.Game.Rulesets.BmsRuleset.Mods;
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
public partial class TestSceneBmsImportedPlayer : PlayerTestScene, IStorageResourceProvider
{
    private BeatmapManager beatmapManager = null!;
    private RealmRulesetStore rulesets = null!;
    private BeatmapInfo importedBeatmap = null!;
    private BmsHitObject firstKeyNote = null!;
    private BmsHitObject offsetTarget = null!;
    private GameHost gameHost = null!;

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
        gameHost = host;
        Dependencies.Cache(rulesets = new RealmRulesetStore(Realm));
        Dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, audio, Resources, host, Beatmap.Default));
        Dependencies.Cache(new ScoreManager(rulesets, () => beatmapManager, LocalStorage, Realm, API));
        Dependencies.Cache(Realm);
    }

    public IRenderer Renderer => gameHost.Renderer;

    public AudioManager AudioManager => Audio;

    public IResourceStore<byte[]> Files => null!;

    public new IResourceStore<byte[]> Resources => base.Resources;

    public IResourceStore<TextureUpload> CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) => gameHost.CreateTextureLoaderStore(underlyingStore);

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

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

    private void selectImportedBeatmap(Mod[] mods = null)
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

    private TestPlayFieldCreator.SkinnedTestPlayer createArgonPlayer(Func<BmsBeatmap, IList<ReplayFrame>> createReplay)
        => new(TestPlayFieldCreator.CreateSkinSource(TestPlayFieldCreator.SkinKind.Argon, this),createReplay);

    [Test]
    public void TestAutoplayWithJudgementsAndHealth()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", importRealBms);
        AddStep("select imported beatmap", () => selectImportedBeatmap());
        AddStep("load player", () => LoadScreen(Player = createArgonPlayer(TestPlayFieldCreator.CreateAutoPlayFrames)));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);

        AddStep("seek to gameplay", () => Player.GameplayClockContainer.Seek(Player.DrawableRuleset.Objects.First().StartTime - 250));
        AddUntilStep("judgements produced", () => Player.Results.Count, () => Is.GreaterThanOrEqualTo(10));
        AddAssert("autoplay produces perfects", () => Player.Results.Count(r => r.Type == HitResult.Perfect), () => Is.GreaterThanOrEqualTo(10));
        AddAssert("health increased", () => Player.HealthProcessor.Health.Value, () => Is.GreaterThan(0.2));
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

        AddStep("load player", () => LoadScreen(Player = createArgonPlayer(TestPlayFieldCreator.CreateAutoPlayFrames)));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("player has imported objects", () => Player.DrawableRuleset.Objects.Count(), () => Is.GreaterThan(100));

        AddStep("seek before first key note", () =>
        {
            firstKeyNote = Player.DrawableRuleset.Objects.OfType<BmsHitObject>().First(o => o.Column == 1);
            Player.GameplayClockContainer.Seek(firstKeyNote.StartTime - 100);
        });

        AddUntilStep("target key note alive", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Any(d => d.HitObject == firstKeyNote));
        AddStep("press key 1", () => InputManager.Key(Key.Z));
        AddUntilStep("hit judgement produced", () => Player.Results.Any(r => r.IsHit && r.Type != HitResult.Miss));
    }

    [Test]
    public void TestOffsetAutoplayProducesVariedJudgements()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", importRealBms);
        AddStep("select imported beatmap", () => selectImportedBeatmap());

        AddStep("load player with offset replay", () => LoadScreen(Player = createArgonPlayer(beatmap => TestPlayFieldCreator.CreateOffsetAutoPlayFrames(beatmap, 25))));
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
