using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osuTK.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.BmsRuleset.Mods;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Components;
using osu.Game.Rulesets.BmsRuleset.UI.SongSelect.Lamp;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSongSelectLampHack : ScreenTestScene
{
    private static int nextTestId;

    private BeatmapManager beatmaps = null!;
    private RealmRulesetStore rulesets = null!;
    private OsuConfigManager config = null!;
    private ScoreManager scoreManager = null!;
    private RealmDetachedBeatmapStore beatmapStore = null!;
    private BmsSongSelect songSelect = null!;
    private BeatmapSetInfo lampBeatmapSet = null!;
    private Action<WorkingBeatmap> resumeInvalidationObserver;

    [TearDown]
    public void TearDownResumeObserver()
    {
        if (resumeInvalidationObserver != null)
            beatmaps.OnInvalidated -= resumeInvalidationObserver;

        resumeInvalidationObserver = null;
    }

    // AssistClear (beatoraja's pattern-assist lamp) has no producing mod in this ruleset and is excluded.
    private static readonly BmsLamp[] all_lamps = Enum.GetValues<BmsLamp>().Where(lamp => lamp != BmsLamp.AssistClear).ToArray();

    private BeatmapCarousel carousel => songSelect.ChildrenOfType<BeatmapCarousel>().Single();

    [Cached]
    private readonly OsuLogo logo;

    [Cached]
    private readonly VolumeOverlay volume;

    [Cached(typeof(INotificationOverlay))]
    private readonly INotificationOverlay notificationOverlay = new NotificationOverlay();

    public TestSceneBmsSongSelectLampHack()
    {
        Children =
        [
            new PopoverContainer
            {
                RelativeSizeAxes = Axes.Both,
                Children =
                [
                    new Toolbar
                    {
                        State = { Value = Visibility.Visible },
                    },
                    logo = new OsuLogo
                    {
                        Alpha = 0,
                    },
                    volume = new VolumeOverlay(),
                ],
            },
        ];

        Stack.Padding = new MarginPadding { Top = Toolbar.HEIGHT };
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        dependencies.Cache(rulesets = new RealmRulesetStore(Realm));
        dependencies.Cache(Realm);
        dependencies.Cache(beatmaps = new BeatmapManager(LocalStorage, Realm, null, Dependencies.Get<AudioManager>(), Resources, Dependencies.Get<GameHost>(), Beatmap.Default));
        dependencies.Cache(config = new OsuConfigManager(LocalStorage));
        dependencies.Cache(scoreManager = new ScoreManager(rulesets, () => beatmaps, LocalStorage, Realm, API, config));
        dependencies.CacheAs<BeatmapStore>(beatmapStore = new RealmDetachedBeatmapStore());

        return dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Add(beatmapStore);
    }

    public override void SetUpSteps()
    {
        base.SetUpSteps();

        AddStep("reset song select stores", () =>
        {
            Ruleset.Value = rulesets.AvailableRulesets.Single(r => r.ShortName == Constant.SHORT_NAME);
            Beatmap.SetDefault();
            SelectedMods.SetDefault();

            config.SetValue(OsuSetting.SongSelectSortingMode, SortMode.Title);
            config.SetValue(OsuSetting.SongSelectGroupMode, GroupMode.None);

            lampBeatmapSet = null!;
            songSelect = null!;
        });

        AddStep("delete all scores", () => scoreManager.Delete());
        AddStep("delete all beatmaps", () => beatmaps.Delete());
    }

    [Test]
    public void TestAllLamps()
    {
        importLampBeatmapSet();
        importLampScores();
        loadSongSelect();

        AddUntilStep("BMS ruleset active", () => Ruleset.Value.ShortName == Constant.SHORT_NAME);
        AddUntilStep("all lamp panels are realised", () => lampRankDisplays().Count(), () => Is.EqualTo(all_lamps.Length));
        AddUntilStep("all real panels have BMS lamps", () => visibleLampDisplays().Count(), () => Is.EqualTo(all_lamps.Length));
        AddAssert("all lamp states are represented", () => visibleLampDisplays().Select(lampFromDisplay).OrderBy(l => l).SequenceEqual(all_lamps.OrderBy(l => l)));
        AddAssert("played lamps keep stock rank", () => playedRankDisplays().All(hasVisibleStockRank));
        AddAssert("no-play lamp has no stock rank", () => noPlayRankDisplay() is { } display && hasNoStockRank(display));
        AddAssert("ruleset mark hidden by every lamp", () => lampRankDisplays().All(hasHiddenRulesetMark));
        AddAssert("lamps render as panel backgrounds", () => lampPanels().All(panel =>
        {
            var lampDrawable = panel.ChildrenOfType<BmsLampDisplay>().SingleOrDefault(l => l.Alpha > 0);
            return lampDrawable != null && getBackgroundContainer(panel) == lampDrawable.Parent;
        }));
        AddAssert("lamps fill their song select cards", () => lampPanels().All(panel =>
        {
            var lampDrawable = panel.ChildrenOfType<BmsLampDisplay>().SingleOrDefault(l => l.Alpha > 0);

            return lampDrawable != null
                   && Math.Abs(lampDrawable.ScreenSpaceDrawQuad.Width - panel.TopLevelContent.ScreenSpaceDrawQuad.Width) < 0.5f
                   && Math.Abs(lampDrawable.ScreenSpaceDrawQuad.Height - panel.TopLevelContent.ScreenSpaceDrawQuad.Height) < 0.5f;
        }));
        AddAssert("all lamps fit on screen", () => lampPanels().All(panel =>
            panel.ScreenSpaceDrawQuad.AABBFloat.Top >= 0 && panel.ScreenSpaceDrawQuad.AABBFloat.Bottom <= DrawHeight));
    }

    [Test]
    public void TestReplacementPageOwnsModSelectOverlay()
    {
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddAssert("mod overlay belongs to song select", () => songSelect.ModSelectOverlay.Parent, () => Is.SameAs(songSelect));
        AddStep("show mod overlay", () => songSelect.ModSelectOverlay.Show());
        AddUntilStep("mod overlay visible", () => songSelect.ModSelectOverlay.State.Value == Visibility.Visible && songSelect.ModSelectOverlay.IsPresent);
    }

    [Test]
    public void TestResumeRefetchesSelectionOnlyInBackground()
    {
        WorkingBeatmap selected = null!;
        var invalidationThreads = new ConcurrentQueue<bool>();

        importLampBeatmapSet();
        loadSongSelect();
        AddStep("suspend song select", () =>
        {
            selected = songSelect.Beatmap.Value;
            songSelect.Push(new ResumeTestScreen());
        });
        AddUntilStep("child screen loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("observe selection refetches", () =>
        {
            resumeInvalidationObserver = _ => invalidationThreads.Enqueue(ThreadSafety.IsUpdateThread);
            beatmaps.OnInvalidated += resumeInvalidationObserver;
        });
        AddStep("return to song select", () => Stack.Exit());
        AddUntilStep("fresh selection applied", () => !ReferenceEquals(songSelect.Beatmap.Value, selected));
        AddAssert("same chart selected", () => songSelect.Beatmap.Value.BeatmapInfo.ID, () => Is.EqualTo(selected.BeatmapInfo.ID));
        AddAssert("single background refetch", () => invalidationThreads.ToArray(), () => Is.EqualTo(new[] { false }));
    }

    [Test]
    public void TestResumeReplacesHiddenSelection()
    {
        BeatmapInfo selected = null!;

        importLampBeatmapSet();
        loadSongSelect();
        AddStep("suspend song select", () =>
        {
            selected = songSelect.Beatmap.Value.BeatmapInfo;
            songSelect.Push(new ResumeTestScreen());
        });
        AddUntilStep("child screen loaded", () => Stack.CurrentScreen is ResumeTestScreen { IsLoaded: true });
        AddStep("hide selected chart", () => beatmaps.Hide(selected));
        AddUntilStep("chart hidden in database", () => Realm.Run(r => r.Find<BeatmapInfo>(selected.ID)!.Hidden));
        AddStep("return to song select", () => Stack.Exit());
        AddUntilStep("another chart selected", () => songSelect.Beatmap.Value.BeatmapInfo.ID != selected.ID);
        AddAssert("replacement is selectable", () => !songSelect.Beatmap.Value.BeatmapInfo.Hidden && !songSelect.Beatmap.IsDefault);
    }

    private partial class ResumeTestScreen : OsuScreen
    {
    }

    [Test]
    public void TestUnavailableDifficultyTableBeatmapIsPresented()
    {
        const string missing_hash = "abcdef0123456789abcdef0123456789";
        DifficultyTableStore previousStore = null!;

        AddStep("load missing table entry", () =>
        {
            previousStore = BmsRulesetRuntime.DifficultyTableStore;
            var store = new DifficultyTableStore(null, Path.Combine(LocalStorage.GetFullPath(string.Empty), "unavailable-table-carousel-test"));
            store.RestoreTable(new DifficultyTable.DifficultyTable
            {
                Name = "Test",
                Symbol = "T",
                Entries = [new TableEntry { Level = "1", Md5Hash = missing_hash, Title = "Missing" }],
            });
            BmsRulesetRuntime.DifficultyTableStore = store;
        });
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("missing entry presented", () => !songSelect.IsFiltering && carousel.GetCarouselItems()?
            .Select(item => item.Model)
            .OfType<GroupedBeatmap>()
            .Any(grouped => grouped.Beatmap.MD5Hash == missing_hash) == true);
        AddAssert("missing entry uses BMS", () => carousel.GetCarouselItems()!
            .Select(item => item.Model)
            .OfType<GroupedBeatmap>()
            .Single(grouped => grouped.Beatmap.MD5Hash == missing_hash)
            .Beatmap.Ruleset.ShortName, () => Is.EqualTo(Constant.SHORT_NAME));
        AddUntilStep("missing entry panel realised", () => carousel.ChildrenOfType<BmsUnavailableBeatmapPanel>()
            .Any(panel => panel.Item?.Model is GroupedBeatmap grouped && grouped.Beatmap.MD5Hash == missing_hash));
        AddAssert("missing entry panel marks unavailable", () => carousel.ChildrenOfType<BmsUnavailableBeatmapPanel>()
            .Single(panel => panel.Item?.Model is GroupedBeatmap grouped && grouped.Beatmap.MD5Hash == missing_hash)
            .ChildrenOfType<SpriteIcon>()
            .Any(icon => icon.Icon.Equals(FontAwesome.Solid.ExclamationTriangle)));
        AddAssert("missing entry warning is visible", () => carousel.ChildrenOfType<BmsUnavailableBeatmapPanel>()
            .Single(panel => panel.Item?.Model is GroupedBeatmap grouped && grouped.Beatmap.MD5Hash == missing_hash)
            .ChildrenOfType<SpriteIcon>()
            .Any(icon => icon.Icon.Equals(FontAwesome.Solid.ExclamationTriangle) && icon.Alpha > 0 && icon.DrawWidth > 0 && icon.DrawHeight > 0));
        AddAssert("missing entry warning is red", () => carousel.ChildrenOfType<BmsUnavailableBeatmapPanel>()
            .Single(panel => panel.Item?.Model is GroupedBeatmap grouped && grouped.Beatmap.MD5Hash == missing_hash)
            .ChildrenOfType<SpriteIcon>()
            .Where(icon => icon.Icon.Equals(FontAwesome.Solid.ExclamationTriangle))
            .Select(icon => icon.Colour)
            .Any(colour => colour == Color4.Red));
        AddStep("activate missing entry", () => carousel.Activate(carousel.GetCarouselItems()!
            .Single(item => item.Model is GroupedBeatmap grouped && grouped.Beatmap.MD5Hash == missing_hash)));
        AddAssert("missing entry does not replace working beatmap", () => Beatmap.IsDefault);
        AddStep("confirm missing entry", () => carousel.Activate(carousel.GetCarouselItems()!
            .Single(item => item.Model is GroupedBeatmap grouped && grouped.Beatmap.MD5Hash == missing_hash)));
        AddAssert("confirm does not start gameplay", () => Stack.CurrentScreen == songSelect && Beatmap.IsDefault);
        AddStep("restore table store", () => BmsRulesetRuntime.DifficultyTableStore = previousStore);
    }

    [Test]
    public void TestImportedDifficultyTableBeatmapIsNotDuplicated()
    {
        DifficultyTableStore previousStore = null!;
        BeatmapInfo importedBeatmap = null!;

        AddStep("import matching BMS beatmap", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(r => r.ShortName == Constant.SHORT_NAME);
            importedBeatmap = beatmaps.Import(createBeatmapSet(bmsRuleset))!.Value.Beatmaps.First().Detach();

            previousStore = BmsRulesetRuntime.DifficultyTableStore;
            var store = new DifficultyTableStore(null, Path.Combine(LocalStorage.GetFullPath(string.Empty), "available-table-carousel-test"));
            store.RestoreTable(new DifficultyTable.DifficultyTable
            {
                Name = "Test",
                Symbol = "T",
                Entries = [new TableEntry { Level = "1", Md5Hash = importedBeatmap.MD5Hash, Title = "Imported" }],
            });
            BmsRulesetRuntime.DifficultyTableStore = store;
            Beatmap.Value = beatmaps.GetWorkingBeatmap(importedBeatmap, true);
        });
        AddUntilStep("wait for imported beatmap", () => beatmaps.GetAllUsableBeatmapSets()
            .SelectMany(set => set.Beatmaps)
            .Any(beatmap => beatmap.MD5Hash == importedBeatmap.MD5Hash));
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select presentation", () => Stack.CurrentScreen == songSelect && songSelect.CarouselItemsPresented && !songSelect.IsFiltering);
        AddAssert("matching beatmap appears once", () => carousel.GetCarouselItems()!
            .Select(item => item.Model)
            .OfType<GroupedBeatmap>()
            .Count(grouped => grouped.Beatmap.MD5Hash == importedBeatmap.MD5Hash), () => Is.EqualTo(1));
        AddStep("restore table store", () => BmsRulesetRuntime.DifficultyTableStore = previousStore);
    }

    [Test]
    public void TestRankFollowsSelectedMods()
    {
        BeatmapInfo beatmap = null!;
        BeatmapSetInfo rankBeatmapSet = null!;

        AddStep("import a beatmap with ranked scores", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(r => r.ShortName == Constant.SHORT_NAME);
            var imported = beatmaps.Import(createBeatmapSet(bmsRuleset));

            Assert.That(imported, Is.Not.Null);
            rankBeatmapSet = imported!.Value.Detach();
            beatmap = rankBeatmapSet.Beatmaps.First();

            // The no-mod score holds a lower rank; the modded one holds the highest.
            Assert.That(scoreManager.Import(createScore(beatmap, ScoreRank.A, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)))), Is.Not.Null);
            Assert.That(scoreManager.Import(createScore(beatmap, ScoreRank.S, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModHideScratch())), Is.Not.Null);
        });
        AddUntilStep("wait for scores", () => Realm.Run(r =>
            r.All<ScoreInfo>().AsEnumerable().Count(score => score.BeatmapHash == beatmap.Hash && !score.DeletePending)), () => Is.EqualTo(2));
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for carousel presentation", () => songSelect.CarouselItemsPresented && !songSelect.IsFiltering);
        AddStep("scope to rank beatmap set", () => songSelect.ScopeToBeatmapSet(rankBeatmapSet));
        AddUntilStep("wait for scoped carousel", () => !songSelect.IsFiltering
                                                       && carousel.Criteria?.SelectedBeatmapSet != null
                                                       && carousel.Criteria.SelectedBeatmapSet.Equals(rankBeatmapSet));
        AddUntilStep("rank panel realised", () => rankDisplayFor(beatmap) != null);
        AddUntilStep("no-mod rank shown", () => rankDisplayFor(beatmap)!.ChildrenOfType<UpdateableRank>().Single().Rank, () => Is.EqualTo(ScoreRank.A));
        AddStep("select hide scratch", () => songSelect.Mods.Value = [new BmsModHideScratch()]);
        AddUntilStep("rank follows selected mods", () => rankDisplayFor(beatmap)!.ChildrenOfType<UpdateableRank>().Single().Rank, () => Is.EqualTo(ScoreRank.S));
    }

    private PanelLocalRankDisplay rankDisplayFor(BeatmapInfo beatmap) =>
        carousel.ChildrenOfType<PanelLocalRankDisplay>().SingleOrDefault(display => display.Beatmap?.Hash == beatmap.Hash);

    private void importLampBeatmapSet()
    {
        AddStep("import one BMS set with all lamp difficulties", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(r => r.ShortName == Constant.SHORT_NAME);
            var imported = beatmaps.Import(createBeatmapSet(bmsRuleset));

            Assert.That(imported, Is.Not.Null);
            lampBeatmapSet = imported!.Value.Detach();

            Beatmap.Value = beatmaps.GetWorkingBeatmap(lampBeatmapSet.Beatmaps[all_lamps.Length / 2], true);
        });

        AddUntilStep("wait for lamp beatmap set", () =>
            beatmaps.GetAllUsableBeatmapSets().Any(set => set.OnlineID == lampBeatmapSet.OnlineID && set.Beatmaps.Count == all_lamps.Length));
    }

    private void importLampScores()
    {
        AddStep("import one score per played lamp", () =>
        {
            foreach (var (lamp, beatmap) in beatmapsForLamps())
            {
                if (lamp == BmsLamp.NoPlay)
                    continue;

                Assert.That(scoreManager.Import(createScoreForLamp(beatmap, lamp)), Is.Not.Null);
            }
        });

        AddUntilStep("wait for lamp scores", () => Realm.Run(r =>
        {
            var hashes = lampBeatmapSet.Beatmaps.Select(b => b.Hash).ToArray();
            return r.All<ScoreInfo>().AsEnumerable().Count(score => hashes.Contains(score.BeatmapHash) && !score.DeletePending);
        }), () => Is.EqualTo(all_lamps.Length - 1));
    }

    private void loadSongSelect()
    {
        AddStep("load real song select", () => Stack.Push(songSelect = new BmsSoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for carousel presentation", () => songSelect.CarouselItemsPresented && !songSelect.IsFiltering);
        AddStep("scope to lamp beatmap set", () => songSelect.ScopeToBeatmapSet(lampBeatmapSet));
        AddUntilStep("wait for scoped carousel", () => !songSelect.IsFiltering
                                                       && carousel.Criteria?.SelectedBeatmapSet != null
                                                       && carousel.Criteria.SelectedBeatmapSet.Equals(lampBeatmapSet)
                                                       && carousel.MatchedBeatmapsCount == all_lamps.Length);
    }

    private IEnumerable<(BmsLamp lamp, BeatmapInfo beatmap)> beatmapsForLamps() =>
        all_lamps.Zip(lampBeatmapSet.Beatmaps.OrderBy(b => b.OnlineID), (lamp, beatmap) => (lamp, beatmap));

    private IEnumerable<Panel> lampPanels() =>
        carousel.ChildrenOfType<Panel>()
            .Where(panel => panel.ChildrenOfType<PanelLocalRankDisplay>().Any(display => isLampBeatmap(display.Beatmap)));

    private IEnumerable<PanelLocalRankDisplay> lampRankDisplays() =>
        lampPanels().Select(panel => panel.ChildrenOfType<PanelLocalRankDisplay>().Single());

    private IEnumerable<PanelLocalRankDisplay> playedRankDisplays() =>
        lampRankDisplays().Where(display => display.Beatmap != null && lampForBeatmap(display.Beatmap) != BmsLamp.NoPlay);

    private PanelLocalRankDisplay noPlayRankDisplay() =>
        lampRankDisplays().SingleOrDefault(display => display.Beatmap != null && lampForBeatmap(display.Beatmap) == BmsLamp.NoPlay);

    private IEnumerable<BmsLampDisplay> visibleLampDisplays() =>
        lampPanels().SelectMany(panel => panel.ChildrenOfType<BmsLampDisplay>().Where(lamp => lamp.Alpha > 0));

    private bool isLampBeatmap(BeatmapInfo beatmap) =>
        beatmap != null && lampBeatmapSet.Beatmaps.Any(b => b.Hash == beatmap.Hash);

    private BmsLamp lampForBeatmap(BeatmapInfo beatmap)
    {
        foreach (var (lamp, lampBeatmap) in beatmapsForLamps())
        {
            if (lampBeatmap.Hash == beatmap.Hash)
                return lamp;
        }

        throw new InvalidOperationException($"Beatmap {beatmap} is not part of the lamp set.");
    }

    private static bool hasVisibleStockRank(PanelLocalRankDisplay display) =>
        display.ChildrenOfType<UpdateableRank>().Any(rank => rank.Rank != null && rank.Alpha > 0);

    private static bool hasHiddenRulesetMark(PanelLocalRankDisplay display)
    {
        var iconContainer = getIconContainer(parentPanel(display));
        return iconContainer != null && iconContainer.Alpha == 0;
    }

    private static bool hasNoStockRank(PanelLocalRankDisplay display) => !hasVisibleStockRank(display);

    private static Panel parentPanel(Drawable drawable)
    {
        for (Drawable current = drawable; current != null; current = current.Parent)
        {
            if (current is Panel panel)
                return panel;
        }

        return null!;
    }

    private static readonly FieldInfo icon_container_field =
        typeof(Panel).GetField("iconContainer", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static Drawable getIconContainer(Panel panel) => icon_container_field.GetValue(panel) as Drawable;

    private static readonly FieldInfo background_container_field =
        typeof(Panel).GetField("backgroundContainer", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static Drawable getBackgroundContainer(Panel panel) => background_container_field.GetValue(panel) as Drawable;

    private static readonly FieldInfo lamp_field =
        typeof(BmsLampDisplay).GetField("lamp", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static BmsLamp lampFromDisplay(BmsLampDisplay display) => (BmsLamp)lamp_field.GetValue(display)!;

    [Test]
    public void TestLampColoursMatchBeatoraja()
    {
        AddStep("clear previous lamp displays", () =>
        {
            foreach (var display in lampDisplays())
                display.Expire();
        });

        AddStep("create a lamp display per state", () =>
        {
            foreach (var lamp in Enum.GetValues<BmsLamp>())
                Add(new BmsLampDisplay(lamp));
        });

        foreach (var (lampValue, expectedBase, expectedFlash) in new[]
                 {
                     (BmsLamp.NoPlay, new Color4(40, 44, 48, 255), Color4.Transparent),
                     (BmsLamp.Failed, new Color4(233, 47, 10, 255), new Color4(15, 3, 0, 255)),
                     (BmsLamp.AssistClear, new Color4(206, 1, 214, 255), Color4.Transparent),
                     (BmsLamp.LightAssistClear, new Color4(221, 162, 223, 255), Color4.Transparent),
                     (BmsLamp.EasyClear, new Color4(86, 202, 67, 255), Color4.Transparent),
                     (BmsLamp.Clear, new Color4(245, 199, 88, 255), Color4.Transparent),
                     (BmsLamp.HardClear, new Color4(248, 247, 245, 255), Color4.Transparent),
                     (BmsLamp.ExHardClear, new Color4(239, 253, 9, 255), new Color4(253, 9, 9, 255)),
                     (BmsLamp.FullCombo, new Color4(255, 255, 255, 255), new Color4(9, 250, 253, 255)),
                     (BmsLamp.Perfect, new Color4(255, 255, 255, 255), new Color4(63, 255, 77, 255)),
                     (BmsLamp.Max, new Color4(255, 255, 255, 255), new Color4(255, 235, 66, 255)),
                 })
        {
            AddAssert($"{lampValue} base colour matches beatoraja", () =>
            {
                var display = lampDisplays().Single(d => d.Lamp == lampValue);
                return getBaseFill(display).Colour == expectedBase;
            });
            AddAssert($"{lampValue} flash colour matches beatoraja", () =>
            {
                var display = lampDisplays().Single(d => d.Lamp == lampValue);
                return getFlashFill(display).Colour == expectedFlash;
            });
        }
    }

    [Test]
    public void TestLampFlashAlphaFollowsBeatorajaCycle()
    {
        AddStep("clear previous lamp displays", () =>
        {
            foreach (var display in lampDisplays())
                display.Expire();
        });

        AddStep("create lamp displays", () =>
        {
            Add(new BmsLampDisplay(BmsLamp.Failed));
            Add(new BmsLampDisplay(BmsLamp.ExHardClear));
            Add(new BmsLampDisplay(BmsLamp.FullCombo));
            Add(new BmsLampDisplay(BmsLamp.Perfect));
            Add(new BmsLampDisplay(BmsLamp.Max));
            Add(new BmsLampDisplay(BmsLamp.Clear));
        });

        AddStep("take control of the lamp clock", () =>
        {
            Content.Clock = new FramedClock(new ManualClock { CurrentTime = 0 });
        });

        AddAssert("failed lamp alternates at the 60 ms cycle", () =>
            cycleMatches(lampDisplays().Single(d => d.Lamp == BmsLamp.Failed), 60));
        AddAssert("exhard lamp alternates at the 60 ms cycle", () =>
            cycleMatches(lampDisplays().Single(d => d.Lamp == BmsLamp.ExHardClear), 60));
        AddAssert("full combo lamp alternates at the 60 ms cycle", () =>
            cycleMatches(lampDisplays().Single(d => d.Lamp == BmsLamp.FullCombo), 60));
        AddAssert("perfect lamp alternates at the 60 ms cycle", () =>
            cycleMatches(lampDisplays().Single(d => d.Lamp == BmsLamp.Perfect), 60));
        AddAssert("max lamp alternates at the 60 ms cycle", () =>
            cycleMatches(lampDisplays().Single(d => d.Lamp == BmsLamp.Max), 60));
        AddAssert("clear lamp never flashes", () =>
        {
            var display = lampDisplays().Single(d => d.Lamp == BmsLamp.Clear);

            foreach (var time in new[] { 30, 60, 90, 120, 150, 180, 210, 240 })
            {
                if (flashAlphaAt(display, time) > 0.5f)
                    return false;
            }

            return true;
        });
    }

    // Sets the content clock to the given time and runs one Update frame so the flash
    // pulse can be sampled deterministically instead of racing the real frame loop.
    private float flashAlphaAt(BmsLampDisplay display, double time)
    {
        ((ManualClock)((FramedClock)Content.Clock!).Source!).CurrentTime = time;
        Content.UpdateSubTree();
        return getFlashFill(display).Alpha;
    }

    private bool cycleMatches(BmsLampDisplay display, double cycle)
    {
        var on = flashAlphaAt(display, cycle / 4);
        var off = flashAlphaAt(display, cycle * 3 / 4);
        var onAgain = flashAlphaAt(display, cycle * 5 / 4);

        return on > 0.5f && off < 0.5f && onAgain > 0.5f;
    }

    private IEnumerable<BmsLampDisplay> lampDisplays() => this.ChildrenOfType<BmsLampDisplay>();

    private static readonly FieldInfo base_fill_field =
        typeof(BmsLampDisplay).GetField("baseFill", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo flash_fill_field =
        typeof(BmsLampDisplay).GetField("flashFill", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static Box getBaseFill(BmsLampDisplay display) => (Box)base_fill_field.GetValue(display)!;

    private static Box getFlashFill(BmsLampDisplay display) => (Box)flash_fill_field.GetValue(display)!;

    private ScoreInfo createScoreForLamp(BeatmapInfo beatmap, BmsLamp lamp) => lamp switch
    {
        BmsLamp.Failed => createScore(beatmap, ScoreRank.F, stats((HitResult.Perfect, 50))),
        BmsLamp.Max => createScore(beatmap, ScoreRank.X, stats((HitResult.Perfect, 50))),
        BmsLamp.Perfect => createScore(beatmap, ScoreRank.S, stats((HitResult.Perfect, 49), (HitResult.Great, 1))),
        BmsLamp.FullCombo => createScore(beatmap, ScoreRank.A, stats((HitResult.Perfect, 48), (HitResult.Great, 1), (HitResult.Good, 1))),
        BmsLamp.LightAssistClear => createScore(beatmap, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModAssistEasyGauge()),
        BmsLamp.EasyClear => createScore(beatmap, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModEasyGauge()),
        BmsLamp.Clear => createScore(beatmap, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1))),
        BmsLamp.HardClear => createScore(beatmap, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModHardGauge()),
        BmsLamp.ExHardClear => createScore(beatmap, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModExHardGauge()),
        _ => throw new ArgumentOutOfRangeException(nameof(lamp)),
    };

    private ScoreInfo createScore(BeatmapInfo beatmap, ScoreRank rank, Dictionary<HitResult, int> statistics, params Mod[] mods) => new()
    {
        User = API.LocalUser.Value,
        BeatmapInfo = beatmap,
        BeatmapHash = beatmap.Hash,
        Ruleset = beatmap.Ruleset,
        Rank = rank,
        Mods = mods,
        TotalScore = (long)(((double)rank + 1) / (Enum.GetValues<ScoreRank>().Length + 1) * 1000000),
        Date = DateTimeOffset.Now,
        Accuracy = 1,
        MaxCombo = 1,
        Statistics = statistics,
    };

    private static Dictionary<HitResult, int> stats(params (HitResult result, int count)[] entries)
    {
        var statistics = new Dictionary<HitResult, int>();

        foreach (var (result, count) in entries)
            statistics[result] = count;

        return statistics;
    }

    private static BeatmapSetInfo createBeatmapSet(RulesetInfo ruleset)
    {
        var id = nextTestId++;

        var metadata = new BeatmapMetadata
        {
            Artist = "BMS Test Artist",
            Title = $"BMS Song Select Lamps {id}",
            Author =
            {
                Username = "BMS Test Author",
            },
        };

        var set = new BeatmapSetInfo
        {
            OnlineID = id + 1,
            Hash = Guid.NewGuid().ToString(),
            DateAdded = DateTimeOffset.UtcNow,
        };

        for (var i = 0; i < all_lamps.Length; i++)
        {
            var hash = Guid.NewGuid().ToString();

            set.Beatmaps.Add(new BeatmapInfo(ruleset)
            {
                OnlineID = (id + 1) * 1000 + i,
                BeatmapSet = set,
                DifficultyName = $"{i:00} {all_lamps[i]}",
                StarRating = 1 + i * 0.5,
                Length = 30000,
                BPM = 150,
                Hash = hash,
                MD5Hash = hash,
                Metadata = metadata,
                Difficulty = new BeatmapDifficulty
                {
                    OverallDifficulty = 8,
                },
            });
        }

        return set;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        rulesets?.Dispose();
    }
}
