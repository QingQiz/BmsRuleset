using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.BmsRuleset.Localisation;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

public partial class TestSceneBmsJudgementImages : BmsPlayerTestScene
{
    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    [TestCase(false, "j-good.png", 178, 50, 1)]
    [TestCase(false, "mania-hit300g-0@2x.png", 376, 182, 2)]
    [TestCase(true, "hit300@2x.png", 206, 120, 2)]
    public void TestJudgementImagesAreIndependentAndSurviveLayoutReload(bool classic, string builtInImage, int builtInWidth, int builtInHeight, float builtInScale)
    {
        const string image_name = "custom-judgement.png";
        const int image_width = 93;
        const int image_height = 19;
        var controls = Array.Empty<SettingsDropdown<string>>();

        AddStep("add an image to a copy of the built-in skin", () =>
        {
            skins().CurrentSkinInfo.Value = classic ? skins().DefaultClassicSkin.SkinInfo : skins().CurrentSkinInfo.Default;
            skins().EnsureMutableSkin();
            skins().CurrentSkinInfo.Value.PerformWrite(skin =>
            {
                using var image = new Image<Rgba32>(image_width, image_height, new Rgba32(255, 255, 255));
                using var png = new MemoryStream();
                image.SaveAsPng(png);
                png.Position = 0;
                skins().AddFile(skin, png, image_name);
            });
        });
        AddStep("load player", LoadPlayer);
        AddUntilStep("playfield HUD loaded", () => Player.IsLoaded && playfieldHud().ComponentsLoaded);
        AddStep("open judgement display settings", () =>
        {
            controls = display().CreateSettingsControls().OfType<SettingsDropdown<string>>().ToArray();
            Add(new FillFlowContainer
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Width = 250,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Children = controls
            });
        });
        AddUntilStep("each grade offers default, imported, and built-in images", () =>
            controls.Length == 6 && controls.All(control => control.IsLoaded
                && control.Items.Contains(string.Empty) && control.Items.Contains(image_name) && control.Items.Contains(builtInImage)));
        AddStep("choose the imported image only for E-POOR", () =>
            controls.Single(control => control.LabelText.Equals(BmsStrings.JudgementDisplayMissImage)).Current.Value = image_name);
        AddAssert("POOR keeps its own default image", () => display().MissImage.Value == image_name && display().MehImage.IsDefault);
        AddAssert("only one grade uses the custom image", () => customJudgements().Count(), () => Is.EqualTo(1));
        AddStep("show an E-POOR judgement", () => ((BmsDrawableRuleset)Player.DrawableRuleset).GameplayEvents.RaiseJudgementDisplayed(HitResult.Miss));
        AddUntilStep("the custom judgement is visible", () => customJudgements().Any(d => d.Parent?.Alpha > 0 && d.Drawable.Alpha > 0));
        AddStep("choose a built-in image for PGREAT", () =>
            controls.Single(control => control.LabelText.Equals(BmsStrings.JudgementDisplayPerfectImage)).Current.Value = builtInImage);
        AddAssert("the built-in image and its display scale are applied", selectedBuiltInImageIsReady);
        AddStep("save and reload the playfield layout", () =>
        {
            skins().CurrentSkin.Value.UpdateDrawableTarget(playfieldHud());
            playfieldHud().Reload();
        });
        AddUntilStep("both choices render after reload", () => playfieldHud().ComponentsLoaded && display().MissImage.Value == image_name
            && customJudgements().Count() == 1 && selectedBuiltInImageIsReady());
        AddStep("restore the grade's default image", () => display().MissImage.SetDefault());
        AddAssert("no grade uses the custom file", () => !customJudgements().Any());

        return;

        SkinManager skins() => Dependencies.Get<SkinManager>();

        SkinnableContainer playfieldHud() => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>()
            .Single(container => container.Lookup.Lookup == GlobalSkinnableContainers.Playfield);

        BmsJudgementDisplay display() => playfieldHud().Components.OfType<BmsJudgementDisplay>().Single();

        bool selectedBuiltInImageIsReady() => display().PerfectImage.Value == builtInImage && display().ChildrenOfType<BmsJudgementDrawable>()
            .Select(d => d.Drawable).OfType<BmsJudgementPiece>().Any(piece => piece.ChildrenOfType<Sprite>()
                .Any(sprite => sprite.Texture is { } texture && texture.Width == builtInWidth && texture.Height == builtInHeight && texture.ScaleAdjust == builtInScale));

        IEnumerable<BmsJudgementDrawable> customJudgements() => display().ChildrenOfType<BmsJudgementDrawable>()
            .Where(d => d.Drawable.ChildrenOfType<Sprite>().Any(sprite => sprite.Texture is { Width: image_width, Height: image_height }));
    }

    [TearDownSteps]
    public void RestoreDefaultSkin() => AddStep("restore default skin", () => Dependencies.Get<SkinManager>().CurrentSkinInfo.SetDefault());
}
