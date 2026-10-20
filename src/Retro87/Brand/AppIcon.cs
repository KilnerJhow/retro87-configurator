// SPDX-License-Identifier: GPL-3.0-or-later

using SkiaSharp;

namespace Retro87.Brand;

/// <summary>
/// The application icon, drawn with Skia (the same renderer as the app) so the mark ships as code
/// rather than as a mystery raster. It is the product's own gesture in one image: a keycap over the
/// instrument's single amber underline — the same amber the active tab wears.
/// </summary>
/// <remarks>
/// The palette is DESIGN.md's: plate <c>background</c>, keycap <c>surface-2</c>, edge <c>line</c>,
/// underline <c>accent</c>. Geometry only (no text, no gradient, no shadow), so it stays legible at
/// 16 px and needs no font. <see cref="WriteIco"/> packs 16/32/48/256 PNG frames into one `.ico`.
/// </remarks>
public static class AppIcon
{
    /// <summary>Renders the icon at <paramref name="size"/> pixels.</summary>
    public static SKBitmap Render(int size)
    {
        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        float u = size / 256f;                       // one design unit
        using var paint = new SKPaint { IsAntialias = true };

        // plate: a slightly deeper slate so the mark holds on light and dark taskbars alike
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(0x12, 0x16, 0x1F);
        var plate = new SKRoundRect(new SKRect(6 * u, 6 * u, 250 * u, 250 * u), 56 * u, 56 * u);
        canvas.DrawRoundRect(plate, paint);

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 3 * u;
        paint.Color = new SKColor(0x26, 0x2C, 0x39);
        canvas.DrawRoundRect(plate, paint);

        // keycap: the product's object
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(0x1C, 0x21, 0x2C);
        var cap = new SKRoundRect(new SKRect(50 * u, 40 * u, 206 * u, 196 * u), 26 * u, 26 * u);
        canvas.DrawRoundRect(cap, paint);

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 3 * u;
        paint.Color = new SKColor(0x3A, 0x42, 0x52);
        canvas.DrawRoundRect(cap, paint);

        // the instrument's underline, in the one accent, fused into the key's bottom edge
        paint.Style = SKPaintStyle.Fill;
        paint.Color = new SKColor(0xE9, 0xB0, 0x4C);
        var underline = new SKRoundRect(new SKRect(72 * u, 150 * u, 184 * u, 170 * u), 10 * u, 10 * u);
        canvas.DrawRoundRect(underline, paint);

        canvas.Flush();
        return bitmap;
    }

    /// <summary>Writes the Windows icon with 16, 32, 48 and 256 px frames (PNG payloads).</summary>
    public static void WriteIco(string path, string? pngPreviewPath = null)
    {
        int[] sizes = { 16, 32, 48, 256 };
        var payloads = new List<(int Size, byte[] Bytes)>();
        foreach (int size in sizes)
        {
            using SKBitmap bitmap = Render(size);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            payloads.Add((size, data.ToArray()));
        }

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);            // reserved
            writer.Write((ushort)1);            // type: icon
            writer.Write((ushort)payloads.Count);

            int offset = 6 + payloads.Count * 16;
            foreach ((int size, byte[] bytes) in payloads)
            {
                writer.Write((byte)(size >= 256 ? 0 : size));   // width (0 = 256)
                writer.Write((byte)(size >= 256 ? 0 : size));   // height
                writer.Write((byte)0);          // palette colours
                writer.Write((byte)0);          // reserved
                writer.Write((ushort)1);        // colour planes
                writer.Write((ushort)32);       // bits per pixel
                writer.Write(bytes.Length);
                writer.Write(offset);
                offset += bytes.Length;
            }

            foreach ((_, byte[] bytes) in payloads) writer.Write(bytes);
        }

        if (pngPreviewPath is not null)
        {
            using SKBitmap preview = Render(256);
            using SKImage image = SKImage.FromBitmap(preview);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(pngPreviewPath, data.ToArray());
        }
    }
}
