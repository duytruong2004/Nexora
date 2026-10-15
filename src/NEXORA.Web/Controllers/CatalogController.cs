using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

public sealed class CatalogController(ICustomerCatalogQueryService catalog) : Controller
{
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
            search, categoryId, brandId, minimumPrice, maximumPrice, sortBy, page)
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

        var filter = new CatalogSearchFilter(null, id, null, null, null, null);
        var result = await catalog.SearchAsync(filter, cancellationToken);
        return View("Index", new CustomerCatalogViewModel { Filter = filter, Page = result });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await catalog.GetProductAsync(id, cancellationToken);
        return product is null
            ? NotFound()
            : View(new CustomerProductDetailsViewModel { Product = product });
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
