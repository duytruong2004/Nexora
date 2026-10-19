namespace NEXORA.Application.Catalog;

public sealed record CatalogCategoryOption(int Id, string Name, int? ParentId);

public sealed record CatalogBrandOption(int Id, string Name);

public sealed record CatalogProductCard(
    int Id,
    int CategoryId,
    string Name,
    string Sku,
    string CategoryName,
    string BrandName,
    decimal Price,
    decimal? DiscountPrice,
    string? PrimaryImageUrl,
    string? Description)
{
    public IReadOnlyList<CatalogProductSpecification> KeySpecifications { get; init; } = [];

    /// <summary>Null means the product has no inventory record.</summary>
    public int? AvailableQuantity { get; init; }
}

public sealed record CatalogProductSpecification(
    int SpecificationId,
    string Name,
    string DataType,
    string? Unit,
    string Value,
    decimal? NumericValue,
    string? DisplayValue);

public sealed record CatalogProductDetails(
    int Id,
    string Name,
    string Slug,
    string Sku,
    string CategoryName,
    int CategoryId,
    string BrandName,
    decimal Price,
    decimal? DiscountPrice,
    string? Description,
    int WarrantyMonths,
    IReadOnlyList<ProductImageItem> Images,
    IReadOnlyList<CatalogProductSpecification> Specifications)
{
    /// <summary>Null means the product has no inventory record.</summary>
    public int? AvailableQuantity { get; init; }
}

public sealed record CatalogSearchFilter(
    string? Search,
    int? CategoryId,
    int? BrandId,
    decimal? MinimumPrice,
    decimal? MaximumPrice,
    string? SortBy,
    int Page = 1,
    int PageSize = 12)
{
    public IReadOnlyList<CatalogSpecificationFilter> SpecificationFilters { get; init; } = [];
}

public sealed class CatalogSpecificationFilter
{
    public int SpecificationId { get; set; }
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }
    public string? Value { get; set; }
}

public sealed record CatalogSpecificationOption(string Value, string DisplayValue);

public sealed record CatalogSpecificationFacet(
    int SpecificationId,
    string Name,
    string DataType,
    string? Unit,
    decimal? MinimumValue,
    decimal? MaximumValue,
    IReadOnlyList<CatalogSpecificationOption> Options);

public sealed record CatalogProductPage(
    IReadOnlyList<CatalogProductCard> Items,
    IReadOnlyList<CatalogCategoryOption> Categories,
    IReadOnlyList<CatalogBrandOption> Brands,
    IReadOnlyList<CatalogSpecificationFacet> SpecificationFacets,
    string? SelectedCategoryName,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record CatalogHomeData(
    IReadOnlyList<CatalogCategoryOption> Categories,
    IReadOnlyList<CatalogProductCard> Products);

public sealed record CatalogComparisonRow(
    int SpecificationId,
    string Name,
    string? Unit,
    IReadOnlyList<string?> Values,
    bool IsDifferent);

public sealed record CatalogComparison(
    IReadOnlyList<CatalogProductDetails> Products,
    IReadOnlyList<CatalogComparisonRow> Specifications);

public interface ICustomerCatalogQueryService
{
    Task<CatalogHomeData> GetHomeAsync(CancellationToken cancellationToken = default);
    Task<CatalogProductPage> SearchAsync(CatalogSearchFilter filter, CancellationToken cancellationToken = default);
    Task<CatalogCategoryOption?> GetCategoryAsync(int categoryId, CancellationToken cancellationToken = default);
    Task<CatalogProductDetails?> GetProductAsync(int productId, CancellationToken cancellationToken = default);
    Task<CatalogComparison> CompareAsync(IReadOnlyList<int> productIds, CancellationToken cancellationToken = default);
}
