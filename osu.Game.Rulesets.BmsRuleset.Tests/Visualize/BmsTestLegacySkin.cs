using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.IO;
using osu.Game.Rulesets.BmsRuleset.Beatmaps;
using osu.Game.Rulesets.BmsRuleset.BmsParser;
using osu.Game.Rulesets.BmsRuleset.Skinning.Legacy;
using osu.Game.Skinning;
using osuTK.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Skin = osu.Game.Skinning.Skin;

namespace osu.Game.Rulesets.BmsRuleset.Tests.Visualize;

/// <summary>
/// A synthetic legacy BMS skin that sets every skin.ini key supported by the BMS skin
/// configuration to a distinct, assertable value, backed by programmatically-generated
/// solid-colour textures. Lets <see cref="TestSceneBmsSkins.TestLegacySkin"/> exercise the
/// full skin.ini surface area (per-column colours, key up/down, explosion, stage images,
/// judgement images, positions, layout) through the visual player, rather than only the
/// built-in Argon/Classic skins.
/// </summary>
/// <remarks>
/// Values are deliberately chosen to be visually distinguishable on inspection: column
/// backgrounds are light and semi-transparent so the vivid note textures contrast strongly
/// over them, per-column light colours are saturated, and every position/spacing/line-width
/// is distinct so a regression that collapses two values is immediately obvious.
/// </remarks>
public static class BmsTestLegacySkin
{
    // The visual test beatmap is always Bme7K (8 lanes incl. scratch) — see BmsTestBeatmaps.CreateBeatmap.
    // The transformer is locked to that layout so the [BMS] Layout: 7K section matches; a mismatch would
    // make the section never match and the custom skin.ini silently fall back to the embedded default.
    public const int COLUMN_COUNT = 8;

    public const string EXPLOSION_IMAGE = "lightingN";
    public const string HOLD_NOTE_LIGHT_IMAGE = "lightingL";
    public const string HIT_TARGET_IMAGE = "mania-stage-hint";
    public const string LEFT_STAGE_IMAGE = "stage-left";
    public const string RIGHT_STAGE_IMAGE = "stage-right";
    public const string BOTTOM_STAGE_IMAGE = "stage-bottom";
    public const string LIGHT_IMAGE = "stage-light";
    public const string HOLD_NOTE_BODY_IMAGE = "note-ln-body";
    public const string HOLD_NOTE_TAIL_IMAGE = "note-ln-tail";
    public const string MINE_IMAGE = "note-mine";
    public const string JUDGEMENT_PGREAT_IMAGE = "j-pgreat";
    public const string JUDGEMENT_GREAT_IMAGE = "j-great";
    public const string JUDGEMENT_GOOD_IMAGE = "j-good";
    public const string JUDGEMENT_BAD_IMAGE = "j-bad";
    public const string JUDGEMENT_POOR_IMAGE = "j-poor";

    public static readonly int[] ANIMATED_NOTE_COLUMNS = [1, 6];
    public static readonly int[] ANIMATED_HOLD_BODY_COLUMNS = [1, 2, 3];
    public static readonly int[] ANIMATED_HOLD_TAIL_COLUMNS = [7];
    public static readonly int[] ONE_PIXEL_TAIL_COLUMNS = [4, 5];

    public static Color4 ExpectedColumnLineColour => toColour(column_line_colour);

    public static Color4 ExpectedJudgementLineColour => toColour(judgement_line_colour);

    public static Color4 ExpectedBarLineColour => toColour(barline_colour);

    public static Color4 ExpectedComboBreakColour => toColour(combo_break_colour);

    // The 480 is the skin.ini coordinate-space height hardcoded in BmsSkinConfiguration.getPositionFromBottom.
    public static float ExpectedHitPosition => (480 - hit_position) * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    public static float ExpectedLightPosition => (480 - light_position) * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    public static float ExpectedScorePosition => score_position * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    public static float ExpectedComboPosition => combo_position * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    public static float ExpectedBarlineHeight => barline_height;

    public static float ExpectedWidthForNoteHeightScale => width_for_note_height_scale * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    public static float ExpectedMinimumColumnWidth => column_width.Min() * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    public static int ExpectedLightFramePerSecond => light_frame_per_second;

    public static bool ExpectedShowJudgementLine => true;

    public static bool ExpectedKeysUnderNotes(bool keysUnderNotes = false) => keysUnderNotes;

