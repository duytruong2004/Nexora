using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Application.Engagement;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

public sealed class CatalogController(ICustomerCatalogQueryService catalog, IReviewService reviews) : Controller
{
    private const int CatalogPageSize = 10;

    public async Task<IActionResult> Index(
        string? search,
        int? categoryId,
        int? brandId,
        decimal? minimumPrice,
        decimal? maximumPrice,
        string? sortBy,
        List<CatalogSpecificationFilter>? specificationFilters,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var filter = new CatalogSearchFilter(
            search, categoryId, brandId, minimumPrice, maximumPrice, sortBy, page, CatalogPageSize)
        {
            SpecificationFilters = specificationFilters ?? []
        };
        var result = await catalog.SearchAsync(filter, cancellationToken);
        return View(new CustomerCatalogViewModel { Filter = filter with { Page = result.Page }, Page = result });
    }

    public async Task<IActionResult> Category(int id, CancellationToken cancellationToken)
    {
        var category = await catalog.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var filter = new CatalogSearchFilter(null, id, null, null, null, null, PageSize: CatalogPageSize);
        var result = await catalog.SearchAsync(filter, cancellationToken);
        return View("Index", new CustomerCatalogViewModel { Filter = filter, Page = result });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductAsync(id, cancellationToken);
        if (product is null) return NotFound();

        var approvedReviews = await reviews.GetApprovedForProductAsync(
            product.Id, cancellationToken: cancellationToken);
        var relatedPage = await catalog.SearchAsync(
            new CatalogSearchFilter(null, product.CategoryId, null, null, null, "name-asc", PageSize: 8),
            cancellationToken);

        return View(new CustomerProductDetailsViewModel
        {
            Product = product,
            ApprovedReviews = approvedReviews,
            RelatedProducts = relatedPage.Items.Where(item => item.Id != product.Id).Take(4).ToArray()
        });
    }

    public async Task<IActionResult> Compare(int[] ids, CancellationToken cancellationToken)
    {
        try
        {
            return View(new CatalogComparisonViewModel
            {
                Comparison = await catalog.CompareAsync(ids, cancellationToken)
            });
        }
        catch (CatalogValidationException exception)
        {
            return View(new CatalogComparisonViewModel { Error = exception.Message });
        }
    }
}
