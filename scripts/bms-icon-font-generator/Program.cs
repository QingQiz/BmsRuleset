using System.Buffers.Binary;
using System.IO.Compression;

if (args.Length != 2)
    throw new ArgumentException("Usage: generator <png path> <fnt path>");

const int glyph_size = 64;
const int height = glyph_size;
var width = 0;
byte[] pixels = [];

Action<float, float>[] glyphs =
[
    drawScratch,
    drawHideScratch,
    drawAutoScratch,
    drawAutoGauge,
    drawNoGood,
    drawNoGreat,
    drawNoMine,
];

width = glyphs.Length * glyph_size;
pixels = new byte[width * height * 4];

for (var index = 0; index < glyphs.Length; index++)
    glyphs[index](index * glyph_size + glyph_size / 2f, glyph_size / 2f);

Directory.CreateDirectory(Path.GetDirectoryName(args[0])!);
Directory.CreateDirectory(Path.GetDirectoryName(args[1])!);
File.WriteAllBytes(args[0], encodePng(width, height, pixels));
File.WriteAllBytes(args[1], encodeFont(width, glyphs.Length));

Console.WriteLine($"Generated {glyphs.Length} glyphs in a {width}x{height} page.");

void drawScratch(float cx, float cy)
{
    drawRing(cx, cy, 24, 21);
    drawRing(cx, cy, 19.5f, 9);
}

void drawHideScratch(float cx, float cy)
{
    drawScratch(cx, cy);
    drawLine(cx - 24, cy - 24, cx + 24, cy + 24, 5);
}

void drawAutoScratch(float cx, float cy)
{
    var ringX = cx - 3;
    var ringY = cy + 2;
    var gearX = cx + 15;
    var gearY = cy - 14;

    drawRing(ringX, ringY, 21, 18.5f);
    drawRing(ringX, ringY, 17, 7.5f);
    clearCircle(gearX, gearY, 8.7f);
    drawGear(gearX, gearY, 7, 3);
}

void drawAutoGauge(float cx, float cy)
{
    drawRoundedBar(cx - 22, cy - 18, 34, 6, 2);
    drawRoundedBar(cx - 22, cy - 10, 30, 6, 2);
    drawRoundedBar(cx - 22, cy - 2, 26, 6, 2);
    drawRoundedBar(cx - 22, cy + 6, 22, 6, 2);
    drawRoundedBar(cx - 22, cy + 14, 18, 6, 2);

    drawLine(cx + 17, cy - 9, cx + 17, cy + 18, 4);
    drawLine(cx + 11, cy + 12, cx + 17, cy + 18, 4);
    drawLine(cx + 23, cy + 12, cx + 17, cy + 18, 4);
}

void drawNoGood(float cx, float cy) => drawJudgementArc(cx, cy, removeGreat: false, removeGood: true);

void drawNoGreat(float cx, float cy) => drawJudgementArc(cx, cy, removeGreat: true, removeGood: true);

void drawNoMine(float cx, float cy)
{
    drawRoundedBar(cx - 20, cy - 12, 34, 34, 17);
    drawLine(cx + 7, cy - 7, cx + 12, cy - 12, 8);
    drawLine(cx + 12, cy - 14, cx + 17, cy - 19, 3.5f);

    drawLine(cx + 20, cy - 23, cx + 20, cy - 28, 3);
    drawLine(cx + 24, cy - 20, cx + 29, cy - 20, 3);
    drawLine(cx + 24, cy - 24, cx + 27, cy - 27, 3);

    // Separate the deletion stroke from the filled body so it stays readable at small sizes.
    clearLine(cx - 24, cy - 24, cx + 24, cy + 24, 10);
    drawLine(cx - 24, cy - 24, cx + 24, cy + 24, 5);
}

