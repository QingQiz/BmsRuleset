using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Timing;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Configuration;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.DifficultyTable;
using osu.Game.Rulesets.BmsRuleset.IO.Import;
using osu.Game.Rulesets.BmsRuleset.UI.Settings.Components;
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
    private SearchContainer search = null!;

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
                Child = search = new SearchContainer
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
    public void TestSoftConstantSliderVisibilityAndValueRetention()
    {
        SettingsItemV2 fadeInItem = null!;
        SettingsItemV2 toggleItem = null!;
        ExpandableSettingsGroup expandable = null!;
        BmsRulesetConfigManager config = null!;

        AddStep("get configuration", () => config = (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset()));
        AddStep("find fade-in slider", () => fadeInItem = settings.ChildrenOfType<SettingsItemV2>()
            .Single(item => item.Control is FormSliderBar<double> slider && slider.Caption.Equals(BmsStrings.SoftConstantFadeIn)));
        AddStep("find slider group", () => expandable = settings.ChildrenOfType<ExpandableSettingsGroup>().Single());
        AddStep("find toggle", () => toggleItem = expandable.ChildrenOfType<SettingsItemV2>().Single(item => item.Control is FormCheckBox));
        AddStep("disable soft constant", () => config.SetValue(BmsRulesetSetting.SoftConstant, false));
        AddUntilStep("slider hidden", () => !fadeInItem.IsPresent);
        AddStep("enable soft constant", () => config.SetValue(BmsRulesetSetting.SoftConstant, true));
        AddUntilStep("slider visible", () => fadeInItem.IsPresent);
        AddStep("adjust fade-in", () => ((FormSliderBar<double>)fadeInItem.Control).Current.Value = -150);
        AddAssert("setting saved", () => config.Get<double>(BmsRulesetSetting.SoftConstantFadeIn) == -150);
        AddStep("disable again", () => config.SetValue(BmsRulesetSetting.SoftConstant, false));
        AddUntilStep("slider hidden again", () => !fadeInItem.IsPresent);
        AddStep("search for hidden slider", () => search.SearchTerm = "fade-in");
        AddUntilStep("search respects toggle", () => !fadeInItem.MatchingFilter);
        AddStep("enable while searching", () => config.SetValue(BmsRulesetSetting.SoftConstant, true));
        AddUntilStep("slider reappears in search", () => fadeInItem.IsPresent);
        AddUntilStep("group visible in search", () => expandable.IsPresent && expandable.ExpandableContent.IsPresent && expandable.ExpandableContent.Height > 0);
        AddUntilStep("slider-only search has no leading gap", () => !toggleItem.IsPresent && expandable.ExpandableContent.Padding.Top == 0
            && Math.Abs(expandable.Height - fadeInItem.Height) < 0.01f);
        AddAssert("value retained", () => ((FormSliderBar<double>)fadeInItem.Control).Current.Value == -150);
        AddStep("search for toggle only", () => search.SearchTerm = "fixed");
        AddUntilStep("toggle-only search leaves no slider gap", () => toggleItem.IsPresent && !fadeInItem.IsPresent
            && Math.Abs(expandable.Height - toggleItem.Height) < 0.01f);
        AddStep("clear search", () => search.SearchTerm = string.Empty);
        AddUntilStep("full group spacing restored", () => Math.Abs(expandable.Height - toggleItem.Height - fadeInItem.Height - SettingsSection.ITEM_SPACING_V2) < 0.01f);
        AddStep("search for other setting", () => search.SearchTerm = "judgement");
        AddUntilStep("unmatched slider leaves no gap", () => !expandable.IsPresent && !fadeInItem.MatchingFilter);
        AddStep("restore defaults", () =>
        {
            search.SearchTerm = string.Empty;
            config.SetValue(BmsRulesetSetting.SoftConstant, false);
            config.SetValue(BmsRulesetSetting.SoftConstantFadeIn, 0.0);
        });
    }

    [Test]
    public void TestSoftConstantSliderAnimation()
    {
        var clock = new ManualClock();
        IFrameBasedClock originalClock = null!;
        ExpandableSettingsGroup expandable = null!;
        SettingsItemV2 fadeInItem = null!;
        OsuSpriteText followingHeader = null!;
        BmsRulesetConfigManager config = null!;
        var heightBeforeReversal = 0f;
        var headerYBeforeCompletion = 0f;
        var headerYAfterCompletion = 0f;
        var headerYDuringCollapse = 0f;

        AddStep("find animated slider", () =>
        {
            config = (BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset());
            expandable = settings.ChildrenOfType<ExpandableSettingsGroup>().Single();
            fadeInItem = expandable.ChildrenOfType<SettingsItemV2>().Single(item => item.Control is FormSliderBar<double>);
            followingHeader = settings.ChildrenOfType<OsuSpriteText>().Single(text => text.Text.Equals(BmsStrings.BmsVisualOffset));
            search.SearchTerm = string.Empty;
            scroll.ScrollToStart(false);
            config.SetValue(BmsRulesetSetting.SoftConstant, false);
        });
        AddUntilStep("initially fully hidden", () => expandable.IsPresent && expandable.ExpandableContent.Height == 0 && !fadeInItem.CanBeShown.Value);
        AddStep("use manual animation clock", () =>
        {
            originalClock = expandable.ExpandableContent.Clock;
            expandable.ExpandableContent.Clock = new FramedClock(clock);
        });
        AddStep("enable soft constant", () => config.SetValue(BmsRulesetSetting.SoftConstant, true));
        AddStep("begin expansion layout", () => clock.CurrentTime += 1);
        AddStep("advance expansion", () => clock.CurrentTime += 49);
        AddAssert("slider partially expanded", () => expandable.ExpandableContent.Height > 0 && expandable.ExpandableContent.Height < fadeInItem.Height + SettingsSection.ITEM_SPACING_V2);
        AddStep("finish expansion", () => clock.CurrentTime += 300);
        AddUntilStep("fully expanded", () => expandable.ExpandableContent.Height,
            () => Is.EqualTo(fadeInItem.Height + SettingsSection.ITEM_SPACING_V2).Within(0.01));
        AddAssert("slider still occupies layout", () => expandable.IsPresent);

        AddStep("disable soft constant", () => config.SetValue(BmsRulesetSetting.SoftConstant, false));
        AddAssert("still visible at collapse start", () => fadeInItem.IsPresent && expandable.IsPresent);
        AddStep("advance collapse", () => clock.CurrentTime += 50);
        AddAssert("slider partially collapsed", () => expandable.ExpandableContent.Height > 0 && expandable.ExpandableContent.Height < fadeInItem.Height + SettingsSection.ITEM_SPACING_V2);
        AddStep("reverse before collapse finishes", () =>
        {
            heightBeforeReversal = expandable.ExpandableContent.Height;
            config.SetValue(BmsRulesetSetting.SoftConstant, true);
        });
        AddAssert("reversal preserves height", () => expandable.ExpandableContent.Height, () => Is.EqualTo(heightBeforeReversal).Within(0.01));
        AddStep("finish reversed expansion", () => clock.CurrentTime += 300);
        AddUntilStep("expanded again", () => expandable.ExpandableContent.Height,
            () => Is.EqualTo(fadeInItem.Height + SettingsSection.ITEM_SPACING_V2).Within(0.01));
        AddAssert("stale collapse does not hide slider", () => fadeInItem.CanBeShown.Value && expandable.IsPresent);

        AddStep("collapse again", () => config.SetValue(BmsRulesetSetting.SoftConstant, false));
        AddStep("advance past collapse midpoint", () => clock.CurrentTime += 150);
        AddStep("record collapsing row position", () => headerYDuringCollapse = followingHeader.DrawPosition.Y);
        AddStep("advance near collapse end", () => clock.CurrentTime += 100);
        AddAssert("following row does not rebound during collapse", () => followingHeader.DrawPosition.Y,
            () => Is.LessThanOrEqualTo(headerYDuringCollapse + 0.1f));
        AddStep("approach collapse end", () => clock.CurrentTime += 49);
        AddStep("record following row position", () => headerYBeforeCompletion = followingHeader.DrawPosition.Y);
        AddStep("finish collapse", () => clock.CurrentTime += 1);
        AddUntilStep("no remaining layout space", () => expandable.IsPresent && expandable.ExpandableContent.Height == 0 && !fadeInItem.IsPresent);
        AddAssert("following row does not jump", () => followingHeader.DrawPosition.Y,
            () => Is.EqualTo(headerYBeforeCompletion).Within(0.1));
        AddStep("record completed position", () => headerYAfterCompletion = followingHeader.DrawPosition.Y);
        AddStep("advance after collapse", () => clock.CurrentTime += 50);
        AddAssert("following row stays put after collapse", () => followingHeader.DrawPosition.Y,
            () => Is.EqualTo(headerYAfterCompletion).Within(0.1));
        AddStep("advance past any delayed animation", () => clock.CurrentTime += 300);
        AddAssert("following row remains settled", () => followingHeader.DrawPosition.Y,
            () => Is.EqualTo(headerYAfterCompletion).Within(0.1));
        AddStep("restore normal clock", () => expandable.ExpandableContent.Clock = originalClock);
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
