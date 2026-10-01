#nullable enable
using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Components;
using osu.Game.Rulesets.Replays;
using osu.Game.Screens.Play;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Shared base for BMS visual player scenes. Centralises the boilerplate
/// <see cref="IStorageResourceProvider"/> implementation, the resolved
/// <see cref="GameHost"/>, and common player/playfield accessors.
/// </summary>
public abstract partial class BmsPlayerTestScene : PlayerTestScene, IStorageResourceProvider
{

    protected override bool HasCustomSteps => true;

    protected override double TimePerAction => 0;

    /// <summary>
    /// The currently-loaded <see cref="BmsPlayfield"/>. Must be called after the player is loaded.
    /// </summary>
    protected BmsPlayfield Playfield => (BmsPlayfield)Player.DrawableRuleset.Playfield;

    // Fixed-plan execution scenes supply numerical contexts; input-generation scenes use actual input events.
    private protected PressOutcome PressColumn(IBmsColumn column, double inputTime, bool reverseScratch = false)
    {
        var source = (BmsColumn)column;
        return source.Judgements.HandleNotePress(new BmsPressContext(inputTime, frameFor(source), reverseScratch, Playfield.IsResumeRewinding),
            Playfield.JudgementAlgorithm);
    }

    private protected void ReleaseColumn(IBmsColumn column, double inputTime, bool reverseScratch = false)
    {
        var source = (BmsColumn)column;
        source.Judgements.HandleExecutionRelease(inputTime, frameFor(source), reverseScratch);
    }

    private static BmsFrameContext frameFor(BmsColumn column)
        => new(column.Time.Current, (column.Clock as IGameplayClock)?.GetTrueGameplayRate() ?? column.Clock.Rate,
            (column.Clock as IGameplayClock)?.IsRewinding == true || column.Time.Elapsed < 0);

    [Resolved]
    protected GameHost Host { get; private set; } = null!;

    protected override Ruleset CreatePlayerRuleset() => new BmsRuleset();

    /// <summary>
    /// Creates a <see cref="BmsTestSkins.SkinnedTestPlayer"/> backed by the requested skin.
    /// Pass <c>null</c> for <paramref name="createReplay"/> to disable replay entirely
    /// (notes scroll, manual input still possible).
    /// </summary>
    protected BmsTestSkins.SkinnedTestPlayer CreateBmsPlayer(
        Func<BmsBeatmap, IList<ReplayFrame>>? createReplay,
        BmsTestSkins.SkinKind skin = BmsTestSkins.SkinKind.Argon)
        => new(BmsTestSkins.CreateSkinSource(skin, this), createReplay);

    #region IStorageResourceProvider

    IRenderer IStorageResourceProvider.Renderer => Host.Renderer;

    AudioManager IStorageResourceProvider.AudioManager => Audio;

    IResourceStore<byte[]> IStorageResourceProvider.Files => null!;

    IResourceStore<byte[]> IStorageResourceProvider.Resources => base.Resources;

    RealmAccess IStorageResourceProvider.RealmAccess => null!;

    IResourceStore<TextureUpload> IStorageResourceProvider.CreateTextureLoaderStore(IResourceStore<byte[]> underlyingStore) =>
        Host.CreateTextureLoaderStore(underlyingStore);

    #endregion

}
