using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[HeadlessTest]
public partial class BmsWorkingBeatmapCacheTest : OsuTestScene
{
    private GameHost host = null!;
    private AudioManager audio = null!;
    private BeatmapManager iconBeatmapManager = null!;

    [Test]
    public void TestInstallWrapsEachBeatmapManagerInstance()
    {
        AddAssert("first manager is wrapped", () => installAndCheckNewManager());
        AddAssert("second manager is also wrapped", () => installAndCheckNewManager());
    }

    [Test]
    public void TestRulesetIconInstallsSongSelectPreviewHook()
    {
        AddStep("load ruleset icon with beatmap manager", () =>
        {
            iconBeatmapManager = createBeatmapManager();

            Child = new DependencyProvidingContainer
            {
                RelativeSizeAxes = Axes.Both,
                CachedDependencies =
                [
                    (typeof(BeatmapManager), iconBeatmapManager),
                ],
                Child = new BmsRuleset().CreateIcon(),
            };
        });

        AddUntilStep("icon installed hook", () => getWorkingBeatmapCache(iconBeatmapManager) is BmsWorkingBeatmapCache);
    }

    [Test]
    public void TestBmsWorkingBeatmapDoesNotTransferTrack()
    {
        BmsWorkingBeatmap source = null!;
        WorkingBeatmap target = null!;

        AddStep("create working beatmaps", () =>
        {
            source = new BmsWorkingBeatmap(new StubWorkingBeatmap(audio), audio);
            target = new StubWorkingBeatmap(audio);
        });

        AddAssert("transfer refused", () => !source.TryTransferTrack(target));
    }

    private static WorkingBeatmapCache getWorkingBeatmapCache(BeatmapManager manager) =>
        (WorkingBeatmapCache)typeof(BeatmapManager)
            .GetField("workingBeatmapCache", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(manager)!;

    [BackgroundDependencyLoader]
    private void load(GameHost host, AudioManager audio)
    {
        this.host = host;
        this.audio = audio;
    }

    private bool installAndCheckNewManager()
    {
        var manager = createBeatmapManager();

        return BmsWorkingBeatmapHelper.Install(manager)
               && getWorkingBeatmapCache(manager) is BmsWorkingBeatmapCache;
    }

    private BeatmapManager createBeatmapManager() =>
        new BeatmapManager(LocalStorage, Realm, null, audio, Resources, host, Beatmap.Default);

    private class StubWorkingBeatmap : WorkingBeatmap
    {
        public StubWorkingBeatmap(AudioManager audioManager)
            : base(new BeatmapInfo(), audioManager)
        {
        }

        public override Texture GetBackground() => throw new NotSupportedException();

        public override Stream GetStream(string storagePath) => throw new NotSupportedException();

        protected override IBeatmap GetBeatmap() => throw new NotSupportedException();

        protected override Track GetBeatmapTrack() => throw new NotSupportedException();

        protected override ISkin GetSkin() => throw new NotSupportedException();
    }
}
