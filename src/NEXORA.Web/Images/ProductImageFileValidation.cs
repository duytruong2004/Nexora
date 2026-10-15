using NEXORA.Application.Catalog;

namespace NEXORA.Web.Images;

public static class ProductImageFileValidation
{
    public const long MaximumFileSizeBytes = 5 * 1024 * 1024;

    public static string ValidateAndGetExtension(ReadOnlySpan<byte> content, string originalFileName)
    {
        if (content.IsEmpty)
        {
            throw new ProductImageValidationException("Tệp ảnh đang trống.");
        }

        var sourceExtension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var detectedExtension = DetectImageExtension(content);
        if (detectedExtension is null)
        {
            throw new ProductImageValidationException("Định dạng ảnh không được hỗ trợ. Chỉ chấp nhận JPEG, PNG hoặc WebP.");
        }

        var sourceIsSameFormat = detectedExtension == ".jpg"
            ? sourceExtension is ".jpg" or ".jpeg"
            : sourceExtension == detectedExtension;
        if (!sourceIsSameFormat)
        {
            throw new ProductImageValidationException("Phần mở rộng không khớp với định dạng thực tế của ảnh.");
        }

        return detectedExtension;
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return ".jpg";
        }

        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.StartsWith(pngSignature))
        {
            return ".png";
        }

        if (content.Length >= 12 &&
            content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }
}
