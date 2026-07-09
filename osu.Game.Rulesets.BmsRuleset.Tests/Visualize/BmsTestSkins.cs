#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Game.IO;
using osu.Game.Replays;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
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
        => kind == SkinKind.Legacy
            ? BmsTestLegacySkin.CreateSkinSource(resources)
            : kind == SkinKind.LegacyKeysUnderNotes
                ? BmsTestLegacySkin.CreateSkinSource(resources, keysUnderNotes: true)
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
        Func<BmsBeatmap, IList<ReplayFrame>>? createReplay)
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

            DrawableRuleset?.SetReplayScore(new Score
            {
                Replay = new Replay { Frames = createReplay(beatmap).ToList() },
            });
        }
    }
}