    // Each position is deliberately distinct so the test can tell them apart. HitPosition/LightPosition
    // are distance-from-bottom (480 - value, scaled); ScorePosition/ComboPosition are plain scaled values.
    private const float hit_position = 440;
    private const float light_position = 350;
    private const float score_position = 250;
    private const float combo_position = 300;
    private const int light_frame_per_second = 40;
    private const float width_for_note_height_scale = 50;
    private const float barline_height = 1.2f;

    // Per-column spacing and line widths are all distinct so the per-column layout lookups resolve
    // to visibly different values (and regressions that alias them are caught).
    private static readonly float[] column_spacing = [3, 6, 9, 12, 15, 18, 21];

    private static readonly float[] column_line_width = [1, 2, 3, 4, 5, 6, 7, 8, 9];

    // Distinct per-column widths so the ColumnWidth config is visibly reflected (not a uniform row).
    private static readonly float[] column_width = [60, 32, 40, 48, 56, 64, 72, 80];

    // Light, semi-transparent column backgrounds — vivid opaque notes contrast strongly over them.
    private static readonly (byte r, byte g, byte b, byte a)[] column_bg_colour =
    [
        (255, 220, 220, 90), (220, 255, 220, 90), (220, 230, 255, 90), (255, 255, 220, 90),
        (255, 220, 255, 90), (220, 255, 255, 90), (255, 235, 220, 90), (235, 220, 255, 90),
    ];

    // Saturated per-column light colours — the tint applied to the hit explosion and key glow.
    private static readonly (byte r, byte g, byte b)[] column_light_colour =
    [
        (255, 255, 0), (255, 0, 255), (0, 255, 255), (255, 128, 0),
        (128, 0, 255), (0, 255, 128), (255, 0, 128), (128, 255, 0),
    ];

    // Vivid opaque note/key textures — they pop against the light transparent column backgrounds.
    private static readonly (byte r, byte g, byte b)[] note_colour =
    [
        (220, 0, 0), (0, 180, 0), (0, 0, 220), (220, 220, 0),
        (220, 0, 220), (0, 180, 180), (220, 110, 0), (110, 0, 220),
    ];

    // Each animated LN body column uses a separate palette so visual-test failures are attributable
    // to a lane at a glance instead of every animated body looking like the same asset.
    private static readonly Dictionary<int, ((byte r, byte g, byte b) frame0, (byte r, byte g, byte b) frame1)> hold_body_animation_colours = new()
    {
        [1] = ((255, 80, 80), (255, 180, 80)),
        [2] = ((80, 255, 120), (80, 200, 255)),
        [3] = ((180, 80, 255), (255, 80, 200)),
    };

    private static readonly (byte r, byte g, byte b, byte a) column_line_colour = (255, 255, 255, 50);
    private static readonly (byte r, byte g, byte b, byte a) judgement_line_colour = (255, 255, 255, 255);
    private static readonly (byte r, byte g, byte b, byte a) barline_colour = (0, 255, 0, 255);
    private static readonly (byte r, byte g, byte b, byte a) combo_break_colour = (255, 0, 0, 255);

    public static string NoteImage(int column) => $"note{column}";

    public static string HoldNoteBodyImage(int column) => ANIMATED_HOLD_BODY_COLUMNS.Contains(column) ? $"note{column}-ln-body" : HOLD_NOTE_BODY_IMAGE;

    public static string HoldNoteTailImage(int column) => ANIMATED_HOLD_TAIL_COLUMNS.Contains(column)
        ? $"note{column}-ln-tail"
        : ONE_PIXEL_TAIL_COLUMNS.Contains(column)
            ? $"note{column}-ln-tail-1x1"
            : HOLD_NOTE_TAIL_IMAGE;

    public static string KeyImage(int column) => $"key{column}";

    public static string KeyImageDown(int column) => $"key{column}D";

    public static ISkinSource CreateSkinSource(IStorageResourceProvider resources, bool keysUnderNotes = false, bool includeColumnLightTexture = true)
    {
        var skin = new TestLegacyBmsSkin(resources, buildResources(keysUnderNotes, includeColumnLightTexture));
        return new SkinProvidingContainer(new BmsLegacySkinTransformer(skin, new BmsBeatmap { LayoutVariant = BmsLayoutVariant.Bme7K }));
    }

