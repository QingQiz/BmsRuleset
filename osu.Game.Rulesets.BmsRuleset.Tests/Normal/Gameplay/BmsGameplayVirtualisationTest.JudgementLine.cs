using System.Linq;
using NUnit.Framework;
using osu.Game.Configuration;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.Gameplay;

public partial class BmsGameplayVirtualisationTest
{
    [Test]
    public void TestJudgementLineOffsetPreservesVisibleTime()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        playfield.ScrollController.SetHitTargetPosition(playfield.Stage.HitTargetPosition);
        var originalRangeScale = playfield.ScrollController.ScrollRangeScale;
        var skinPositionChanges = 0;
        var lightPositionChanges = 0;
        playfield.Stage.SkinHitTargetPositionChanged += _ => skinPositionChanges++;
        playfield.Stage.LightPositionOffsetChanged += _ => lightPositionChanges++;

        playfield.Stage.SetHitTargetPositionOffset(40);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.HitTargetPosition, Is.EqualTo(120));
            Assert.That(playfield.ScrollController.ScrollRangeScale, Is.EqualTo(originalRangeScale));
            Assert.That(skinPositionChanges, Is.Zero);
            Assert.That(playfield.Stage.LightPositionOffset, Is.Zero);
            Assert.That(lightPositionChanges, Is.Zero);
        });
    }

    [Test]
    public void TestLightPositionOffsetDoesNotMoveJudgementLine()
    {
        var playfield = new BmsPlayfield(attachBeatmap(new BmsBeatmap
        {
            TotalColumns = BmsLayout.BME7_KEY_COLUMNS,
            LayoutVariant = BmsLayoutVariant.Bme7K,
        }));
        var originalHitTargetPosition = playfield.Stage.HitTargetPosition;
        float? lightPositionOffset = null;
        playfield.Stage.LightPositionOffsetChanged += offset => lightPositionOffset = offset;

        playfield.Stage.SetLightPositionOffset(40);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.HitTargetPosition, Is.EqualTo(originalHitTargetPosition));
            Assert.That(playfield.Stage.HitTargetPositionOffset, Is.Zero);
            Assert.That(playfield.Stage.LightPositionOffset, Is.EqualTo(40));
            Assert.That(lightPositionOffset, Is.EqualTo(40));
        });
    }

    [Test]
    public void TestHitExplosionUsesJudgementLineOffset()
    {
        var explosion = new BmsHitExplosion(
            new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1), 40);

        Assert.That(explosion.Y, Is.EqualTo(-40));
    }

    [Test]
    public void TestJudgementLineOffsetIsSerialisedWithStageHud()
    {
        var hud = new BmsStageHud();
        hud.JudgementLineOffset.Value = 40;

        var restored = (BmsStageHud)hud.CreateSerialisedInfo().CreateInstance();

        Assert.That(restored.JudgementLineOffset.Value, Is.EqualTo(40));
    }

    [Test]
    public void TestLightPositionOffsetIsSerialisedWithStageHud()
    {
        var hud = new BmsStageHud();
        hud.LightPositionOffset.Value = 40;

        var restored = (BmsStageHud)hud.CreateSerialisedInfo().CreateInstance();

        Assert.That(restored.LightPositionOffset.Value, Is.EqualTo(40));
    }

    [Test]
    public void TestNoteHeightScaleIsSerialisedWithStageHud()
    {
        var hud = new BmsStageHud();
        hud.NoteHeightScale.Value = 1.5f;

        var restored = (BmsStageHud)hud.CreateSerialisedInfo().CreateInstance();

        Assert.That(restored.NoteHeightScale.Value, Is.EqualTo(1.5f));
    }

    [Test]
    public void TestProportionalWidthReferenceIsSerialisedWithStageHud()
    {
        var hud = new BmsStageHud();
        hud.ProportionalWidthReference.Value = 360;

        var restored = (BmsStageHud)hud.CreateSerialisedInfo().CreateInstance();

        Assert.That(restored.ProportionalWidthReference.Value, Is.EqualTo(360));
    }

    [Test]
    public void TestStageHudSettingsCanCreateWidthScalingCheckbox()
    {
        var hud = new BmsStageHud();

        Assert.DoesNotThrow(() => _ = hud.CreateSettingsControls().ToArray());
    }
}
