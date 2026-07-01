using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Sprites;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Normal.SkinTest;

[TestFixture]
public class BmsTextHudEditModeTest
{
    [Test]
    public void TestEditModeShowsPlaceholderAndFullyVisible()
    {
        var hud = new BmsTextHud();
        applyEditModeVisibility(hud, true);

        Assert.Multiple(() =>
        {
            Assert.That(hud.Alpha, Is.EqualTo(1f));
            Assert.That(getMainText(hud).Text.ToString(), Is.EqualTo("Sample Text Event"));
        });
    }

    [Test]
    public void TestNonEditModeShowsGameStartText()
    {
        var hud = new BmsTextHud();
        applyEditModeVisibility(hud, false);

        Assert.That(getMainText(hud).Text.ToString(), Is.EqualTo("Game Start"));
    }

    private static void applyEditModeVisibility(BmsTextHud hud, bool isEditing)
    {
        var method = typeof(BmsTextHud).GetMethod("applyEditModeVisibility", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);

        method!.Invoke(hud, [isEditing]);
    }

    private static SpriteText getMainText(BmsTextHud hud)
    {
        var field = typeof(BmsTextHud).GetField("mainText", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);

        return (SpriteText)field!.GetValue(hud)!;
    }
}
