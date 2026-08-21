using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Beatmaps.Objects;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Drawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.NoteTextures;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.Components;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
using osu.Game.Rulesets.BmsRuleset.UI.Objects;
using osu.Game.Rulesets.BmsRuleset.UI.Objects.LnHelper;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;
using osuTK.Graphics;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

[TestFixture]
public partial class TestSceneBmsSkins : BmsPlayerTestScene
{
    private BmsTestSkins.SkinKind skinKind;

    protected override TestPlayer CreatePlayer(Ruleset ruleset)
        => CreateBmsPlayer(BmsTestReplays.CreateReplayFrames, skinKind);

    protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
    {
        var beatmap = BmsTestBeatmaps.CreateBeatmap();
        BmsTestBeatmaps.SetupBeatmapInfo(beatmap, ruleset);
        return beatmap;
    }

    private void createSkinScene(BmsTestSkins.SkinKind kind)
    {
        this.AddSetupStep($"use {kind} skin", () => skinKind = kind);
        this.AddSetupStep("load player", LoadPlayer);
        this.AddSetupUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        this.AddSetupAssert("beatmap loaded", () => Player.LoadedBeatmapSuccessfully);
        this.AddSetupAssert("loaded bms drawable ruleset", () => Player.DrawableRuleset, Is.TypeOf<BmsDrawableRuleset>());
        this.AddSetupAssert("loaded bms playfield", () => Player.DrawableRuleset.Playfield, Is.TypeOf<BmsPlayfield>());
        this.AddSetupUntilStep("gameplay hud loaded", () => Player.HUDOverlay.IsLoaded);
        this.AddSetupUntilStep("hud skin components loaded", () => Player.HUDOverlay.ChildrenOfType<SkinnableContainer>().All(c => c.ComponentsLoaded));
        this.AddSetupUntilStep("bms stage loaded", () => Playfield.Stage.IsLoaded);
        if (kind == BmsTestSkins.SkinKind.Legacy)
            addLegacySkinRuntimeCoverageAssertions();

        this.AddSetupUntilStep("all hit results produced",
            () => BmsRuleset.STATIC_VALID_HIT_RESULTS.All(result => Player.ScoreProcessor.Statistics.GetValueOrDefault(result) > 0));
        this.AddSetupUntilStep("judgement hud active", () =>
        {
            var display = Player.HUDOverlay.ChildrenOfType<BmsJudgementDisplay>().SingleOrDefault();
            return display?.ChildrenOfType<SkinnableDrawable>().Any(d => d.Parent?.Alpha > 0 && d.Alpha > 0 && d.DrawWidth > 0 && d.DrawHeight > 0) == true;
        });
        this.AddSetupAssert("score changed", () => Player.ScoreProcessor.TotalScore.Value, Is.GreaterThan(0));
        AddStep("skin scene complete", () => { });
    }

