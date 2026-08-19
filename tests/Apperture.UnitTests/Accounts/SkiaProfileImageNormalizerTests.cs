using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Accounts;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Apperture.UnitTests.Accounts;

/// <summary>
/// Verifies the avatar normalization pipeline against the inputs an untrusted upload endpoint actually
/// receives. Assertions inspect decoded output bytes rather than trusting file names or declared types.
/// </summary>
public sealed class SkiaProfileImageNormalizerTests
{
    private readonly SkiaProfileImageNormalizer _normalizer = new();

    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public void Accepts_each_supported_raster_format(string format)
    {
        var bytes = format switch
        {
            "jpeg" => ProfileImageTestImages.Jpeg(),
            "png" => ProfileImageTestImages.Png(),
            _ => ProfileImageTestImages.Webp()
        };

        var result = _normalizer.Normalize(bytes);

        result.Succeeded.Should().BeTrue();
        result.Reason.Should().Be(ProfileImageRejectionReason.None);
    }

    [Fact]
    public void Produces_one_square_png_rendition_for_each_configured_size()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.Jpeg(500, 300));

        result.Succeeded.Should().BeTrue();
        result.Renditions.Select(r => r.Size).Should().BeEquivalentTo(ProfileImageLimits.RenditionSizes);

        foreach (var rendition in result.Renditions)
        {
            rendition.ContentType.Should().Be("image/png");

            using var decoded = SKBitmap.Decode(rendition.Content);
            decoded.Should().NotBeNull("every stored rendition must be a decodable image");
            decoded!.Width.Should().Be(rendition.Size);
            decoded.Height.Should().Be(rendition.Size, "avatars are always square");
        }
    }

    [Fact]
    public void Re_encodes_a_jpeg_upload_as_png_so_the_stored_format_is_platform_controlled()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.Jpeg());

        result.Succeeded.Should().BeTrue();
        result.SourceFormat.Should().Be("Jpeg");

        using var codec = SKCodec.Create(SKData.CreateCopy(result.Renditions[0].Content));
        codec!.EncodedFormat.Should().Be(SKEncodedImageFormat.Png);
    }

    [Fact]
    public void Strips_exif_metadata_from_the_stored_output()
    {
        var (bytes, marker) = ProfileImageTestImages.JpegWithExif();

        // The canary must be present in the input, or the test proves nothing.
        Contains(bytes, marker).Should().BeTrue("the fixture must actually carry the EXIF canary");

        var result = _normalizer.Normalize(bytes);

        result.Succeeded.Should().BeTrue();
        foreach (var rendition in result.Renditions)
        {
            Contains(rendition.Content, marker).Should().BeFalse(
                "re-encoding from decoded pixels must not carry EXIF text into storage");
            Contains(rendition.Content, "Exif").Should().BeFalse(
                "no EXIF segment should survive into the stored PNG");
        }
    }

    [Fact]
    public void Normalizes_orientation_so_a_rotated_source_is_stored_upright()
    {
        // Orientation 6 means "rotate 90° clockwise to display", so the source top-left quadrant
        // (red) must end up in the top-right of the stored image.
        var (bytes, _) = ProfileImageTestImages.JpegWithExif();
        var oriented = ReplaceWithQuadrants(bytes);

        var result = _normalizer.Normalize(oriented);
        result.Succeeded.Should().BeTrue();

        using var decoded = SKBitmap.Decode(result.Renditions[0].Content);
        decoded.Should().NotBeNull();

        // Sample well inside each quadrant to stay clear of JPEG ringing at the boundaries.
        var quarter = decoded!.Width / 4;
        var topRight = decoded.GetPixel(decoded.Width - quarter, quarter);
        topRight.Red.Should().BeGreaterThan(120, "the rotated source's red quadrant belongs top-right");
        topRight.Green.Should().BeLessThan(120);
    }

    [Fact]
    public void Center_crops_a_wide_image_rather_than_stretching_it()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.Quadrants(SKEncodedImageFormat.Png, 400, 200));

        result.Succeeded.Should().BeTrue();
        result.SourceWidth.Should().Be(400);
        result.SourceHeight.Should().Be(200);

        using var decoded = SKBitmap.Decode(result.Renditions[0].Content);
        decoded!.Width.Should().Be(decoded.Height, "a wide source is cropped square, not squashed");
    }

    [Fact]
    public void Rejects_svg_uploads()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.Svg());

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().BeOneOf(
            ProfileImageRejectionReason.UndecodableImage,
            ProfileImageRejectionReason.UnsupportedImageFormat);
        result.Renditions.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_an_executable_regardless_of_what_it_is_named()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.WindowsExecutable());

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.UndecodableImage);
    }

    [Fact]
    public void Rejects_plain_text()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.PlainText());

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.UndecodableImage);
    }

    [Fact]
    public void Rejects_a_file_with_valid_image_magic_bytes_but_no_usable_content()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.TruncatedPng());

        result.Succeeded.Should().BeFalse();
        result.Renditions.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_an_empty_upload()
    {
        var result = _normalizer.Normalize([]);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.Empty);
    }

    [Fact]
    public void Rejects_an_upload_larger_than_the_byte_limit_before_decoding_it()
    {
        var oversized = new byte[ProfileImageLimits.MaxUploadBytes + 1];

        var result = _normalizer.Normalize(oversized);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.TooLarge);
    }

    [Fact]
    public void Rejects_an_image_whose_declared_dimensions_exceed_the_pixel_ceiling()
    {
        // A highly compressible single-colour PNG stays small on disk while declaring a very large
        // canvas: the decompression-bomb shape the dimension ceiling exists to stop.
        var bomb = ProfileImageTestImages.Encode(
            SKEncodedImageFormat.Png,
            ProfileImageLimits.MaxSourceDimension + 1,
            8,
            SKColors.White);

        bomb.Length.Should().BeLessThan(
            ProfileImageLimits.MaxUploadBytes,
            "the fixture must pass the byte-size gate so the dimension gate is the one under test");

        var result = _normalizer.Normalize(bomb);

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.DimensionsTooLarge);
    }

    [Fact]
    public void Rejects_an_image_too_small_to_be_a_useful_avatar()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.Png(8, 8));

        result.Succeeded.Should().BeFalse();
        result.Reason.Should().Be(ProfileImageRejectionReason.DimensionsTooSmall);
    }

    [Fact]
    public void Upscales_a_small_but_acceptable_source_to_every_rendition_size()
    {
        var result = _normalizer.Normalize(ProfileImageTestImages.Png(32, 32));

        result.Succeeded.Should().BeTrue();
        result.Renditions.Should().HaveCount(ProfileImageLimits.RenditionSizes.Count);
        result.Renditions.Should().OnlyContain(r => r.Content.Length > 0);
    }

    private static byte[] ReplaceWithQuadrants(byte[] jpegWithExif)
    {
        // Rebuild the EXIF fixture around a quadrant image so orientation is observable in the pixels.
        var quadrants = ProfileImageTestImages.Quadrants(SKEncodedImageFormat.Jpeg, 240, 160);
        var app1Length = FindApp1SegmentLength(jpegWithExif);

        var result = new byte[quadrants.Length + app1Length];
        result[0] = quadrants[0];
        result[1] = quadrants[1];
        Array.Copy(jpegWithExif, 2, result, 2, app1Length);
        Array.Copy(quadrants, 2, result, 2 + app1Length, quadrants.Length - 2);
        return result;
    }

    private static int FindApp1SegmentLength(byte[] jpeg)
    {
        jpeg[2].Should().Be(0xFF);
        jpeg[3].Should().Be(0xE1, "the fixture places APP1 immediately after SOI");
        var length = (jpeg[4] << 8) | jpeg[5];
        return length + 2; // Marker bytes plus the declared length.
    }

    private static bool Contains(byte[] haystack, string needle)
    {
        var pattern = System.Text.Encoding.ASCII.GetBytes(needle);
        for (var i = 0; i + pattern.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (haystack[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }
}
