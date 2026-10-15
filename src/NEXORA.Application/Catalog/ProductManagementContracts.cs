namespace NEXORA.Application.Catalog;

public sealed record ProductInput(
    int? Id,
    int CategoryId,
    int BrandId,
    string Name,
    string Sku,
    string? Description,
    decimal Price,
    decimal? DiscountPrice,
    int WarrantyMonths,
    string Status);

public sealed record ProductAdminFilter(
    string? Search,
    int? CategoryId,
    int? BrandId,
    string? Status,
    int Page = 1,
    int PageSize = 20);

public sealed record ProductAdminRow(
    int Id,
    string Name,
    string Sku,
    string CategoryName,
    string BrandName,
    decimal Price,
    decimal? DiscountPrice,
    string Status,
    string? PrimaryImageUrl);

public sealed record ProductAdminPage(
    IReadOnlyList<ProductAdminRow> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record ProductImageItem(int Id, string ImageUrl, bool IsPrimary, int SortOrder);
public sealed record ProductAdminSpecificationItem(string Name, string DataType, string? Unit, string Value, string? DisplayValue);

public sealed record ProductAdminDetails(
    int Id,
    int CategoryId,
    string CategoryName,
    int BrandId,
    string BrandName,
    string Name,
    string Slug,
    string Sku,
    string? Description,
    decimal Price,
    decimal? DiscountPrice,
    int WarrantyMonths,
    string Status,
    IReadOnlyList<ProductImageItem> Images,
    IReadOnlyList<ProductAdminSpecificationItem> Specifications);

public sealed record ProductImageFile(Stream Content, string OriginalFileName, long Length);

public sealed class ProductImageValidationException(string message) : Exception(message);

public interface IProductManagementService
{
    Task<ProductAdminPage> SearchAsync(ProductAdminFilter filter, CancellationToken cancellationToken = default);
    Task<ProductAdminDetails?> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<int> SaveAsync(ProductInput input, CancellationToken cancellationToken = default);
    Task<bool> DeactivateAsync(int id, CancellationToken cancellationToken = default);
}

public interface IProductImageStorage
{
    Task<string> SaveAsync(ProductImageFile image, CancellationToken cancellationToken = default);
    Task DeleteAsync(string relativeUrl, CancellationToken cancellationToken = default);
}

public interface IProductImageManagementService
{
    Task SaveImagesAsync(
        int productId,
        ProductImageFile? primaryImage,
        IReadOnlyList<ProductImageFile> detailImages,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteImageAsync(int productId, int imageId, CancellationToken cancellationToken = default);
}
