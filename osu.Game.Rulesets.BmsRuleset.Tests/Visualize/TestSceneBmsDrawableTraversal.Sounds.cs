#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsDrawableTraversal
{
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Argon, BmsLongNoteMode.HellChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.LongNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.ChargeNote)]
    [TestCase(BmsTestSkins.SkinKind.Legacy, BmsLongNoteMode.HellChargeNote)]
    public void EmptyFrameworkSoundsKeepLifetimeAndDisposalContracts(BmsTestSkins.SkinKind skin, BmsLongNoteMode mode)
    {
        Drawable[] owned = [];
        var disposals = new Dictionary<Drawable, int>();
        var isDisposed = typeof(Drawable).GetProperty("IsDisposed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!
            .CreateDelegate<Func<Drawable, bool>>();
        loadSoundFixture(skin, mode);
        AddStep("reapply defaults and return representations", () =>
        {
            var drawables = soundFixtureObjects();
            var sounds = drawables.Select(frameworkSound).ToArray();
            owned = sounds.SelectMany(s => s.ChildrenOfType<Drawable>()).ToArray();
            foreach (var soundChild in owned)
            {
                disposals.Add(soundChild, 0);
                typeof(Drawable).GetEvent("OnDispose", BindingFlags.Instance | BindingFlags.NonPublic)!.GetAddMethod(true)!
                    .Invoke(soundChild, [(Action)(() => disposals[soundChild]++)]);
            }

            for (var i = 0; i < drawables.Length; i++)
            {
                var drawable = drawables[i];
                var sound = sounds[i];
                Assert.That(drawable.IsInPool && drawable.IsInUse, Is.True);
                Assert.That(sound.Samples, Is.Empty);
                Assert.That(sound.Length, Is.Zero);
                Assert.That(sound.IsAlive, Is.False);
                Assert.That(isDisposed(sound), Is.False);

                // Exercise the inherited stop hook even though BMS never starts this sound.
                sound.Looping = true;
                sound.Play();
                Assert.That(sound.RequestedPlaying, Is.True);
                drawable.StopAllSamples();
                Assert.That(sound.RequestedPlaying, Is.False);
                sound.Looping = false;
                drawable.PlaySamples();
                Assert.That(sound.RequestedPlaying, Is.False);

                var generation = drawable.BindingGeneration;
                var lifetime = (drawable.LifetimeStart, drawable.LifetimeEnd);
                drawable.HitObject.ApplyDefaults(Playfield.Beatmap.ControlPointInfo, Playfield.Beatmap.Difficulty);
                Assert.That(drawable.BindingGeneration, Is.GreaterThan(generation));
                Assert.That(frameworkSound(drawable), Is.SameAs(sound));
                Assert.That((drawable.LifetimeStart, drawable.LifetimeEnd), Is.EqualTo(lifetime));
                Assert.That(sound.Length, Is.Zero);

                // Disposal must cover both active notes and notes already returned to the pool.
                if (i % 2 == 0)
                {
                    var column = (BmsColumn)Playfield.Stage.Columns[drawable.HitObject.Column];
                    var entry = drawable.Entry!;
                    Assert.That(column.HitObjectContainer.Remove(entry), Is.True);
                    Assert.That(drawable.HitObject, Is.Null);
                    Assert.That(drawable.IsInUse, Is.False);
                    Assert.That(sound.Samples, Is.Empty);

                    column.HitObjectContainer.Add(entry);
                    column.HitObjectContainer.UpdateSubTree();
                    var reused = (DrawableBmsHitObject)column.HitObjectContainer.AliveEntries[entry];
                    Assert.That(reused, Is.SameAs(drawable));
                    Assert.That(frameworkSound(reused), Is.SameAs(sound));
                    Assert.That(reused.IsInUse, Is.True);
                    Assert.That(sound.RequestedPlaying, Is.False);
                    Assert.That(isDisposed(sound), Is.False);
                    Assert.That(column.HitObjectContainer.Remove(entry), Is.True);
                }
            }
            Assert.That(owned.All(d => !isDisposed(d)), Is.True);
        });
        AddStep("replace player and dispose its pools", LoadPlayer);
        AddUntilStep("all sound resources disposed once", () => owned.All(d => isDisposed(d) && disposals[d] == 1));
    }

    private void loadSoundFixture(BmsTestSkins.SkinKind skin, BmsLongNoteMode mode)
    {
        AddStep("load sound lifetime fixture", () =>
        {
            this.skin = skin;
            this.mode = mode;
            ((BmsRulesetConfigManager)RulesetConfigs.GetConfigFor(new BmsRuleset())!).SetValue(BmsRulesetSetting.ShowInvisibleNotes, true);
            LoadPlayer();
        });
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.LoadedBeatmapSuccessfully);
        AddStep("seek before notes", () =>
        {
            Player.GameplayClockContainer.Stop();
            Player.GameplayClockContainer.Seek(4800);
        });
        AddUntilStep("simulation caught up", () => Math.Abs(Player.DrawableRuleset.FrameStableClock.CurrentTime - 4800) < .001);
    }

    private DrawableBmsHitObject[] soundFixtureObjects()
    {
        var drawables = Playfield.AllColumnAliveObjects().OfType<DrawableBmsHitObject>().Where(d => d.HitObject.StartTime == 5000).ToArray();
        Assert.That(drawables, Has.Length.EqualTo(4));
        Assert.That(drawables.Select(d => d.HitObject.GetType()).Distinct().Count(), Is.EqualTo(4));
        return drawables;
    }

    private static PausableSkinnableSound frameworkSound(DrawableBmsHitObject drawable)
        => (PausableSkinnableSound)typeof(DrawableHitObject).GetProperty("Samples", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;

}
