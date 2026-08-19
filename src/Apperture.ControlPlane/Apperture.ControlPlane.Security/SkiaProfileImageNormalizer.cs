using Apperture.Core.Application.Accounts;
using SkiaSharp;

namespace Apperture.ControlPlane.Security;

/// <summary>
/// Turns an untrusted upload into platform-owned square PNG renditions.
/// </summary>
/// <remarks>
/// Every byte that arrives is treated as hostile. The declared content type and file name are never
/// consulted here — Skia's codec decides what the bytes actually are, and anything it cannot decode
/// as one of the accepted raster formats is refused. SVG is rejected implicitly because Skia's image
/// codecs do not decode it, so no markup is ever parsed or stored.
///
/// Because output is produced by drawing decoded pixels onto a fresh surface and re-encoding, EXIF,
/// ICC, XMP, embedded thumbnails, and any trailing appended data are dropped: none of that survives
/// the pixel copy. Orientation is baked into the pixels first, so the stored image is upright without
/// retaining the metadata that described it.
/// </remarks>
public sealed class SkiaProfileImageNormalizer : IProfileImageNormalizer
{
    private static readonly SKEncodedImageFormat[] AcceptedSourceFormats =
    [
        SKEncodedImageFormat.Jpeg,
        SKEncodedImageFormat.Png,
        SKEncodedImageFormat.Webp
    ];

    /// <summary>Orientation and crop are 1:1 pixel copies, so no resampling is wanted.</summary>
    private static readonly SKSamplingOptions ExactCopy = new(SKFilterMode.Nearest, SKMipmapMode.None);

    public ProfileImageNormalizationResult Normalize(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.Empty,
                "The uploaded file is empty.");

        if (content.Length > ProfileImageLimits.MaxUploadBytes)
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.TooLarge,
                $"The image must be {ProfileImageLimits.MaxUploadBytes / (1024 * 1024)} MB or smaller.");

        using var data = SKData.CreateCopy(content.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null)
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.UndecodableImage,
                "That file could not be read as an image. Upload a JPEG, PNG, or WebP.");

        if (!AcceptedSourceFormats.Contains(codec.EncodedFormat))
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.UnsupportedImageFormat,
                "That image format is not supported. Upload a JPEG, PNG, or WebP.");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.UndecodableImage,
                "That file could not be read as an image. Upload a JPEG, PNG, or WebP.");

        // Refuse oversized headers before allocating a decode buffer, so a small compressed payload
        // claiming enormous dimensions cannot force a large allocation.
        if (info.Width > ProfileImageLimits.MaxSourceDimension
            || info.Height > ProfileImageLimits.MaxSourceDimension
            || (long)info.Width * info.Height > ProfileImageLimits.MaxSourcePixels)
        {
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.DimensionsTooLarge,
                $"The image is too large to process. Use an image no larger than {ProfileImageLimits.MaxSourceDimension} pixels on either side.");
        }

        if (info.Width < ProfileImageLimits.MinSourceDimension
            || info.Height < ProfileImageLimits.MinSourceDimension)
        {
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.DimensionsTooSmall,
                $"The image is too small. Use an image at least {ProfileImageLimits.MinSourceDimension} pixels on each side.");
        }

        using var decoded = DecodePixels(codec, info);
        if (decoded is null)
            return ProfileImageNormalizationResult.Rejected(
                ProfileImageRejectionReason.UndecodableImage,
                "That image could not be decoded. Upload a JPEG, PNG, or WebP.");

        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);
        using var square = CenterCrop(upright);

        var renditions = new List<ProfileImageRendition>(ProfileImageLimits.RenditionSizes.Count);
        foreach (var size in ProfileImageLimits.RenditionSizes)
        {
            var encoded = ResizeAndEncode(square, size);
            if (encoded is null || encoded.Length == 0)
                return ProfileImageNormalizationResult.Rejected(
                    ProfileImageRejectionReason.EncodeFailed,
                    "The image could not be prepared for storage. Try a different image.");

            renditions.Add(new ProfileImageRendition(size, ProfileImageLimits.OutputContentType, encoded));
        }

        return ProfileImageNormalizationResult.Success(
            renditions,
            info.Width,
            info.Height,
            codec.EncodedFormat.ToString());
    }

    private static SKBitmap? DecodePixels(SKCodec codec, SKImageInfo info)
    {
        var target = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(target);
        var result = codec.GetPixels(target, bitmap.GetPixels());

        // A truncated but structurally valid image still yields usable pixels; anything else is a refusal.
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
            return bitmap;

        bitmap.Dispose();
        return null;
    }

    /// <summary>
    /// Bakes the source EXIF orientation into the pixels so the stored image is upright and the
    /// orientation metadata no longer needs to exist.
    /// </summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
            return source.Copy();

        var width = SwapsAxes(origin) ? source.Height : source.Width;
        var height = SwapsAxes(origin) ? source.Width : source.Height;

        var oriented = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(oriented);
        using var sourceImage = SKImage.FromBitmap(source);
        canvas.SetMatrix(OrientationMatrix(origin, source.Width, source.Height));
        canvas.DrawImage(sourceImage, 0, 0, ExactCopy);
        canvas.Flush();
        return oriented;
    }

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Maps source pixel (x, y) to its upright position. Written out per EXIF orientation rather than
    /// composed from rotations and flips, because the composed form is easy to get subtly wrong.
    /// Each entry reads destX = scaleX·x + skewX·y + transX, destY = skewY·x + scaleY·y + transY.
    /// </summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        // Mirror horizontally.
        SKEncodedOrigin.TopRight => Matrix(-1, 0, w, 0, 1, 0),
        // Rotate 180°.
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, w, 0, -1, h),
        // Mirror vertically.
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, h),
        // Transpose about the main diagonal.
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        // Rotate 90° clockwise.
        SKEncodedOrigin.RightTop => Matrix(0, -1, h, 1, 0, 0),
        // Transpose about the anti-diagonal.
        SKEncodedOrigin.RightBottom => Matrix(0, -1, h, -1, 0, w),
        // Rotate 270° clockwise.
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, w),
        _ => SKMatrix.Identity
    };

    private static SKMatrix Matrix(
        float scaleX, float skewX, float transX,
        float skewY, float scaleY, float transY) =>
        new(scaleX, skewX, transX, skewY, scaleY, transY, 0, 0, 1);

    /// <summary>Takes the largest centered square so portrait and landscape photos both crop sensibly.</summary>
    private static SKBitmap CenterCrop(SKBitmap source)
    {
        if (source.Width == source.Height)
            return source.Copy();

        var edge = Math.Min(source.Width, source.Height);
        var left = (source.Width - edge) / 2;
        var top = (source.Height - edge) / 2;

        var cropped = new SKBitmap(new SKImageInfo(edge, edge, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(cropped);
        using var sourceImage = SKImage.FromBitmap(source);
        canvas.DrawImage(
            sourceImage,
            SKRect.Create(left, top, edge, edge),
            SKRect.Create(0, 0, edge, edge),
            ExactCopy);
        canvas.Flush();
        return cropped;
    }

    private static byte[]? ResizeAndEncode(SKBitmap square, int size)
    {
        var target = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var resized = square.Resize(target, new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized is null)
            return null;

        using var image = SKImage.FromBitmap(resized);
        if (image is null)
            return null;

        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }
}
