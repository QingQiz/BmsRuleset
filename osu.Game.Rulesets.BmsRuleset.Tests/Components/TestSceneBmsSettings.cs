using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.Settings.Components;
using osu.Game.Tests.Visual;
using DT = osu.Game.Rulesets.BmsRuleset.DifficultyTable.DifficultyTable;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Components;

[TestFixture]
public partial class TestSceneBmsSettings : OsuTestScene
{
    [Cached]
    private OverlayColourProvider colourProvider = new(OverlayColourScheme.Purple);

    private TemporaryNativeStorage storage = null!;
    private DifficultyTableStore previousStore = null!;
    private OsuScrollContainer scroll = null!;
    private Drawable settings = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        storage = new TemporaryNativeStorage($"{nameof(TestSceneBmsSettings)}-{Guid.NewGuid()}");
        previousStore = BmsRulesetRuntime.DifficultyTableStore;
        BmsRulesetRuntime.VisualOffsetSuggestions.Clear();

        var syncManager = new CollectionSyncManager();
        var store = new DifficultyTableStore(null, storage.GetFullPath("difficulty-tables"), syncManager);
        var remoteTable = createRemoteTable();
        store.RestoreTable(remoteTable);
        store.RestoreTable(createLocalTable());
        BmsRulesetRuntime.DifficultyTableStore = store;
        syncManager.ToggleSubdivide(null, remoteTable);

        var ruleset = new BmsRuleset();
        settings = ruleset.CreateSettings();

        Add(new PopoverContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = scroll = new OsuScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Children = [settings],
                },
            },
        });
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);

        BmsRulesetRuntime.DifficultyTableStore = previousStore;
        BmsRulesetRuntime.VisualOffsetSuggestions.Clear();
        storage.Dispose();
    }

    [Test]
    public void TestShow()
    {
        AddStep("visible", () => { });
    }

    [Test]
    public void TestDifficultyTables()
    {
        AddAssert("table rows have drawable size", () => settings.ChildrenOfType<OsuClickableContainer>()
            .Where(header => header.Name.StartsWith("Difficulty table header", StringComparison.Ordinal))
            .All(header => header.DrawWidth > 0 && header.DrawHeight > 0));
        AddAssert("table rows show subdivision status", () => settings.ChildrenOfType<OsuClickableContainer>()
            .Where(header => header.Name.StartsWith("Difficulty table header", StringComparison.Ordinal))
            .All(header => header.ChildrenOfType<SpriteText>()
                .Any(text => text.Name.StartsWith("Difficulty table subdivision status", StringComparison.Ordinal))));
        AddStep("expand table actions", () =>
        {
            getTableHeader("Satellite Difficulty Table").TriggerClickWithSound();
            getTableHeader("Local Practice Table with a long name that wraps across multiple lines").TriggerClickWithSound();
        });
        AddStep("scroll to difficulty tables", () => scroll.ScrollTo(getTableHeader("Satellite Difficulty Table"), false));
    }

    [Test]
    public void TestVisualOffsetSuggestionCanBeApplied()
    {
        VisualOffsetAdjustControl control = null!;

        AddStep("get visual offset control", () => control = settings.ChildrenOfType<VisualOffsetAdjustControl>().Single());
        AddStep("add visual offset suggestion", () => BmsRulesetRuntime.VisualOffsetSuggestions.Add(20, 0));
        AddUntilStep("suggestion is displayed", () => control.SuggestedOffset.Value == 20);
        AddStep("apply suggestion", () => control.ChildrenOfType<RoundedButton>().Single().TriggerClick());
        AddAssert("visual offset is updated", () => control.Current.Value == 20);
        AddAssert("suggestion history is cleared", () => BmsRulesetRuntime.VisualOffsetSuggestions.History.Count == 0);
    }

    private OsuClickableContainer getTableHeader(string tableName) => settings.ChildrenOfType<OsuClickableContainer>()
        .Single(header => header.Name == $"Difficulty table header ({tableName})");

    private static DT createRemoteTable() => new()
    {
        Name = "Satellite Difficulty Table",
        Symbol = "sl",
        Source = TableSource.RemoteUrl,
        SourcePath = "https://example.com/satellite/header.json",
        LevelOrder = ["0", "1", "2"],
        Entries =
        [
            new TableEntry { Level = "0", Md5Hash = "00000000000000000000000000000001" },
            new TableEntry { Level = "0", Md5Hash = "00000000000000000000000000000002" },
            new TableEntry { Level = "1", Md5Hash = "00000000000000000000000000000003" },
            new TableEntry { Level = "1", Md5Hash = "00000000000000000000000000000004" },
            new TableEntry { Level = "1", Md5Hash = "00000000000000000000000000000005" },
            new TableEntry { Level = "2", Md5Hash = "00000000000000000000000000000006" },
        ],
    };

    private static DT createLocalTable() => new()
    {
        Name = "Local Practice Table with a long name that wraps across multiple lines",
        Symbol = "LP",
        Source = TableSource.LocalFile,
        SourcePath = "local-practice-table.json",
        LevelOrder = ["Beginner", "Advanced"],
        Entries =
        [
            new TableEntry { Level = "Beginner", Md5Hash = "00000000000000000000000000000007" },
            new TableEntry { Level = "Advanced", Md5Hash = "00000000000000000000000000000008" },
        ],
    };
}

[TestFixture]
public partial class TestSceneBmsFileImportScreen : ScreenTestScene
{
    private BmsFileImportScreen importScreen = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
    }

    [Test]
    public void TestNavigate()
    {
        AddStep("load screen", () => LoadScreen(importScreen = new BmsFileImportScreen()));
        AddUntilStep("wait for load", () => importScreen.IsLoaded);
    }

    [Test]
    public void TestShow()
    {
        AddStep("load screen", () => LoadScreen(importScreen = new BmsFileImportScreen()));
    }
}
