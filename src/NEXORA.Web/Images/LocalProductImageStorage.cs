using System.Buffers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Catalog;

namespace NEXORA.Web.Images;

public sealed class LocalProductImageStorage(
    IWebHostEnvironment environment,
    ILogger<LocalProductImageStorage> logger) : IProductImageStorage
{
    private const string UploadFolder = "uploads/products";
    private const string PublicPrefix = "/uploads/products/";

    public async Task<string> SaveAsync(ProductImageFile image, CancellationToken cancellationToken = default)
    {
        if (image.Length <= 0 || image.Length > ProductImageFileValidation.MaximumFileSizeBytes)
        {
            throw new ProductImageValidationException("Mỗi ảnh phải có dung lượng lớn hơn 0 và không vượt quá 5 MB.");
        }

        var bytes = await ReadBoundedAsync(image.Content, cancellationToken);
        var extension = ProductImageFileValidation.ValidateAndGetExtension(bytes, image.OriginalFileName);
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        var uploadDirectory = Path.Combine(webRoot, "uploads", "products");
        Directory.CreateDirectory(uploadDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(uploadDirectory, fileName);
        try
        {
            await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await file.WriteAsync(bytes, cancellationToken);
        }
        catch
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            throw;
        }

        return $"{PublicPrefix}{fileName}";
    }

    public Task DeleteAsync(string relativeUrl, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetManagedImagePath(relativeUrl, out var fullPath))
        {
            logger.LogWarning("Skipped deletion of a product image path outside the managed upload format.");
            return Task.CompletedTask;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private async Task<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        await using var bufferStream = new MemoryStream((int)Math.Min(ProductImageFileValidation.MaximumFileSizeBytes, 81920));
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long total = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > ProductImageFileValidation.MaximumFileSizeBytes)
                {
                    throw new ProductImageValidationException("Mỗi ảnh không được vượt quá 5 MB.");
                }

                await bufferStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return bufferStream.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private bool TryGetManagedImagePath(string relativeUrl, out string fullPath)
    {
        fullPath = string.Empty;
        if (!relativeUrl.StartsWith(PublicPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var fileName = relativeUrl[PublicPrefix.Length..];
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (fileName.Contains('/') || fileName.Contains('\\') ||
            extension is not (".jpg" or ".png" or ".webp") || !Guid.TryParseExact(stem, "N", out _))
        {
            return false;
        }

        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        fullPath = Path.GetFullPath(Path.Combine(webRoot, UploadFolder, fileName));
        var expectedRoot = Path.GetFullPath(Path.Combine(webRoot, UploadFolder)) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