void drawJudgementArc(float cx, float cy, bool removeGreat, bool removeGood)
{
    const float outerRadius = 26;
    const float innerRadius = 16;
    const float separator = 0.08f;
    var arcCentreY = cy + 8;

    // A display-adjusted scale keeps the inner grades legible while retaining the timing order.
    drawSide(270, 180, [0, 50, 120, 190, 280]);
    drawSide(270, 360, [0, 50, 120, 190, 280]);

    void drawSide(float startDegrees, float endDegrees, int[] boundaries)
    {
        var max = boundaries[^1];
        var badStart = removeGreat ? boundaries[1] : removeGood ? boundaries[2] : boundaries[3];
        var regions = new[]
        {
            (start: boundaries[0], end: boundaries[1], draw: true),
            (start: boundaries[1], end: boundaries[2], draw: !removeGreat),
            (start: boundaries[2], end: boundaries[3], draw: !removeGreat && !removeGood),
            (start: badStart, end: max, draw: true),
        };

        foreach (var region in regions)
        {
            if (!region.draw || region.end <= region.start)
                continue;

            var low = region.start / (float)max;
            var high = region.end / (float)max;
            var angleStart = (startDegrees + (endDegrees - startDegrees) * low) * MathF.PI / 180;
            var angleEnd = (startDegrees + (endDegrees - startDegrees) * high) * MathF.PI / 180;
            var direction = MathF.Sign(angleEnd - angleStart);

            if (region.start > 0)
                angleStart += direction * separator;
            if (region.end < max)
                angleEnd -= direction * separator;

            drawArcBand(cx, arcCentreY, outerRadius, innerRadius, MathF.Min(angleStart, angleEnd), MathF.Max(angleStart, angleEnd));
        }
    }
}

void drawArcBand(float cx, float centreY, float outerRadius, float innerRadius, float start, float end)
{
    for (var y = (int)MathF.Floor(centreY - outerRadius - 2); y <= MathF.Ceiling(centreY + 2); y++)
    for (var x = (int)MathF.Floor(cx - outerRadius - 2); x <= MathF.Ceiling(cx + outerRadius + 2); x++)
    {
        var dx = x + 0.5f - cx;
        var dy = y + 0.5f - centreY;
        var radius = MathF.Sqrt(dx * dx + dy * dy);
        var angle = MathF.Atan2(dy, dx);
        if (angle < 0)
            angle += 2 * MathF.PI;
        if (angle < start - MathF.PI)
            angle += 2 * MathF.PI;

        var angularDistance = MathF.Max(start - angle, angle - end);
        var alpha = edge(outerRadius + 0.75f, outerRadius - 0.75f, radius)
                    * edge(innerRadius - 0.75f, innerRadius + 0.75f, radius)
                    * edge(0.02f, 0, angularDistance);
        blend(x, y, alpha);
    }
}

void drawRing(float cx, float cy, float outerRadius, float innerRadius)
{
    for (var y = (int)MathF.Floor(cy - outerRadius - 2); y <= MathF.Ceiling(cy + outerRadius + 2); y++)
    for (var x = (int)MathF.Floor(cx - outerRadius - 2); x <= MathF.Ceiling(cx + outerRadius + 2); x++)
    {
        var distance = MathF.Sqrt(MathF.Pow(x + 0.5f - cx, 2) + MathF.Pow(y + 0.5f - cy, 2));
        var alpha = edge(outerRadius + 0.75f, outerRadius - 0.75f, distance)
                    * edge(innerRadius - 0.75f, innerRadius + 0.75f, distance);
        blend(x, y, alpha);
    }
}

void drawRoundedBar(float left, float top, float barWidth, float barHeight, float radius)
{
    var right = left + barWidth;
    var bottom = top + barHeight;

    for (var y = (int)MathF.Floor(top - 2); y <= MathF.Ceiling(bottom + 2); y++)
    for (var x = (int)MathF.Floor(left - 2); x <= MathF.Ceiling(right + 2); x++)
    {
        var distance = roundedRectDistance(x + 0.5f, y + 0.5f, left, top, right, bottom, radius);
        blend(x, y, edge(0.75f, -0.75f, distance));
    }
}

void drawLine(float x1, float y1, float x2, float y2, float thickness)
{
    var minX = (int)MathF.Floor(MathF.Min(x1, x2) - thickness);
    var maxX = (int)MathF.Ceiling(MathF.Max(x1, x2) + thickness);
    var minY = (int)MathF.Floor(MathF.Min(y1, y2) - thickness);
    var maxY = (int)MathF.Ceiling(MathF.Max(y1, y2) + thickness);

    for (var y = minY; y <= maxY; y++)
    for (var x = minX; x <= maxX; x++)
    {
        var distance = distanceToSegment(x + 0.5f, y + 0.5f, x1, y1, x2, y2);
        blend(x, y, edge(thickness / 2 + 0.75f, thickness / 2 - 0.75f, distance));
    }
}

