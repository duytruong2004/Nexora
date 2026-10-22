using NEXORA.Application.Catalog;

namespace NEXORA.Web.Models;

public sealed class StorefrontHomeViewModel
{
    public CatalogHomeData Data { get; init; } = new([], []);
}

public sealed class CustomerCatalogViewModel
{
    public CatalogSearchFilter Filter { get; init; } = new(null, null, null, null, null, null);
    public CatalogProductPage Page { get; init; } = new([], [], [], [], null, 0, 1, 12);
}

public sealed class CustomerProductDetailsViewModel
{
    public CatalogProductDetails Product { get; init; } = null!;
    public IReadOnlyList<NEXORA.Application.Engagement.PublicProductReview> ApprovedReviews { get; init; } = [];
    public IReadOnlyList<CatalogProductCard> RelatedProducts { get; init; } = [];
}

public sealed class CatalogComparisonViewModel
{
    public CatalogComparison? Comparison { get; init; }
    public string? Error { get; init; }
}