    // Expected resolved values — mirroring the skin.ini parsing/scaling so the test asserts what
    // the runtime actually resolves rather than hand-picked magic numbers.

    public static Color4 ExpectedColumnBackgroundColour(int column) => toColour(column_bg_colour[column]);

    public static Color4 ExpectedColumnLightColour(int column) => toColour(column_light_colour[column]);

    public static float ExpectedColumnWidth(int column) => column_width[column] * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR;

    // Spacing is scaled by POSITION_SCALE_FACTOR then halved (each gap is split left/right); line widths are raw.
    // Left spacing is null for the leftmost column (no gap to its left), so it is only asserted for columns >= 1.
    public static float ExpectedLeftColumnSpacing(int column) => column_spacing[column - 1] * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR / 2;

    public static float ExpectedRightColumnSpacing(int column) => column_spacing[column] * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR / 2;

    public static float ExpectedLeftLineWidth(int column) => column_line_width[column];

    public static float ExpectedRightLineWidth(int column) => column_line_width[column + 1];

    private static string buildSkinIni(bool keysUnderNotes, bool includeColumnLightTexture)
    {
        var s = new StringBuilder();
        s.AppendLine("[General]");
        s.AppendLine("Name: TestLegacy");
        s.AppendLine("Version: 2.7");
        s.AppendLine();
        s.AppendLine("[BMS]");
        s.AppendLine("Layout: 7K");
        s.AppendLine();
        s.AppendLine($"HitPosition: {ini(hit_position)}");
        s.AppendLine($"LightPosition: {ini(light_position)}");
        s.AppendLine($"ScorePosition: {ini(score_position)}");
        s.AppendLine($"ComboPosition: {ini(combo_position)}");
        s.AppendLine("JudgementLine: 1");
        s.AppendLine();
        s.AppendLine($"LightFramePerSecond: {light_frame_per_second}");
        s.AppendLine($"ColumnWidth: {string.Join(',', column_width.Select(ini))}");
        s.AppendLine($"ColumnSpacing: {string.Join(',', column_spacing.Select(ini))}");
        s.AppendLine($"ColumnLineWidth: {string.Join(',', column_line_width.Select(ini))}");
        s.AppendLine($"WidthForNoteHeightScale: {ini(width_for_note_height_scale)}");
        s.AppendLine($"BarlineHeight: {ini(barline_height)}");
        s.AppendLine($"KeysUnderNotes: {(keysUnderNotes ? 1 : 0)}");
        s.AppendLine();
        s.AppendLine($"ColourBarline: {toIniColour(barline_colour)}");
        s.AppendLine($"ColourColumnLine: {toIniColour(column_line_colour)}");
        s.AppendLine($"ColourJudgementLine: {toIniColour(judgement_line_colour)}");
        s.AppendLine($"ColourBreak: {toIniColour(combo_break_colour)}");
        s.AppendLine();
        for (var c = 0; c < COLUMN_COUNT; c++)
            s.AppendLine($"ColourLight{c + 1}: {toIniColour(column_light_colour[c])}");
        for (var c = 0; c < COLUMN_COUNT; c++)
            s.AppendLine($"Colour{c + 1}: {toIniColour(column_bg_colour[c])}");
        s.AppendLine();
        for (var c = 0; c < COLUMN_COUNT; c++)
            s.AppendLine($"NoteImage{c}: {NoteImage(c)}");
        foreach (var c in ANIMATED_HOLD_BODY_COLUMNS)
            s.AppendLine($"NoteImage{c}L: {HoldNoteBodyImage(c)}");
        foreach (var c in ANIMATED_HOLD_TAIL_COLUMNS)
            s.AppendLine($"NoteImage{c}T: {HoldNoteTailImage(c)}");
        foreach (var c in ONE_PIXEL_TAIL_COLUMNS)
            s.AppendLine($"NoteImage{c}T: {HoldNoteTailImage(c)}");
        s.AppendLine($"NoteImageL: {HOLD_NOTE_BODY_IMAGE}");
        s.AppendLine($"NoteImageT: {HOLD_NOTE_TAIL_IMAGE}");
        s.AppendLine($"MineImage: {MINE_IMAGE}");
        s.AppendLine();
        for (var c = 0; c < COLUMN_COUNT; c++)
        {
            s.AppendLine($"KeyImage{c}: {KeyImage(c)}");
            s.AppendLine($"KeyImage{c}D: {KeyImageDown(c)}");
        }

        s.AppendLine();
        s.AppendLine($"LightingN: {EXPLOSION_IMAGE}");
        s.AppendLine($"LightingL: {HOLD_NOTE_LIGHT_IMAGE}");
        s.AppendLine();
        s.AppendLine($"StageHint: {HIT_TARGET_IMAGE}");
        s.AppendLine($"StageLeft: {LEFT_STAGE_IMAGE}");
        s.AppendLine($"StageRight: {RIGHT_STAGE_IMAGE}");
        s.AppendLine($"StageBottom: {BOTTOM_STAGE_IMAGE}");
        if (includeColumnLightTexture)
            s.AppendLine($"StageLight: {LIGHT_IMAGE}");
        s.AppendLine();
        s.AppendLine($"HitPGreat: {JUDGEMENT_PGREAT_IMAGE}");
        s.AppendLine($"HitGreat: {JUDGEMENT_GREAT_IMAGE}");
        s.AppendLine($"HitGood: {JUDGEMENT_GOOD_IMAGE}");
        s.AppendLine($"HitBad: {JUDGEMENT_BAD_IMAGE}");
        s.AppendLine($"HitPoor: {JUDGEMENT_POOR_IMAGE}");
        return s.ToString();
    }

