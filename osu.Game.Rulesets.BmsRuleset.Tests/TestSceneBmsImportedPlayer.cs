using System;
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
using osu.Game.Rulesets.BmsRuleset.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.BmsRuleset.Tests;

[TestFixture]
public partial class TestSceneBmsImportedPlayer : PlayerTestScene
{
    private BeatmapManager beatmapManager = null!;
    private RealmRulesetStore rulesets = null!;
    private BeatmapInfo importedBeatmap = null!;
    private BmsHitObject firstKeyNote = null!;

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
    public void TestImportedRealBmsLoadsPlayer()
    {
        AddStep("register bms ruleset", addBmsRuleset);
        AddStep("import real bms", () =>
        {
            var chartPath = Path.Combine(BmsEmbeddedSongDecoderTest.TestSongsRoot, "Aleph-0 (by LeaF)", "_7NORMAL.bms");
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
            SelectedMods.Value = Array.Empty<Mod>();
        });

        AddAssert("beatmap path stored", () => Beatmap.Value.BeatmapInfo.Path, () => Is.EqualTo("_7NORMAL.bms"));
        AddAssert("stored file resolves", () => Beatmap.Value.BeatmapInfo.BeatmapSet?.GetPathForFile(Beatmap.Value.BeatmapInfo.Path!), () => Is.Not.Null.And.Not.Empty);
        AddAssert("working beatmap decodes", () => Beatmap.Value.Beatmap.HitObjects.OfType<BmsHitObject>().Count(), () => Is.GreaterThan(100));

        AddStep("load player", () => LoadScreen(Player = new TestPlayer(false, false)));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("player loaded beatmap", () => Player.LoadedBeatmapSuccessfully);
        AddAssert("player has imported objects", () => Player.DrawableRuleset.Objects.Count(), () => Is.GreaterThan(100));

        AddStep("seek to first key note", () =>
        {
            firstKeyNote = Player.DrawableRuleset.Objects.OfType<BmsHitObject>().First(o => o.Column == 1);
            Player.GameplayClockContainer.Seek(firstKeyNote.StartTime);
        });

        AddUntilStep("target key note alive", () => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.Any(d => d.HitObject == firstKeyNote));
        AddStep("press key 1", () => InputManager.Key(Key.Z));
        AddUntilStep("hit judgement produced", () => Player.Results.Any(r => r.IsHit && r.Type != HitResult.Miss));
    }
}
