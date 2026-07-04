using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.BmsRuleset.Mods.Gauge;
using osu.Game.Rulesets.BmsRuleset.SongSelect;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
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
    private osu.Game.Screens.Select.SongSelect songSelect = null!;

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
            BmsSongSelectLampPatcher.InstallOnce();

            Ruleset.Value = rulesets.AvailableRulesets.Single(r => r.ShortName == "bms");
            Beatmap.SetDefault();
            SelectedMods.SetDefault();

            config.SetValue(OsuSetting.SongSelectSortingMode, SortMode.Title);
            config.SetValue(OsuSetting.SongSelectGroupMode, GroupMode.None);

            songSelect = null!;
        });

        AddStep("delete all beatmaps", () => beatmaps.Delete());
    }

    [Test]
    public void TestAllLamps()
    {
        AddStep("import 9 BMS beatmaps", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(r => r.ShortName == "bms");
            for (var i = 0; i < Enum.GetValues<BmsLamp>().Length; i++)
                beatmaps.Import(createBeatmapSet(bmsRuleset));
        });

        AddUntilStep("wait for lamp beatmaps", () => beatmaps.GetAllUsableBeatmapSets().SelectMany(s => s.Beatmaps).Count() >= Enum.GetValues<BmsLamp>().Length);

        AddStep("import one score per played lamp", () =>
        {
            var lamps = Enum.GetValues<BmsLamp>();
            var allBeatmaps = beatmaps.GetAllUsableBeatmapSets().SelectMany(s => s.Beatmaps).ToList();
            for (var i = 0; i < lamps.Length; i++)
            {
                if (lamps[i] != BmsLamp.NoPlay)
                    scoreManager.Import(createScoreForLamp(allBeatmaps[i], lamps[i]));
            }
        });

        loadSongSelect();

        AddAssert("patch installed", () => BmsSongSelectLampPatcher.IsInstalled);
        AddUntilStep("BMS ruleset active", () => Ruleset.Value.ShortName == "bms");
        AddUntilStep("real local rank display loaded", () => localRankDisplays().Any());
        // The carousel only realises visible panels, so this asserts at least one lamp renders;
        // scroll the test browser to inspect every lamp state.
        AddUntilStep("real panel has BMS lamp", () => panelWithVisibleLamp() != null);
        AddAssert("stock rank still visible beside lamp", () => localRankDisplays().Any(hasVisibleStockRank));
        AddAssert("ruleset mark hidden by lamp", () => localRankDisplays().Any(hasHiddenRulesetMark));
        AddAssert("lamp rendered as panel background", () =>
        {
            var panel = panelWithVisibleLamp();
            var lampDrawable = panel?.ChildrenOfType<BmsLampDisplay>().SingleOrDefault(l => l.Alpha > 0);

            return panel != null && lampDrawable != null && getBackgroundContainer(panel) == lampDrawable.Parent;
        });
        AddAssert("lamp fills the song select card", () =>
        {
            var panel = panelWithVisibleLamp();
            var lampDrawable = panel?.ChildrenOfType<BmsLampDisplay>().SingleOrDefault(l => l.Alpha > 0);

            return panel != null && lampDrawable != null
                                 && Math.Abs(lampDrawable.ScreenSpaceDrawQuad.Width - panel.TopLevelContent.ScreenSpaceDrawQuad.Width) < 0.5f
                                 && Math.Abs(lampDrawable.ScreenSpaceDrawQuad.Height - panel.TopLevelContent.ScreenSpaceDrawQuad.Height) < 0.5f;
        });
    }

    [Test]
    public void TestUnplayedBeatmapHasGrayLamp()
    {
        AddStep("import BMS beatmap with no score", () =>
        {
            var bmsRuleset = rulesets.AvailableRulesets.Single(r => r.ShortName == "bms");
            beatmaps.Import(createBeatmapSet(bmsRuleset));
        });

        AddUntilStep("wait for beatmap", () => beatmaps.GetAllUsableBeatmapSets().SelectMany(s => s.Beatmaps).Any());

        loadSongSelect();

        AddAssert("patch installed", () => BmsSongSelectLampPatcher.IsInstalled);
        AddUntilStep("BMS ruleset active", () => Ruleset.Value.ShortName == "bms");
        AddUntilStep("real local rank display loaded", () => localRankDisplays().Any());
        AddAssert("BMS lamp visible on unplayed panel", () => panelWithVisibleLamp() != null);
        AddAssert("ruleset mark hidden by no-play lamp", () => localRankDisplays().Any(hasHiddenRulesetMark));
        AddAssert("no stock rank on unplayed panel", () => localRankDisplays().Any(hasNoStockRank));
    }

    private void loadSongSelect()
    {
        AddStep("load real song select", () => Stack.Push(songSelect = new SoloSongSelect()));
        AddUntilStep("wait for song select load", () => Stack.CurrentScreen == songSelect && songSelect.IsLoaded);
        AddUntilStep("wait for carousel filtering", () => !carousel.IsFiltering);
    }

    private IEnumerable<PanelLocalRankDisplay> localRankDisplays() => carousel.ChildrenOfType<PanelLocalRankDisplay>();

    private Panel panelWithVisibleLamp() =>
        carousel.ChildrenOfType<Panel>().FirstOrDefault(panel => panel.ChildrenOfType<BmsLampDisplay>().Any(lamp => lamp.Alpha > 0));

    private static bool hasVisibleStockRank(PanelLocalRankDisplay display) =>
        display.ChildrenOfType<UpdateableRank>().Any(rank => rank.Rank != null && rank.Alpha > 0);

    private static bool hasHiddenRulesetMark(PanelLocalRankDisplay display) =>
        getIconContainer(parentPanel(display))?.Alpha == 0;

    private static bool hasVisibleRulesetMark(PanelLocalRankDisplay display) =>
        getIconContainer(parentPanel(display))?.Alpha > 0;

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

    // Maps each lamp to a score whose Rank/Statistics/Mods BmsLampCalculator resolves to that lamp.
    // Gauge lamps include HitResult.Ok so the clear-quality checks (MAX/PERFECT/FULL COMBO) don't
    // override the gauge-derived lamp.
    private ScoreInfo createScoreForLamp(BeatmapInfo beatmap, BmsLamp lamp) => lamp switch
    {
        BmsLamp.Failed => createScore(beatmap, ScoreRank.F, stats((HitResult.Perfect, 50))),
        BmsLamp.Max => createScore(beatmap, ScoreRank.X, stats((HitResult.Perfect, 50))),
        BmsLamp.Perfect => createScore(beatmap, ScoreRank.S, stats((HitResult.Perfect, 49), (HitResult.Great, 1))),
        BmsLamp.FullCombo => createScore(beatmap, ScoreRank.A, stats((HitResult.Perfect, 48), (HitResult.Great, 1), (HitResult.Good, 1))),
        BmsLamp.AssistClear => createScore(beatmap, ScoreRank.D, stats((HitResult.Perfect, 1), (HitResult.Ok, 1)), new BmsModAssistEasyGauge()),
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
        var hash = Guid.NewGuid().ToString();

        var metadata = new BeatmapMetadata
        {
            Artist = "BMS Test Artist",
            Title = $"BMS Song Select Lamp {id}",
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

        set.Beatmaps.Add(new BeatmapInfo(ruleset)
        {
            OnlineID = (id + 1) * 1000,
            BeatmapSet = set,
            DifficultyName = "Real Song Select",
            StarRating = 5,
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

        return set;
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        rulesets?.Dispose();
    }
}