    private static Dictionary<string, byte[]> buildResources(bool keysUnderNotes, bool includeColumnLightTexture)
    {
        var dict = buildTextures(includeColumnLightTexture);
        dict["skin.ini"] = Encoding.UTF8.GetBytes(buildSkinIni(keysUnderNotes, includeColumnLightTexture));
        return dict;
    }

    private static Dictionary<string, byte[]> buildTextures(bool includeColumnLightTexture)
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        for (var c = 0; c < COLUMN_COUNT; c++)
        {
            var note = note_colour[c];

            if (ANIMATED_NOTE_COLUMNS.Contains(c))
            {
                dict[$"{NoteImage(c)}-0"] = solidPng(note.r, note.g, note.b);
                dict[$"{NoteImage(c)}-1"] = solidPng((byte)Math.Min(255, note.r + 35), (byte)Math.Min(255, note.g + 35), (byte)Math.Min(255, note.b + 35));
            }
            else
                dict[NoteImage(c)] = solidPng(note.r, note.g, note.b);

            dict[KeyImage(c)] = solidPng(note.r, note.g, note.b);
            // "Down" key state uses a high-contrast white so the pressed state is unmistakable
            // when the autoplay replay presses a key (the up-state is vivid coloured).
            dict[KeyImageDown(c)] = solidPng(255, 255, 255);
        }

        foreach (var c in ANIMATED_HOLD_BODY_COLUMNS)
        {
            var (frame0, frame1) = hold_body_animation_colours[c];
            dict[$"{HoldNoteBodyImage(c)}-0"] = solidPng(frame0.r, frame0.g, frame0.b);
            dict[$"{HoldNoteBodyImage(c)}-1"] = solidPng(frame1.r, frame1.g, frame1.b);
        }

        foreach (var c in ONE_PIXEL_TAIL_COLUMNS)
            dict[HoldNoteTailImage(c)] = solidPng(255, 255, 255, 1);

        foreach (var c in ANIMATED_HOLD_TAIL_COLUMNS)
        {
            dict[$"{HoldNoteTailImage(c)}-0"] = solidPng(255, 255, 255);
            dict[$"{HoldNoteTailImage(c)}-1"] = solidPng(255, 120, 255);
        }

