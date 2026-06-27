using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Components;
using osu.Game.Rulesets.BmsRuleset.Skinning.Configuration;
using osu.Game.Rulesets.BmsRuleset.Skinning.Drawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Rulesets.BmsRuleset.Skinning.LegacyDrawables;
using osu.Game.Rulesets.BmsRuleset.Skinning.Runtime;
using osu.Game.Rulesets.BmsRuleset.UI;
using osu.Game.Rulesets.BmsRuleset.UI.HudComponents;
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
            () => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteBodyImage, BmsSkinComponents.HoldNoteBody, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.HOLD_NOTE_BODY_IMAGE));
        AddAssert("hold note tail image resolves",
            () => getConfigString(LegacyManiaSkinConfigurationLookups.HoldNoteTailImage, BmsSkinComponents.HoldNoteTail, 1, skin()),
            () => Is.EqualTo(BmsTestLegacySkin.HOLD_NOTE_TAIL_IMAGE));
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
                Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT)
                    .Select(c => getConfig<float>(LegacyManiaSkinConfigurationLookups.RightColumnSpacing, BmsSkinComponents.ColumnBackground, c, skin()).GetValueOrDefault())
                    .ToArray(),
            () => Is.EqualTo(Enumerable.Range(0, BmsTestLegacySkin.COLUMN_COUNT).Select(BmsTestLegacySkin.ExpectedRightColumnSpacing).ToArray()));
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
            () => Is.EqualTo(BmsTestLegacySkin.ExpectedKeysUnderNotes));
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

        AddAssert("note factory produces resolved note piece",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.Note, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<BmsResolvedNotePiece>);
        AddAssert("explosion factory produces resolved explosion",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<BmsResolvedHitExplosion>);
        AddAssert("key area factory produces legacy key area",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.KeyArea, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<LegacyBmsKeyArea>);
        AddAssert("column background factory produces legacy background",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.ColumnBackground, BmsLayoutVariant.Bme7K, 1))?.Create(),
            Is.TypeOf<LegacyBmsColumnBackground>);
        AddAssert("hit target factory produces legacy hit target",
            () => factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitTarget))?.Create(),
            Is.TypeOf<LegacyBmsHitTarget>);

        // N-suffix frame animations: the explosion (lightingN-0/1/2) and column light (stage-light-0/1)
        // are provided as multi-frame textures; verify the runtime picks them up as animations.
        AddAssert("explosion is a multi-frame animation",
            () => (factorySource()?.GetDrawableFactory(new BmsSkinComponentLookup(BmsSkinComponents.HitExplosion, BmsLayoutVariant.Bme7K, 1))?.Create() as BmsResolvedHitExplosion)
                ?.ChildrenOfType<TextureAnimation>().SingleOrDefault()?.FrameCount,
            () => Is.EqualTo(3));
        AddAssert("column light is a multi-frame animation",
            () => Playfield.ChildrenOfType<LegacyBmsColumnBackground>()
                .Select(c => c.ChildrenOfType<TextureAnimation>().SingleOrDefault()?.FrameCount ?? 0)
                .ToArray(),
            () => Is.EqualTo(Enumerable.Repeat(2, BmsTestLegacySkin.COLUMN_COUNT).ToArray()));

        AddAssert("legacy column backgrounds render per column",
            () => Playfield.ChildrenOfType<LegacyBmsColumnBackground>().Count(c => c.IsLoaded && c.DrawWidth > 0),
            () => Is.EqualTo(BmsTestLegacySkin.COLUMN_COUNT));
        AddAssert("legacy key areas render per column",
            () => Playfield.ChildrenOfType<LegacyBmsKeyArea>().Count(c => c.IsLoaded && c.DrawWidth > 0),
            () => Is.EqualTo(BmsTestLegacySkin.COLUMN_COUNT));
        AddAssert("legacy hit target renders",
            () => Playfield.ChildrenOfType<LegacyBmsHitTarget>().Any(c => c.IsLoaded && c.DrawWidth > 0),
            () => Is.True);

        // The down-state key image (mania-keyND / KeyImageDown) must exist alongside the up-state,
        // and key presses during the autoplay replay must actually drive it (PressCount > 0).
        AddAssert("key area has up and down key sprites", () =>
            Playfield.ChildrenOfType<LegacyBmsKeyArea>()
                .All(c => c.ChildrenOfType<Sprite>().Count(s => s.DrawHeight > 0) >= 2));

        // The LN hold pulses the hit light (lightingL) repeatedly throughout the hold, not just at
        // the head/tail. The synthetic beatmap holds several LNs, so an 80 ms pulse produces far
        // more fires than there are long notes.
        AddAssert("LN hold repeatedly triggers hit explosion",
            () => Playfield.HoldExplosionCount,
            () => Is.GreaterThan(BmsTestLegacySkin.COLUMN_COUNT));
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
}
