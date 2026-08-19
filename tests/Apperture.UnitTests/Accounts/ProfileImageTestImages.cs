using SkiaSharp;

namespace Apperture.UnitTests.Accounts;

/// <summary>
/// Builds real encoded images for the avatar pipeline tests. Tests assert on decoded output rather
/// than on file extensions, so the inputs have to be genuine bitstreams.
/// </summary>
internal static class ProfileImageTestImages
{
    /// <summary>Encodes a solid-colour bitmap in the requested format.</summary>
    public static byte[] Encode(SKEncodedImageFormat format, int width, int height, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color ?? SKColors.CornflowerBlue);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static byte[] Jpeg(int width = 200, int height = 200) =>
        Encode(SKEncodedImageFormat.Jpeg, width, height);

    public static byte[] Png(int width = 200, int height = 200) =>
        Encode(SKEncodedImageFormat.Png, width, height);

    public static byte[] Webp(int width = 200, int height = 200) =>
        Encode(SKEncodedImageFormat.Webp, width, height);

    /// <summary>
    /// A quadrant image, used to prove that orientation and cropping move the pixels the test expects
    /// rather than merely producing an image of the right size.
    /// </summary>
    public static byte[] Quadrants(SKEncodedImageFormat format, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            var halfWidth = width / 2f;
            var halfHeight = height / 2f;
            using var paint = new SKPaint { Style = SKPaintStyle.Fill };

            paint.Color = SKColors.Red;
            canvas.DrawRect(SKRect.Create(0, 0, halfWidth, halfHeight), paint);
            paint.Color = SKColors.Lime;
            canvas.DrawRect(SKRect.Create(halfWidth, 0, halfWidth, halfHeight), paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(SKRect.Create(0, halfHeight, halfWidth, halfHeight), paint);
            paint.Color = SKColors.Yellow;
            canvas.DrawRect(SKRect.Create(halfWidth, halfHeight, halfWidth, halfHeight), paint);
            canvas.Flush();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 100);
        return data.ToArray();
    }

    /// <summary>An SVG document. Valid markup, but never an acceptable avatar upload.</summary>
    public static byte[] Svg() => System.Text.Encoding.UTF8.GetBytes(
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">
          <rect width="200" height="200" fill="red" />
          <script>alert('xss')</script>
        </svg>
        """);

    /// <summary>A Windows executable header. The classic "rename it to .png" upload.</summary>
    public static byte[] WindowsExecutable()
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        // PE signature at the conventional offset.
        bytes[0x3C] = 0x40;
        bytes[0x40] = (byte)'P';
        bytes[0x41] = (byte)'E';
        return bytes;
    }

    /// <summary>Plain text with no image structure at all.</summary>
    public static byte[] PlainText() =>
        System.Text.Encoding.UTF8.GetBytes("This is definitely not an image, no matter what it is called.");

    /// <summary>A PNG header followed by garbage: correct magic bytes, unusable content.</summary>
    public static byte[] TruncatedPng()
    {
        var real = Png(64, 64);
        var truncated = new byte[16];
        Array.Copy(real, truncated, truncated.Length);
        return truncated;
    }

    /// <summary>
    /// A JPEG carrying an EXIF APP1 segment with an orientation tag and a distinctive text comment,
    /// so tests can prove the metadata is absent from the stored output.
    /// </summary>
    public static (byte[] Bytes, string Marker) JpegWithExif(int width = 240, int height = 160)
    {
        var baseline = Encode(SKEncodedImageFormat.Jpeg, width, height);
        const string marker = "APPERTURE-EXIF-CANARY";
        var exif = BuildExifApp1Segment(marker);

        // Splice the APP1 segment in immediately after SOI, which is where a camera would put it.
        var result = new byte[baseline.Length + exif.Length];
        result[0] = baseline[0];
        result[1] = baseline[1];
        Array.Copy(exif, 0, result, 2, exif.Length);
        Array.Copy(baseline, 2, result, 2 + exif.Length, baseline.Length - 2);
        return (result, marker);
    }

    /// <summary>
    /// Minimal little-endian TIFF header inside an APP1 segment: orientation 6 (rotate 90° clockwise)
    /// plus an ImageDescription holding the canary text.
    /// </summary>
    private static byte[] BuildExifApp1Segment(string marker)
    {
        var description = System.Text.Encoding.ASCII.GetBytes(marker + "\0");

        using var tiff = new MemoryStream();
        using var writer = new BinaryWriter(tiff);

        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((ushort)42);
        writer.Write((uint)8); // First IFD offset.

        writer.Write((ushort)2); // Entry count.

        // Orientation (0x0112), SHORT, value inline.
        writer.Write((ushort)0x0112);
        writer.Write((ushort)3);
        writer.Write((uint)1);
        writer.Write((ushort)6);
        writer.Write((ushort)0);

        // ImageDescription (0x010E), ASCII, value out of line.
        var descriptionOffset = 8 + 2 + (12 * 2) + 4;
        writer.Write((ushort)0x010E);
        writer.Write((ushort)2);
        writer.Write((uint)description.Length);
        writer.Write((uint)descriptionOffset);

        writer.Write((uint)0); // No next IFD.
        writer.Write(description);
        writer.Flush();

        var tiffBytes = tiff.ToArray();
        var payloadLength = 6 + tiffBytes.Length; // "Exif\0\0" + TIFF block.
        var segmentLength = payloadLength + 2;    // Length field counts itself.

        var segment = new byte[2 + segmentLength];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((segmentLength >> 8) & 0xFF);
        segment[3] = (byte)(segmentLength & 0xFF);
        segment[4] = (byte)'E';
        segment[5] = (byte)'x';
        segment[6] = (byte)'i';
        segment[7] = (byte)'f';
        segment[8] = 0;
        segment[9] = 0;
        Array.Copy(tiffBytes, 0, segment, 10, tiffBytes.Length);
        return segment;
    }
}