void drawGear(float cx, float cy, float outerRadius, float innerRadius)
{
    for (var y = (int)MathF.Floor(cy - outerRadius - 2); y <= MathF.Ceiling(cy + outerRadius + 2); y++)
    for (var x = (int)MathF.Floor(cx - outerRadius - 2); x <= MathF.Ceiling(cx + outerRadius + 2); x++)
    {
        var dx = x + 0.5f - cx;
        var dy = y + 0.5f - cy;
        var radius = MathF.Sqrt(dx * dx + dy * dy);
        var angle = MathF.Atan2(dy, dx);
        var toothAngle = MathF.Abs(MathF.IEEERemainder(angle, MathF.PI / 4));
        var outer = toothAngle < 0.16f ? outerRadius : outerRadius - 1.7f;
        var alpha = edge(outer + 0.75f, outer - 0.75f, radius)
                    * edge(innerRadius - 0.75f, innerRadius + 0.75f, radius);
        blend(x, y, alpha);
    }
}

void clearLine(float x1, float y1, float x2, float y2, float thickness)
{
    var minX = (int)MathF.Floor(MathF.Min(x1, x2) - thickness);
    var maxX = (int)MathF.Ceiling(MathF.Max(x1, x2) + thickness);
    var minY = (int)MathF.Floor(MathF.Min(y1, y2) - thickness);
    var maxY = (int)MathF.Ceiling(MathF.Max(y1, y2) + thickness);

    for (var y = minY; y <= maxY; y++)
    for (var x = minX; x <= maxX; x++)
    {
        if ((uint)x >= width || (uint)y >= height)
            continue;

        var distance = distanceToSegment(x + 0.5f, y + 0.5f, x1, y1, x2, y2);
        var alpha = edge(thickness / 2 + 0.75f, thickness / 2 - 0.75f, distance);
        pixels[(y * width + x) * 4 + 3] = (byte)(pixels[(y * width + x) * 4 + 3] * (1 - alpha));
    }
}

void clearCircle(float cx, float cy, float radius)
{
    for (var y = (int)MathF.Floor(cy - radius - 1); y <= MathF.Ceiling(cy + radius + 1); y++)
    for (var x = (int)MathF.Floor(cx - radius - 1); x <= MathF.Ceiling(cx + radius + 1); x++)
    {
        var distance = MathF.Sqrt(MathF.Pow(x + 0.5f - cx, 2) + MathF.Pow(y + 0.5f - cy, 2));
        var alpha = edge(radius + 0.75f, radius - 0.75f, distance);

        if (alpha <= 0 || (uint)x >= width || (uint)y >= height)
            continue;

        pixels[(y * width + x) * 4 + 3] = (byte)(pixels[(y * width + x) * 4 + 3] * (1 - alpha));
    }
}

void blend(int x, int y, float alpha)
{
    if ((uint)x >= width || (uint)y >= height || alpha <= 0)
        return;

    var offset = (y * width + x) * 4;
    pixels[offset] = 255;
    pixels[offset + 1] = 255;
    pixels[offset + 2] = 255;
    pixels[offset + 3] = Math.Max(pixels[offset + 3], (byte)Math.Clamp(alpha * 255, 0, 255));
}

static float edge(float high, float low, float value)
    => Math.Clamp((high - value) / (high - low), 0, 1);

static float roundedRectDistance(float x, float y, float left, float top, float right, float bottom, float radius)
{
    var centreX = (left + right) / 2;
    var centreY = (top + bottom) / 2;
    var qx = MathF.Abs(x - centreX) - (right - left) / 2 + radius;
    var qy = MathF.Abs(y - centreY) - (bottom - top) / 2 + radius;
    var outside = MathF.Sqrt(MathF.Max(qx, 0) * MathF.Max(qx, 0) + MathF.Max(qy, 0) * MathF.Max(qy, 0));
    return outside + MathF.Min(MathF.Max(qx, qy), 0) - radius;
}

