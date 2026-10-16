#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.Extensions;
using osu.Game.IO;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Configuration;
using osu.Game.Rulesets.BmsRuleset.Gameplay.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Replays;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// Skin source plus a <see cref="TestPlayer"/> that injects an <see cref="ISkinSource"/>
/// and optionally replays caller-supplied frames.
/// </summary>
public static partial class BmsTestSkins
{

    /// <summary>
    /// Creates an <see cref="ISkinSource"/> for the given <see cref="SkinKind"/>.
    /// </summary>
    public static ISkinSource CreateSkinSource(SkinKind kind, IStorageResourceProvider resources)
        => Environment.GetEnvironmentVariable("BMS_LIFETIME_SKIN") is { Length: > 0 } storage
            ? BmsLifetimeLocalSkin.Create(resources, storage)
            : kind == SkinKind.Legacy
                ? BmsTestLegacySkin.CreateSkinSource(resources)
                : kind == SkinKind.LegacyKeysUnderNotes
                    ? BmsTestLegacySkin.CreateSkinSource(resources, keysUnderNotes: true)
                    : kind == SkinKind.LegacyMissingColumnLightTexture
                        ? BmsTestLegacySkin.CreateSkinSource(resources, includeColumnLightTexture: false)
                        : new SkinProvidingContainer(kind == SkinKind.Classic
                            ? new DefaultLegacySkin(resources)
                            : new ArgonSkin(resources));

    public enum SkinKind
    {
        Argon,
        Classic,

        // A synthetic legacy skin with a comprehensive skin.ini; see BmsTestLegacySkin.
        Legacy,
        LegacyKeysUnderNotes,
        LegacyMissingColumnLightTexture,
    }

    /// <inheritdoc />
    /// <summary>
    /// A <see cref="T:osu.Game.Tests.Visual.TestPlayer">TestPlayer</see> that caches an <see cref="T:osu.Game.Skinning.ISkinSource">ISkinSource</see> and optionally
    /// uses a caller-supplied function to generate replay frames.
    /// Pass <c>null</c> for <paramref name="createReplay" /> to disable replay entirely
    /// (notes scroll without being hit, manual input still possible).
    /// </summary>
    public partial class SkinnedTestPlayer(
        ISkinSource skinSource,
        Func<BmsBeatmap, IList<ReplayFrame>>? createReplay,
        BmsJudgementAlgorithm? replayAlgorithm = null,
        IReadOnlyList<Mod>? replayMods = null)
        : TestPlayer(allowPause: false, showResults: false)
    {

        /// <summary>
        /// The cached skin source, exposed so visual scenes can assert against the active skin's
        /// config and drawable factories (e.g. <see cref="TestSceneBmsSkins.TestLegacySkin"/>).
        /// </summary>
        public ISkinSource SkinSource => skinSource;

        [Cached(typeof(ISkinSource))]
        private readonly ISkinSource skinSource = skinSource;

        protected override void PrepareReplay()
        {
            if (createReplay == null) return;

            var beatmap = (BmsBeatmap)GameplayState.Beatmap;
            var frames = createReplay(beatmap).ToList();
            if (frames.FirstOrDefault() is BmsReplayFrame first && replayAlgorithm is { } algorithm)
                frames[0] = new BmsReplayFrame(first.Time, first.Actions.ToArray()) { Algorithm = algorithm };

            DrawableRuleset?.SetReplayScore(new Score
            {
                Replay = new Replay { Frames = frames },
                ScoreInfo = new ScoreInfo
                {
                    Mods = replayMods?.Select(mod => mod.DeepClone()).ToArray() ?? [],
                },
            });
        }
    }
}