        dict[HOLD_NOTE_BODY_IMAGE] = solidPng(200, 200, 200);
        dict[HOLD_NOTE_TAIL_IMAGE] = solidPng(160, 160, 160);
        dict[MINE_IMAGE] = solidPng(255, 128, 0);
        // Larger explosion textures so the hit flash (the column light) is clearly visible, not a speck.
        // Explosion and column-light are provided as N-suffix frame animations (lightingN-0/1/2,
        // stage-light-0/1) so the test exercises the multi-frame animation path most skins rely on.
        dict[$"{EXPLOSION_IMAGE}-0"] = solidPng(255, 80, 80, 64);
        dict[$"{EXPLOSION_IMAGE}-1"] = solidPng(80, 255, 80, 64);
        dict[$"{EXPLOSION_IMAGE}-2"] = solidPng(80, 80, 255, 64);
        dict[HOLD_NOTE_LIGHT_IMAGE] = solidPng(255, 255, 200, 64);
        dict[HIT_TARGET_IMAGE] = solidPng(200, 200, 80);
        // Distinct greys for the three stage panels so they don't blur together on inspection.
        dict[LEFT_STAGE_IMAGE] = solidPng(70, 70, 70);
        dict[RIGHT_STAGE_IMAGE] = solidPng(110, 110, 110);
        dict[BOTTOM_STAGE_IMAGE] = solidPng(40, 40, 40);
        if (includeColumnLightTexture)
        {
            dict[$"{LIGHT_IMAGE}-0"] = solidPng(255, 255, 0, 16, 120);
            dict[$"{LIGHT_IMAGE}-1"] = solidPng(255, 160, 0, 16, 120);
        }

        dict[JUDGEMENT_PGREAT_IMAGE] = solidPng(255, 255, 0);
        dict[JUDGEMENT_GREAT_IMAGE] = solidPng(255, 200, 0);
        dict[JUDGEMENT_GOOD_IMAGE] = solidPng(0, 255, 0);
        dict[JUDGEMENT_BAD_IMAGE] = solidPng(255, 0, 0);
        dict[JUDGEMENT_POOR_IMAGE] = solidPng(128, 128, 128);

        return dict;
    }

    private static string ini(float f) => f.ToString(CultureInfo.InvariantCulture);

    // The byte constructor is used deliberately so the resulting Color4 matches what
    // BmsSkinConfigurationDecoder.tryParseColour produces (it parses components as bytes).
    private static Color4 toColour((byte r, byte g, byte b) c) => new(c.r, c.g, c.b, byte.MaxValue);

    private static Color4 toColour((byte r, byte g, byte b, byte a) c) => new(c.r, c.g, c.b, c.a);

    private static string toIniColour((byte r, byte g, byte b) c) => $"{c.r},{c.g},{c.b}";

    private static string toIniColour((byte r, byte g, byte b, byte a) c) => c.a == byte.MaxValue ? $"{c.r},{c.g},{c.b}" : $"{c.r},{c.g},{c.b},{c.a}";

    private static byte[] solidPng(byte r, byte g, byte b, int size = 16)
    {
        using var image = new Image<Rgba32>(size, size, new Rgba32(r, g, b, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    // Non-square variant for tall strips (e.g. the column light, whose height is the texture height).
    private static byte[] solidPng(byte r, byte g, byte b, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(r, g, b, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private sealed class TestLegacyBmsSkin(IStorageResourceProvider resourcesProvider, Dictionary<string, byte[]> resources)
        : Skin(new SkinInfo("TestLegacy", "Test"), resourcesProvider, new ByteResourceStore(resources))
    {
        private readonly IRenderer renderer = resourcesProvider.Renderer;
        private readonly IReadOnlyDictionary<string, byte[]> resources = resources;

        public override Drawable GetDrawableComponent(ISkinComponentLookup lookup) => null;

        public override Texture GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
        {
            if (!resources.TryGetValue(componentName, out var bytes))
                return null;

            return Texture.FromStream(renderer, new MemoryStream(bytes));
        }

        public override ISample GetSample(ISampleInfo sampleInfo) => null;

        public override IBindable<TValue> GetConfig<TLookup, TValue>(TLookup lookup)
        {
            // A legacy version keeps LegacySkinTransformer's base legacy checks happy for non-BMS
            // lookups; the [BMS] skin.ini section alone is what activates the BMS legacy path via
            // BmsLegacySkinTransformer.hasBmsResources.
            if (lookup is SkinConfiguration.LegacySetting legacy && legacy == SkinConfiguration.LegacySetting.Version)
                return SkinUtils.As<TValue>(new Bindable<decimal>(2.7m));

            return null;
        }
    }

    private sealed class ByteResourceStore(Dictionary<string, byte[]> resources)
        : IResourceStore<byte[]>
    {

        #region Disposal

        public void Dispose()
        {
        }

        #endregion

        public byte[] Get(string name) => resources.GetValueOrDefault(name);

        public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

        public Stream GetStream(string name) => Get(name) is { } bytes ? new MemoryStream(bytes) : null;

        public IEnumerable<string> GetAvailableResources() => resources.Keys;
    }
}
