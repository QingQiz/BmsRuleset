using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Profiles;
using System.Linq;
using NUnit.Framework;
using RhythmEngine.Core;
using RhythmEngine.Profiles.Common;
using RhythmEngine.Profiles.Bms;
using RhythmEngine.Profiles.Mania;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Judgement.Framework;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Mods.Profiles;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Scoring;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Audio;

[TestFixture]
public partial class BmsKeySoundPolicyTest
{
    [TestCase(BmsJudgementModifiers.None, 835)]
    [TestCase(BmsJudgementModifiers.NoBad, 887.5)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGood, 955)]
    [TestCase(BmsJudgementModifiers.NoBad | BmsJudgementModifiers.NoGreat, 985)]
    public void AudioReadinessUsesTheEarliestEnabledHitWindow(BmsJudgementModifiers modifiers, double earliest)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000, SampleKey = 1 });
        beatmap.HitObjects.Add(new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000, SampleKey = 2 });
        var usage = BmsGameplayAudioController.GetSampleUsages(beatmap, new BmsEngineChart(beatmap, new BmsGameplayProfile(modifiers))).Single(item => item.SampleKey == 2);
        Assert.That(usage.EarliestTriggerTime, Is.EqualTo(earliest));
        Assert.That(usage.LatestTriggerTime, Is.EqualTo(2210));
    }

    [Test]
    public void ConfiguredModsShareAudioAndFrameworkOffsets()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 0, TotalColumns = 8 };
        var first = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 1000 };
        var second = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 2000 };
        first.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        second.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);
        beatmap.HitObjects.AddRange([first, second]);
        using var score = new BmsScoreProcessor();
        score.Mods.Value = [new BmsModLr2(), new BmsModNoBad()];
        score.ApplyBeatmap(beatmap);
        Assert.That(first.HitWindows.WindowFor(HitResult.Ok), Is.EqualTo(200));
        var result = new BmsJudgementResult(first, first.CreateJudgement()) { Type = HitResult.Ok };
        result.SetTiming(1100, 1);
        Assert.That(result.TimeOffset, Is.EqualTo(100));
    }

    [Test]
    public void BeatorajaEmptyPoorAndIdlePressUseDifferentSources()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var old = new BmsLongNote { Beatmap = beatmap, StartTime = 1000, Duration = 2000, Column = 1, SampleKey = 1 };
        var next = new BmsNote { Beatmap = beatmap, StartTime = 5000, Column = 1, SampleKey = 2 };
        beatmap.HitObjects.AddRange([old, next]);
        beatmap.InvisibleNotes = [new BmsInvisibleNote { Beatmap = beatmap, StartTime = 1300, Column = 1, SampleKey = 3 }];
        var compiled = new BmsEngineChart(beatmap);
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);

        var batch = session.AdvanceTo(4600_000,
            [new(1500_000, 1, true), new(1600_000, 1, false), new(3500_000, 1, true), new(3600_000, 1, false), new(4600_000, 1, true)]);

        Assert.That(batch.Inputs.Where(input => input.Input.Pressed).Select(input => input.KeySound), Is.EqualTo(new KeySound?[]
        {
            new(compiled.Objects.Length), new(compiled.Ids[old], NoteParts.TAIL), new(compiled.Ids[next]),
        }));
        Assert.That(session.GetNote(compiled.Ids[next]).Completed, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BeatorajaIdlePressPrefersTheLastVisibleEndpointAtATie(bool hasTap)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000, TailSampleKey = 1 };
        var tap = new BmsNote { Beatmap = beatmap, Column = 1, StartTime = 3000, SampleKey = 2 };
        beatmap.HitObjects.Add(hold);
        if (hasTap)
            beatmap.HitObjects.Add(tap);
        beatmap.InvisibleNotes = [new BmsInvisibleNote { Beatmap = beatmap, Column = 1, StartTime = 3000, SampleKey = 3 }];
        var compiled = new BmsEngineChart(beatmap);
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);

        var press = session.AdvanceTo(3500_000, [new(3500_000, 1, true)]).Inputs.Single();
        var expected = hasTap ? new KeySound(compiled.Ids[tap]) : new KeySound(compiled.Ids[hold], NoteParts.TAIL);
        Assert.That(press.KeySound, Is.EqualTo(expected));
    }

    [Test]
    public void Lr2TimeoutAndEmptyPoorUpdateIndependentSessionCaches()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        var old = new BmsLongNote { Beatmap = beatmap, StartTime = 2000, Duration = 1000, Column = 1 };
        var next = new BmsNote { Beatmap = beatmap, StartTime = 5000, Column = 1 };
        beatmap.HitObjects.AddRange([old, next]);
        var compiled = new BmsEngineChart(beatmap, new BmsGameplayProfile(BmsJudgementModifiers.Lr2oraja));
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        Assert.That(session.AdvanceTo(2500_000, [new(2500_000, 1, true)]).Inputs.Single().KeySound, Is.EqualTo(new KeySound(compiled.Ids[old])));
        session.AdvanceTo(2600_000, [new(2600_000, 1, false)]);
        var clone = session.Clone();

        Assert.That(session.AdvanceTo(4000_001, [new(4000_001, 1, true)]).Inputs.Single().KeySound, Is.EqualTo(new KeySound(compiled.Ids[next])));
        Assert.That(clone.AdvanceTo(4000_000, [new(4000_000, 1, true)]).Inputs.Single().KeySound, Is.EqualTo(new KeySound(compiled.Ids[old])));
        session.Reset();
        Assert.That(session.AdvanceTo(0, [new(0, 1, true)]).Inputs.Single().KeySound, Is.Null);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ManiaEarlyPressAndRegrabSelectTheirProfileSources(bool lazer)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K };
        var hold = new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 };
        beatmap.HitObjects.Add(hold);
        var profile = GameplayProfile.FromMods([new BmsModManiaProfile { UseLazer = { Value = lazer } }]);
        var compiled = new BmsEngineChart(beatmap, profile);
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        var batch = session.AdvanceTo(1500_000,
            [new(0, 1, true), new(100_000, 1, false), new(1000_000, 1, true), new(1200_000, 1, false), new(1500_000, 1, true)]);
        var presses = batch.Inputs.Where(input => input.Input.Pressed).Select(input => input.KeySound).ToArray();
        var id = compiled.Ids[hold];

        Assert.That(presses, Is.EqualTo(lazer ? new KeySound?[] { new(id), new(id), null } : [new(id, KeySoundPolicies.STABLE_TAIL), new(id), new(id)]));
    }

    [TestCase(KeyNoteKind.Hold)]
    [TestCase(KeyNoteKind.ReleaseHold)]
    public void BeatorajaReleaseGraceControlsTailAndHeadSound(KeyNoteKind kind)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Popn9K, Rank = 2 };
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000, Kind = kind });
        var compiled = new BmsEngineChart(beatmap);
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        var rescued = session.AdvanceTo(1500_000, [new(1000_000, 1, true), new(1200_000, 1, false), new(1300_000, 1, true)]);
        Assert.That(rescued.Inputs[^1].KeySound, Is.Null);
        Assert.That(rescued.Transitions.SelectMany(t => t.KeySounds), Is.Empty);

        var expired = session.AdvanceTo(1800_000, [new(1600_000, 1, false)]);
        Assert.That(expired.Transitions.Single(t => !t.KeySounds.IsEmpty).Time, Is.EqualTo(1800_000));
        Assert.That(expired.Transitions.SelectMany(t => t.KeySounds), Is.EqualTo(new KeySound[]
        {
            new(0, NoteParts.HEAD, KeySoundAction.Mute), new(0, NoteParts.TAIL),
        }));
    }

    [Test]
    public void BeatorajaAutomaticTailAndSustainHeadUseSeparateSounds()
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        beatmap.HitObjects.AddRange([
            new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000 },
            new BmsLongNote { Beatmap = beatmap, Column = 2, StartTime = 1000, Duration = 2000, Kind = KeyNoteKind.SustainHold },
        ]);
        var compiled = new BmsEngineChart(beatmap);
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        var batch = session.AdvanceTo(3000_001,
            [new(1000_000, 1, true), new(1000_000, 2, true), new(1400_000, 2, false), new(1500_000, 2, true)]);
        Assert.That(batch.Inputs[^1].KeySound, Is.Null);
        Assert.That(batch.Transitions.SelectMany(t => t.KeySounds), Is.EqualTo(new KeySound[]
        {
            new(1, NoteParts.HEAD, KeySoundAction.Mute), new(1, NoteParts.TAIL),
            new(1, NoteParts.HEAD, KeySoundAction.Unmute), new(0, NoteParts.TAIL),
        }));
    }

    [TestCase(1500, true)]
    [TestCase(1850, true)]
    [TestCase(1900, false)]
    public void R2StopsOnlyTheEarlyReleaseHeadAndNeverPlaysATail(int release, bool stop)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 1000 });
        var compiled = new BmsEngineChart(beatmap, new BmsGameplayProfile(BmsJudgementModifiers.Lr2oraja));
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        var batch = session.AdvanceTo(2500_000, [new(1000_000, 1, true), new(release * 1000, 1, false)]);
        Assert.That(batch.Transitions.SelectMany(t => t.KeySounds),
            Is.EqualTo(stop ? new KeySound[] { new(0, NoteParts.HEAD, KeySoundAction.Stop) } : []));
    }

    [TestCase(KeyNoteKind.Hold, false, true)]
    [TestCase(KeyNoteKind.Hold, true, true)]
    [TestCase(KeyNoteKind.SustainHold, false, false)]
    public void BmsPassiveMissLeavesOnlyEmptyPressSoundOrSustain(KeyNoteKind kind, bool r2, bool emptyPressSound)
    {
        var beatmap = new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K, Rank = 2 };
        beatmap.HitObjects.Add(new BmsLongNote { Beatmap = beatmap, Column = 1, StartTime = 1000, Duration = 2000, Kind = kind });
        var compiled = new BmsEngineChart(beatmap, new BmsGameplayProfile(r2 ? BmsJudgementModifiers.Lr2oraja : BmsJudgementModifiers.None));
        var session = new JudgementSession(compiled.Chart, compiled.Configuration);
        var passive = session.AdvanceTo(1700_000);
        Assert.That(passive.Transitions.SelectMany(t => t.KeySounds), Is.Empty);
        Assert.That(session.GetNote(0).Parts.Single(p => p.Part == NoteParts.TAIL).Result, Is.EqualTo(BmsResults.POOR));

        var regrab = session.AdvanceTo(2400_000, [new(2000_000, 1, true)]);
        Assert.That(regrab.Inputs.Single().KeySound, Is.EqualTo(emptyPressSound ? new KeySound(0) : (KeySound?)null));
        Assert.That(regrab.Transitions.SelectMany(t => t.KeySounds), Is.Empty);
        Assert.That(session.GetNote(0).Parts.Single(p => p.Part == NoteParts.BODY).Active, Is.EqualTo(kind == KeyNoteKind.SustainHold));
        Assert.That(regrab.Transitions.SelectMany(t => t.Judgements), Has.None.Matches<JudgementResult>(j => j.Event.Part == NoteParts.TAIL || j.Event.Part == NoteParts.HEAD));
        Assert.That(session.GetNote(0).Parts.Single(p => p.Part == NoteParts.TAIL).Result, Is.EqualTo(BmsResults.POOR));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void ManiaPassiveHeadMissAndRegrabUseSeparateHeadAndTailSounds(bool lazer, bool release)
    {
        var chart = new KeyChart(1, [new KeyNote(0, 0, 1000_000, 3000_000, KeyNoteKind.Hold)]);
        var profile = lazer ? ManiaProfiles.CreateLazer(chart) : ManiaProfiles.CreateStable(chart);
        var session = new JudgementSession(profile.Chart, profile.Configuration);
        var missedHead = session.AdvanceTo(1500_000);
        Assert.That(missedHead.Transitions.SelectMany(t => t.KeySounds), Is.Empty);
        Assert.That(missedHead.Transitions.SelectMany(t => t.Judgements).Select(j => j.Event.Result),
            Is.EqualTo(lazer ? new[] { ManiaResults.MISS } : []));

        var regrab = session.AdvanceTo(1700_000, [new(1700_000, 0, true)]);
        Assert.That(regrab.Inputs.Single().KeySound, Is.EqualTo(lazer ? (KeySound?)null : new KeySound(0)));
        Assert.That(regrab.Transitions.SelectMany(t => t.KeySounds), Is.Empty);

        var tail = session.AdvanceTo(3300_000, release ? [new(3000_000, 0, false)] : []);
        var sounds = release || !lazer
            ? new[] { new KeySound(0, lazer ? NoteParts.TAIL : KeySoundPolicies.STABLE_TAIL) }
            : [];
        Assert.That(tail.Transitions.SelectMany(t => t.KeySounds), Is.EqualTo(sounds));
    }
}
