using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

public sealed class ProductImageManagementService(
    AppDbContext db,
    IProductImageStorage storage,
    ILogger<ProductImageManagementService> logger) : IProductImageManagementService
{
    private const int MaximumDetailImagesPerRequest = 8;

    public async Task SaveImagesAsync(
        int productId,
        ProductImageFile? primaryImage,
        IReadOnlyList<ProductImageFile> detailImages,
        CancellationToken cancellationToken = default)
    {
        if (detailImages.Count > MaximumDetailImagesPerRequest)
        {
            throw new ProductImageValidationException($"Mỗi lần chỉ được tải tối đa {MaximumDetailImagesPerRequest} ảnh chi tiết.");
        }

        var storedUrls = new List<string>();
        try
        {
            string? primaryUrl = null;
            if (primaryImage is not null)
            {
                primaryUrl = await storage.SaveAsync(primaryImage, cancellationToken);
                storedUrls.Add(primaryUrl);
            }

            var detailUrls = new List<string>(detailImages.Count);
            foreach (var image in detailImages)
            {
                var url = await storage.SaveAsync(image, cancellationToken);
                storedUrls.Add(url);
                detailUrls.Add(url);
            }

            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var productExists = await db.Products.AnyAsync(item => item.Id == productId, cancellationToken);
                if (!productExists)
                {
                    throw new CatalogValidationException(nameof(productId), "Không tìm thấy sản phẩm.");
                }

                var operationUrls = detailUrls.Concat(primaryUrl is null ? [] : [primaryUrl]).ToArray();
                if (operationUrls.Length > 0 && await db.ProductImages.AsNoTracking()
                        .CountAsync(image => image.ProductId == productId && operationUrls.Contains(image.ImageUrl), cancellationToken)
                    == operationUrls.Length)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return;
                }

                if (primaryUrl is not null)
                {
                    var existingPrimary = await db.ProductImages
                        .SingleOrDefaultAsync(image => image.ProductId == productId && image.IsPrimary, cancellationToken);
                    if (existingPrimary is not null)
                    {
                        existingPrimary.IsPrimary = false;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }

                var nextSortOrder = await db.ProductImages
                    .Where(image => image.ProductId == productId)
                    .Select(image => (int?)image.SortOrder)
                    .MaxAsync(cancellationToken) ?? -1;

                if (primaryUrl is not null)
                {
                    db.ProductImages.Add(new ProductImage
                    {
                        ProductId = productId,
                        ImageUrl = primaryUrl,
                        IsPrimary = true,
                        SortOrder = ++nextSortOrder,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                foreach (var detailUrl in detailUrls)
                {
                    db.ProductImages.Add(new ProductImage
                    {
                        ProductId = productId,
                        ImageUrl = detailUrl,
                        IsPrimary = false,
                        SortOrder = ++nextSortOrder,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                if (primaryUrl is not null || detailUrls.Count > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch
        {
            foreach (var storedUrl in storedUrls)
            {
                try
                {
                    await storage.DeleteAsync(storedUrl, cancellationToken);
                }
                catch (Exception cleanupException)
                {
                    logger.LogWarning(cleanupException, "Unable to clean up an uncommitted product image file.");
                }
            }

            throw;
        }
    }

    public async Task<bool> DeleteImageAsync(
        int productId,
        int imageId,
        CancellationToken cancellationToken = default)
    {
        var image = await db.ProductImages.SingleOrDefaultAsync(
            item => item.Id == imageId && item.ProductId == productId,
            cancellationToken);
        if (image is null)
        {
            return false;
        }

        var relativeUrl = image.ImageUrl;
        db.ProductImages.Remove(image);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await storage.DeleteAsync(relativeUrl, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Image record {ImageId} was removed but file cleanup failed.", imageId);
        }

        return true;
    }
}
