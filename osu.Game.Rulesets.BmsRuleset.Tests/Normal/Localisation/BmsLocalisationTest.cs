using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Configuration;
using osu.Framework.Localisation;
using osu.Game.Rulesets.BmsRuleset.Localisation;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Localisation;

[TestFixture]
public class BmsLocalisationTest
{
    private const string resource_prefix = "osu.Game.Rulesets.BmsRuleset.Resources.Localisation.BmsStrings";

    private TestFrameworkConfigManager config = null!;
    private LocalisationManager localisation = null!;

    [SetUp]
    public void SetUp()
    {
        config = new TestFrameworkConfigManager();
        localisation = new LocalisationManager(config);
        localisation.AddLanguage("en", new TestLocalisationStore("en"));
        localisation.AddLanguage("zh", new TestLocalisationStore("zh"));
    }

    [TearDown]
    public void TearDown()
    {
        localisation.Dispose();
        config.Dispose();
    }

    [Test]
    public void TestTextUpdatesWhenLanguageChanges()
    {
        var text = localisation.GetLocalisedBindableString(BmsStrings.ImportBmsFiles);

        Assert.That(text.Value, Is.EqualTo("Import BMS files"));

        config.SetValue(FrameworkSetting.Locale, "zh");
        Assert.That(text.Value, Is.EqualTo("导入 BMS 文件"));

        config.SetValue(FrameworkSetting.Locale, "en");
        Assert.That(text.Value, Is.EqualTo("Import BMS files"));
    }

    [Test]
    public void TestFormattedTextUsesEmbeddedChineseResource()
    {
        config.SetValue(FrameworkSetting.Locale, "zh");

        Assert.That(
            localisation.GetLocalisedString(BmsStrings.LoadedTable("Satellite", 3)),
            Is.EqualTo("已载入难度表：Satellite（3 张谱面）"));
    }

    [Test]
    public void TestResourceKeysAndFormatArgumentsMatch()
    {
        var english = getResourceValues(resource_prefix);
        var chinese = getResourceValues($"{resource_prefix}.zh");

        Assert.That(english, Is.Not.Empty);
        Assert.That(chinese.Keys, Is.EquivalentTo(english.Keys));

        Assert.Multiple(() =>
        {
            foreach (var (key, englishValue) in english)
            {
                var englishFormat = CompositeFormat.Parse(englishValue);
                var chineseFormat = CompositeFormat.Parse(chinese[key]);

                Assert.That(chineseFormat.MinimumArgumentCount, Is.EqualTo(englishFormat.MinimumArgumentCount), $"Format arguments differ for '{key}'");
            }
        });
    }

    private static IReadOnlyDictionary<string, string> getResourceValues(string resourceName)
    {
        var manager = new ResourceManager(resourceName, typeof(BmsStrings).Assembly);
        var resources = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;

        return resources.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private sealed class TestFrameworkConfigManager() : FrameworkConfigManager(null)
    {
        protected override string Filename => null;

        protected override void InitialiseDefaults()
        {
            SetDefault(FrameworkSetting.Locale, "en");
            SetDefault(FrameworkSetting.ShowUnicode, true);
        }
    }

    private sealed class TestLocalisationStore(string culture) : ILocalisationStore
    {
        public CultureInfo EffectiveCulture { get; } = CultureInfo.GetCultureInfo(culture);

        public string Get(string name) => null;

        public Task<string> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

        public Stream GetStream(string name) => null;

        public IEnumerable<string> GetAvailableResources() => [];

        public void Dispose()
        {
        }
    }
}
