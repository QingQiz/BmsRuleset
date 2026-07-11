using NUnit.Framework;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
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
        float? lightPositionOffset = null;
        playfield.Stage.SkinHitTargetPositionChanged += _ => skinPositionChanges++;
        playfield.Stage.HitTargetPositionOffsetChanged += offset => lightPositionOffset = offset;

        playfield.Stage.SetHitTargetPositionOffset(40);

        Assert.Multiple(() =>
        {
            Assert.That(playfield.Stage.HitTargetPosition, Is.EqualTo(120));
            Assert.That(playfield.ScrollController.ScrollRangeScale, Is.EqualTo(originalRangeScale));
            Assert.That(skinPositionChanges, Is.Zero);
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
}
