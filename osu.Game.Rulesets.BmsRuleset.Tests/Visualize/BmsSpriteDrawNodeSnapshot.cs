#nullable enable

using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.BmsRuleset.UI.Gameplay.Drawables;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

// Read the applied node, never its mutable source, so comparisons also check buffer isolation.
internal static class BmsSpriteDrawNodeSnapshot
{
    internal static bool IsSprite(DrawNode node)
        => node is SpriteDrawNode || node.GetType().DeclaringType == typeof(BmsResolvedNoteSprite);

    internal static Quad ReadQuad(DrawNode node) => Read<Quad>(node, "ScreenSpaceDrawQuad");

    internal static Texture? ReadTexture(DrawNode node) => Read<Texture?>(node, "Texture");

    internal static T Read<T>(DrawNode node, string name)
        => (T)node.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node)!;
}