static float distanceToSegment(float x, float y, float x1, float y1, float x2, float y2)
{
    var dx = x2 - x1;
    var dy = y2 - y1;
    var lengthSquared = dx * dx + dy * dy;
    var t = lengthSquared == 0 ? 0 : Math.Clamp(((x - x1) * dx + (y - y1) * dy) / lengthSquared, 0, 1);
    var px = x1 + t * dx;
    var py = y1 + t * dy;
    return MathF.Sqrt(MathF.Pow(x - px, 2) + MathF.Pow(y - py, 2));
}

static byte[] encodePng(int pngWidth, int pngHeight, byte[] data)
{
    using var stream = new MemoryStream();
    stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);
    writeChunk(stream, "IHDR", createIhdr(pngWidth, pngHeight));

    using var raw = new MemoryStream();
    for (var y = 0; y < pngHeight; y++)
    {
        raw.WriteByte(0);
        raw.Write(data, y * pngWidth * 4, pngWidth * 4);
    }

    using var compressed = new MemoryStream();
    using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        raw.WriteTo(zlib);

    writeChunk(stream, "IDAT", compressed.ToArray());
    writeChunk(stream, "IEND", []);
    return stream.ToArray();
}

static byte[] createIhdr(int pngWidth, int pngHeight)
{
    var header = new byte[13];
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)pngWidth);
    BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)pngHeight);
    header[8] = 8;
    header[9] = 6;
    return header;
}

static void writeChunk(Stream stream, string type, byte[] data)
{
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
    stream.Write(length);

    var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
    stream.Write(typeBytes);
    stream.Write(data);

    var crc = crc32(typeBytes, data);
    Span<byte> crcBytes = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
    stream.Write(crcBytes);
}

static uint crc32(byte[] type, byte[] data)
{
    var crc = 0xFFFFFFFFu;

    foreach (var value in type.Concat(data))
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
    }

    return ~crc;
}

static byte[] encodeFont(int pageWidth, int glyphCount)
{
    using var stream = new MemoryStream();
    stream.Write("BMF"u8);
    stream.WriteByte(3);

    var info = new List<byte>();
    writeInt16(info, glyph_size);
    info.Add(0);
    info.Add(0);
    writeUInt16(info, 100);
    info.Add(1);
    info.AddRange([0, 0, 0, 0, 0, 0, 0]);
    info.AddRange("bmsIcons\0"u8.ToArray());
    writeBlock(stream, 1, info.ToArray());

    var common = new List<byte>();
    writeUInt16(common, glyph_size);
    writeUInt16(common, glyph_size);
    writeUInt16(common, (ushort)pageWidth);
    writeUInt16(common, glyph_size);
    writeUInt16(common, 1);
    common.AddRange([0, 0, 0, 0, 0]);
    writeBlock(stream, 2, common.ToArray());

    writeBlock(stream, 3, "bmsIcons_0.png\0"u8.ToArray());

    var chars = new List<byte>();
    for (var index = 0; index < glyphCount; index++)
    {
        writeUInt32(chars, (uint)(0xE000 + index));
        writeUInt16(chars, (ushort)(index * glyph_size));
        writeUInt16(chars, 0);
        writeUInt16(chars, glyph_size);
        writeUInt16(chars, glyph_size);
        writeInt16(chars, 0);
        writeInt16(chars, 0);
        writeInt16(chars, glyph_size);
        chars.Add(0);
        chars.Add(15);
    }

    writeBlock(stream, 4, chars.ToArray());
    return stream.ToArray();
}

static void writeBlock(Stream stream, byte type, byte[] data)
{
    stream.WriteByte(type);
    Span<byte> size = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
    stream.Write(size);
    stream.Write(data);
}

static void writeUInt16(List<byte> bytes, ushort value)
{
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
    bytes.AddRange(buffer.ToArray());
}

static void writeInt16(List<byte> bytes, short value) => writeUInt16(bytes, unchecked((ushort)value));

static void writeUInt32(List<byte> bytes, uint value)
{
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
    bytes.AddRange(buffer.ToArray());
}