    private void addLegacySkinRuntimeCoverageAssertions()
    {
        const int animated_body_column = 1;
        const int animated_tail_column = 7;
        Sprite[] bodySpritesBeforeReload = null;

        this.AddSetupUntilStep("stage HUD uses skin.ini width", () =>
            Player.HUDOverlay.ChildrenOfType<BmsStageHud>().SingleOrDefault() is { Width: > 0 }
            && Precision.AlmostEquals(Playfield.Stage.Scale.X, 1));

        this.AddSetupUntilStep("legacy LN skin coverage held", () =>
            Player.GameplayClockContainer.CurrentTime >= BmsTestBeatmaps.LN_SKIN_COVERAGE_START_TIME + 95);
        this.AddSetupUntilStep("animated LN body uses first frame when not held", () =>
        {
            var body = longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column));
            if (body == null)
                return false;

            body.UpdateBody(Math.Max(1, body.DrawHeight), false, false);
            return bodyAnimationFrameCount(body) == 2 && currentBodyAnimationFrame(body) == 0;
        });
        this.AddSetupAssert("held LN body uses normal tint", () =>
            longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column)) is { } body
            && body.Colour == Color4.White
            && body.ChildrenOfType<Sprite>().Any(s => s.Alpha > 0 && s.Texture != null));
        this.AddSetupAssert("animated LN body uses one stretched sprite", () =>
            longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column)) is { Masking: false } body
            && body.ChildrenOfType<Sprite>().Count(sprite => sprite.Alpha > 0 && sprite.Texture != null) == 1);
        this.AddSetupUntilStep("animated LN body advances frames while held", () =>
            bodyAnimationFrameCount(longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column))) == 2
            && currentBodyAnimationFrame(longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column))) == 1);
        this.AddSetupAssert("animated LN tail renders multi-frame drawable", () =>
            liveSkinCoverageLongNote(animated_tail_column)?.ChildrenOfType<TextureAnimation>().Any(a => a.FrameCount == 2) == true);
        this.AddSetupStep("capture LN body sprites", () =>
            bodySpritesBeforeReload = longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column)).ChildrenOfType<Sprite>().ToArray());
        this.AddSetupStep("reload skin source", () =>
            typeof(SkinProvidingContainer).GetMethod("TriggerSourceChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource, null));
        this.AddSetupUntilStep("stage HUD survives skin reload", () =>
            Player.HUDOverlay.ChildrenOfType<BmsStageHud>().Count() == 1
            && Playfield.Stage.HasHudTransform
            && Precision.AlmostEquals(Playfield.Stage.Scale.X, 1));
        this.AddSetupUntilStep("LN body reloads after skin change", () =>
            longNoteBodyOf(liveSkinCoverageLongNote(animated_body_column)).ChildrenOfType<Sprite>()
                .Any(sprite => sprite.Alpha > 0 && !bodySpritesBeforeReload.Contains(sprite)));
    }

    private void assertLegacySkinConfigResolves()
    {
        ISkinSource skin() => ((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource;

        AddAssert("per-column background colours resolve", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnBackgroundColour, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.ExpectedColumnBackgroundColour).ToArray()));

        AddAssert("per-column light colours resolve", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.ExpectedColumnLightColour).ToArray()));

        AddAssert("per-column key images resolve", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfigString(LegacyManiaSkinConfigurationLookups.KeyImage, BmsSkinComponents.KeyArea, c, skin()))
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.KeyImage).ToArray()));

        AddAssert("per-column key-down images resolve", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfigString(LegacyManiaSkinConfigurationLookups.KeyImageDown, BmsSkinComponents.KeyArea, c, skin()))
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.KeyImageDown).ToArray()));

        AddAssert("per-column note images resolve", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfigString(LegacyManiaSkinConfigurationLookups.NoteImage, BmsSkinComponents.Note, c, skin()))
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.NoteImage).ToArray()));

        AddAssert("hold note body image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, BmsSkinComponents.HoldNoteBody, 0, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.HOLD_NOTE_BODY_IMAGE));
        AddAssert("animated hold note body images resolve per column",
            () => BmsTestLegacySkin.ANIMATED_HOLD_BODY_COLUMNS
                .Select(c => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, BmsSkinComponents.HoldNoteBody, c, skin()))
                .ToArray(),
            () => Is.EqualTo(BmsTestLegacySkin.ANIMATED_HOLD_BODY_COLUMNS.Select(BmsTestLegacySkin.HoldNoteBodyImage).ToArray()));
        AddAssert("hold note tail image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, BmsSkinComponents.HoldNoteTail, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.HOLD_NOTE_TAIL_IMAGE));
        AddAssert("animated hold note tail images resolve per column",
            () => BmsTestLegacySkin.ANIMATED_HOLD_TAIL_COLUMNS
                .Select(c => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, BmsSkinComponents.HoldNoteTail, c, skin()))
                .ToArray(),
            () => Is.EqualTo(BmsTestLegacySkin.ANIMATED_HOLD_TAIL_COLUMNS.Select(BmsTestLegacySkin.HoldNoteTailImage).ToArray()));
        AddAssert("mine image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.Hit100, BmsSkinComponents.Mine, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.MINE_IMAGE));
        AddAssert("explosion image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.ExplosionImage, BmsSkinComponents.HitExplosion, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.EXPLOSION_IMAGE));
        AddAssert("hold note light image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteLightImage, BmsSkinComponents.HitExplosion, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.HOLD_NOTE_LIGHT_IMAGE));
        AddAssert("hit target image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.HitTargetImage, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.HIT_TARGET_IMAGE));
        AddAssert("left stage image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.LeftStageImage, BmsSkinComponents.StageBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.LEFT_STAGE_IMAGE));
        AddAssert("right stage image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.RightStageImage, BmsSkinComponents.StageBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.RIGHT_STAGE_IMAGE));
        AddAssert("bottom stage image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.BottomStageImage, BmsSkinComponents.StageForeground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.BOTTOM_STAGE_IMAGE));
        AddAssert("stage light image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.LightImage, BmsSkinComponents.StageBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.LIGHT_IMAGE));

        AddAssert("hit position resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedHitPosition));
        AddAssert("light position resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.LightPosition, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedLightPosition));
        AddAssert("score position resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.ScorePosition, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedScorePosition));
        AddAssert("combo position resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.ComboPosition, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedComboPosition));
        AddAssert("show judgement line resolves",
            () => getConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedShowJudgementLine));

        AddAssert("bar line colour resolves",
            () => getConfig<Color4>(LegacyManiaSkinConfigurationLookups.BarLineColour, BmsSkinComponents.BarLine, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedBarLineColour));
        AddAssert("column line colour resolves",
            () => getConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLineColour, BmsSkinComponents.ColumnBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedColumnLineColour));
        AddAssert("judgement line colour resolves",
            () => getConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour, BmsSkinComponents.HitTarget, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedJudgementLineColour));
        AddAssert("combo break colour resolves",
            () => getConfig<Color4>(LegacyManiaSkinConfigurationLookups.ComboBreakColour, BmsSkinComponents.BarLine, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedComboBreakColour));

        AddAssert("barline height resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.BarLineHeight, BmsSkinComponents.BarLine, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedBarlineHeight));
        AddAssert("width-for-note-height-scale resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, BmsSkinComponents.Note, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedWidthForNoteHeightScale));
        AddAssert("per-column width resolves", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfig<float>(LegacyManiaSkinConfigurationLookups.ColumnWidth, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.ExpectedColumnWidth).ToArray()));
        AddAssert("per-column left line width resolves", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfig<float>(LegacyManiaSkinConfigurationLookups.LeftLineWidth, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.ExpectedLeftLineWidth).ToArray()));
        AddAssert("per-column right line width resolves", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfig<float>(LegacyManiaSkinConfigurationLookups.RightLineWidth, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.ExpectedRightLineWidth).ToArray()));
        AddAssert("per-column right spacing resolves", () =>
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT - 1)
                    .Select(c => getConfig<float>(LegacyManiaSkinConfigurationLookups.RightColumnSpacing, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT - 1).Select(BmsTestLegacySkin.ExpectedRightColumnSpacing).ToArray()));
        AddAssert("rightmost column has no outer spacing",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.RightColumnSpacing, BmsSkinComponents.ColumnBackground, BmsTestLegacySkin.COLUMN_COUNT - 1, skin()),
            () => Is.Null);
        // Left spacing is null for the leftmost column (no gap to its left), so assert columns 1..7 only.
        AddAssert("per-column left spacing resolves", () =>
                Enumerable.Range(1, BmsTestLegacySkin.COLUMN_COUNT - 1)
                    .Select(c => getConfig<float>(LegacyManiaSkinConfigurationLookups.LeftColumnSpacing, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(1, BmsTestLegacySkin.COLUMN_COUNT - 1).Select(BmsTestLegacySkin.ExpectedLeftColumnSpacing).ToArray()));
        AddAssert("minimum column width resolves",
            () => getConfig<float>(LegacyManiaSkinConfigurationLookups.MinimumColumnWidth, BmsSkinComponents.ColumnBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedMinimumColumnWidth));
        AddAssert("keys under notes resolves",
            () => getConfig<bool>(LegacyManiaSkinConfigurationLookups.KeysUnderNotes, BmsSkinComponents.ColumnBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedKeysUnderNotes()));
        AddAssert("light frame per second resolves",
            () => getConfig<int>(LegacyManiaSkinConfigurationLookups.LightFramePerSecond, BmsSkinComponents.StageBackground, null, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedLightFramePerSecond));

        AddAssert("judgement images resolve", () => new[]
        {
            getConfigString(LegacyManiaSkinConfigurationLookups.Hit300g, BmsSkinComponents.HitTarget, null, skin()),
            getConfigString(LegacyManiaSkinConfigurationLookups.Hit300, BmsSkinComponents.HitTarget, null, skin()),
            getConfigString(LegacyManiaSkinConfigurationLookups.Hit200, BmsSkinComponents.HitTarget, null, skin()),
            getConfigString(LegacyManiaSkinConfigurationLookups.Hit50, BmsSkinComponents.HitTarget, null, skin()),
            getConfigString(LegacyManiaSkinConfigurationLookups.Hit0, BmsSkinComponents.HitTarget, null, skin()),
        }, () => Is.EqualTo([
            BmsTestLegacySkin.JUDGEMENT_PGREAT_IMAGE,
            BmsTestLegacySkin.JUDGEMENT_GREAT_IMAGE,
            BmsTestLegacySkin.JUDGEMENT_GOOD_IMAGE,
            BmsTestLegacySkin.JUDGEMENT_BAD_IMAGE,
            BmsTestLegacySkin.JUDGEMENT_POOR_IMAGE,
        ]));
    }

    private void assertLegacySkinRenders()
    {
        IBmsGameplaySkinDrawableSource factorySource()
            => ((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource.AllSources.OfType<BmsLegacySkinTransformer>().FirstOrDefault();

        ISkinSource skinSource()
            => ((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource;

        AddAssert("note factory produces resolved note piece",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<BmsResolvedNotePiece>);
        AddAssert("animated note factories produce multi-frame animations",
            () => BmsTestLegacySkin.ANIMATED_NOTE_COLUMNS.All(c =>
                (factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, c))?.Create() as BmsResolvedNotePiece)
                ?.ChildrenOfType<TextureAnimation>().SingleOrDefault()?.FrameCount == 2));
        AddAssert("animated hold body textures resolve as animation frames",
            () =>
            {
                using var cache = new BmsLongNoteBodySource.BmsLongNoteBodyTextureCache();

                return BmsTestLegacySkin.ANIMATED_HOLD_BODY_COLUMNS.All(c =>
                {
                    var textures = BmsLongNoteBodySource.Resolve(
                        skinSource(),
                        new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteBody, BmsLayoutVariant.Bme7K, c),
                        ((IStorageResourceProvider)this).Renderer,
                        cache);

                    return textures?.Kind == BmsLongNoteBodyTextureKind.AnimationFrames && textures.Value.Textures.Length == 2;
                });
            });
        AddAssert("1x1 hold tail factories resolve per column",
            () => BmsTestLegacySkin.ONE_PIXEL_TAIL_COLUMNS.All(c =>
            {
                var sprite = (factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, BmsLayoutVariant.Bme7K, c))?.Create() as BmsResolvedNotePiece)
                    ?.ChildrenOfType<Sprite>().SingleOrDefault();

                return sprite?.Texture?.DisplayWidth == 1 && sprite.Texture.DisplayHeight == 1;
            }));
        AddAssert("animated hold tail factories produce multi-frame animations",
            () => BmsTestLegacySkin.ANIMATED_HOLD_TAIL_COLUMNS.All(c =>
                (factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HoldNoteTail, BmsLayoutVariant.Bme7K, c))?.Create() as BmsResolvedNotePiece)
                ?.ChildrenOfType<TextureAnimation>().SingleOrDefault()?.FrameCount == 2));
        AddAssert("explosion factory produces resolved explosion",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<LegacyBmsHitExplosion>);
        AddAssert("key area factory produces legacy key area",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<LegacyBmsKeyArea>);
        AddAssert("column background factory produces legacy background",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<LegacyBmsColumnBackground>);
        AddAssert("column light factory produces legacy light",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.ColumnLight, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<LegacyBmsColumnLight>);
        AddAssert("stage hit target factory produces legacy hit target",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget))?.Create(),
            Is.TypeOf<LegacyBmsHitTarget>);
        AddAssert("column hit target factory is not provided",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget, BmsLayoutVariant.Bme7K, 1)) == null);

        // N-suffix frame animations: the explosion (lightingN-0/1/2) and column light (stage-light-0/1)
        // are provided as multi-frame textures; verify the runtime picks them up as animations.
        AddAssert("explosion is a multi-frame animation",
            () => (factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1))?.Create() as LegacyBmsHitExplosion)
                ?.ChildrenOfType<TextureAnimation>().SingleOrDefault()?.FrameCount,
            () => Is.EqualTo(3));
        AddAssert("column light is a multi-frame animation",
            () => Playfield.ChildrenOfType<LegacyBmsColumnLight>()
                .Select(c => c.ChildrenOfType<TextureAnimation>().SingleOrDefault()?.FrameCount ?? 0)
                .ToArray(),
            () => Is.EqualTo(Enumerable.Repeat(2, BmsTestLegacySkin.COLUMN_COUNT).ToArray()));

        AddAssert("gameplay layers render in legacy mania order", () =>
        {
            var stage = (CompositeDrawable)Playfield.Stage;
            var children = aliveInternalChildren(stage);
            var backgroundIndex = childIndexContaining<LegacyBmsColumnBackground>(stage);
            var hitTargetIndex = childIndexContaining<LegacyBmsHitTarget>(stage);
            var columnLightIndex = childIndexContaining<LegacyBmsColumnLight>(stage);
            var measureLineIndex = children.Select((child, index) => (child, index)).Single(pair => pair.child == Playfield.Stage.MeasureLineArea).index;
            var noteColumnIndex = childIndexContaining<BmsColumn>(stage);

            return backgroundIndex < hitTargetIndex
                   && hitTargetIndex < columnLightIndex
                   && columnLightIndex < measureLineIndex
                   && measureLineIndex < noteColumnIndex;
        });

        AddAssert("legacy column backgrounds render per column",
            () => Playfield.ChildrenOfType<LegacyBmsColumnBackground>().Count(c => c.IsLoaded && c.DrawWidth > 0),
            () => Is.EqualTo(BmsTestLegacySkin.COLUMN_COUNT));
        AddAssert("column gaps contain only configured spacing", () =>
            Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT - 1).All(c =>
                Precision.AlmostEquals(
                    ((Drawable)Playfield.Stage.Columns[c]).Margin.Right + ((Drawable)Playfield.Stage.Columns[c + 1]).Margin.Left,
                    BmsTestLegacySkin.ExpectedRightColumnSpacing(c) * 2)));
        AddAssert("legacy key areas render per column",
            () => Playfield.ChildrenOfType<LegacyBmsKeyArea>().Count(c => c.IsLoaded && c.DrawWidth > 0),
            () => Is.EqualTo(BmsTestLegacySkin.COLUMN_COUNT));
        AddAssert("legacy hit target renders once",
            () => Playfield.ChildrenOfType<LegacyBmsHitTarget>().Count(c => c.IsLoaded && c.DrawWidth > 0),
            () => Is.EqualTo(1));
        AddAssert("hit target texture spans the stage", () =>
        {
            var stageQuad = Playfield.Stage.ScreenSpaceDrawQuad;
            var targetQuad = Playfield.ChildrenOfType<LegacyBmsHitTarget>()
                .Single(c => c.IsAlive && c.IsLoaded && c.DrawWidth > 0)
                .Target.ScreenSpaceDrawQuad;

            return new[]
            {
                targetQuad.TopLeft.X - stageQuad.TopLeft.X,
                targetQuad.TopRight.X - stageQuad.TopRight.X,
            };
        }, () => Is.All.InRange(-1f, 1f));

        // The down-state key image (mania-keyND / KeyImageDown) must exist alongside the up-state,
        // and key presses during the autoplay replay must actually drive it (PressCount > 0).
        AddAssert("key area has up and down key sprites", () =>
            Playfield.ChildrenOfType<LegacyBmsKeyArea>()
                .All(c => c.ChildrenOfType<Sprite>().Count(s => s.DrawHeight > 0) >= 2));
    }

    private T? getConfig<T>(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponents component, int? column, ISkinSource source)
        where T : struct
    {
        var skinLookup = column.HasValue
            ? new BmsSkinConfigurationLookup(lookup, new BmsSkinComponentLookup(component, BmsLayoutVariant.Bme7K, column.Value))
            : new BmsSkinConfigurationLookup(lookup);

        return source.GetConfig<BmsSkinConfigurationLookup, T>(skinLookup)?.Value;
    }

    private string getConfigString(LegacyManiaSkinConfigurationLookups lookup, BmsSkinComponents component, int? column, ISkinSource source)
    {
        var skinLookup = column.HasValue
            ? new BmsSkinConfigurationLookup(lookup, new BmsSkinComponentLookup(component, BmsLayoutVariant.Bme7K, column.Value))
            : new BmsSkinConfigurationLookup(lookup);

        return source.GetConfig<BmsSkinConfigurationLookup, string>(skinLookup)?.Value;
    }

    private DrawableBmsHitObject liveSkinCoverageLongNote(int column)
    {
        var hitObject = ((BmsBeatmap)Player.GameplayState.Beatmap).HitObjects.OfType<BmsLongNote>().Single(h =>
            h.Column == column && h.StartTime == BmsTestBeatmaps.LN_SKIN_COVERAGE_START_TIME);

        return Playfield.GetAliveObjectAtTime(hitObject.StartTime);
    }

    private static BmsSegmentedLongNoteBody longNoteBodyOf(DrawableBmsHitObject longNote)
    {
        if (longNote == null)
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (BmsSegmentedLongNoteBody)longNote.GetType().GetField("longNoteBody", flags)!.GetValue(longNote)!;
    }

    private static int bodyAnimationFrameCount(BmsSegmentedLongNoteBody body)
    {
        if (body == null)
            return 0;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return ((Array)body.GetType().GetField("bodyFrames", flags)!.GetValue(body)!).Length;
    }

    private static int currentBodyAnimationFrame(BmsSegmentedLongNoteBody body)
    {
        if (body == null)
            return -1;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (int)body.GetType().GetField("currentFrameIndex", flags)!.GetValue(body)!;
    }

    private static IReadOnlyList<Drawable> aliveInternalChildren(CompositeDrawable drawable)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        return (IReadOnlyList<Drawable>)typeof(CompositeDrawable).GetProperty("AliveInternalChildren", flags)!.GetValue(drawable)!;
    }

    private static int childIndexContaining<T>(CompositeDrawable drawable)
        where T : Drawable
    {
        var match = aliveInternalChildren(drawable).Select((child, index) => (child, index)).SingleOrDefault(pair => pair.child.ChildrenOfType<T>().Any());
        return match.child == null ? -1 : match.index;
    }

    [Test]
    public void TestArgonSkin() => createSkinScene(BmsTestSkins.SkinKind.Argon);

    [Test]
    public void TestClassicSkin()
    {
        createSkinScene(BmsTestSkins.SkinKind.Classic);
        // The Classic skin has no mania-key of its own; the embedded fallback must render it
        // (textures load asynchronously from DLL resources, so wait rather than assert immediately).
        AddUntilStep("legacy key area container renders per column", () =>
            Playfield.ChildrenOfType<LegacyBmsKeyArea>().Count(c => c.IsLoaded && c.DrawWidth > 0) == BmsTestLegacySkin.COLUMN_COUNT);
        AddUntilStep("classic mania-key texture is loaded and visible", () =>
            Playfield.ChildrenOfType<LegacyBmsKeyArea>().All(c =>
                c.ChildrenOfType<Sprite>().Any(s => s.Alpha > 0 && s.DrawHeight > 0)));
        // The down-state (mania-keyND) must load too, and key presses during autoplay must drive it.
        AddUntilStep("classic key area has up and down key sprites", () =>
            Playfield.ChildrenOfType<LegacyBmsKeyArea>()
                .All(c => c.ChildrenOfType<Sprite>().Count(s => s.DrawHeight > 0) >= 2));
    }

    /// <summary>
    /// Loads a synthetic legacy skin whose <c>skin.ini</c> sets every BMS-supported key to a
    /// distinct value, then asserts that the runtime both resolves those values via
    /// <see cref="BmsSkinConfigurationLookup"/> and renders the legacy gameplay drawables
    /// (columns, keys, hit target, explosion, notes) from them — covering the full skin.ini
    /// surface that the Argon/Classic tests leave unexercised.
    /// </summary>
    [Test]
    public void TestLegacySkin()
    {
        createSkinScene(BmsTestSkins.SkinKind.Legacy);
        assertLegacySkinConfigResolves();
        assertLegacySkinRenders();
    }

    [Test]
    public void TestLegacySkinKeysUnderNotes()
    {
        createSkinScene(BmsTestSkins.SkinKind.LegacyKeysUnderNotes);

        AddAssert("keys under notes config resolves",
            () => getConfig<bool>(LegacyManiaSkinConfigurationLookups.KeysUnderNotes, BmsSkinComponents.ColumnBackground, null, ((BmsTestSkins.SkinnedTestPlayer)Player).SkinSource),
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedKeysUnderNotes(keysUnderNotes: true)));

        AddAssert("key areas render below notes", () =>
            Playfield.Stage.Columns.All(column =>
            {
                var drawable = (CompositeDrawable)column;
                var children = aliveInternalChildren(drawable);
                var keyAreaIndex = childIndexContaining<LegacyBmsKeyArea>(drawable);
                var hitObjectIndex = children.Select((child, index) => (child, index)).Single(pair => pair.child == column.HitObjectContainer).index;

                return keyAreaIndex < hitObjectIndex;
            }));
    }

    [Test]
    public void TestLegacySkinFallsBackColumnLightTexture()
    {
        createSkinScene(BmsTestSkins.SkinKind.LegacyMissingColumnLightTexture);

        AddAssert("column lights use embedded fallback texture", () =>
            Playfield.ChildrenOfType<LegacyBmsColumnLight>().All(light =>
                light.ChildrenOfType<Sprite>().Any(sprite => sprite.Texture != null && sprite.DrawHeight > 0)));
    }

}
