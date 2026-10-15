using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;

namespace NEXORA.Infrastructure.Catalog;

/// <summary>Composes storefront filters as IQueryable expressions so EF executes them in SQL.</summary>
public static class CustomerCatalogQueryComposer
{
    public static IQueryable<Product> ApplyFilters(
        IQueryable<Product> source,
        CatalogSearchFilter filter,
        IReadOnlyDictionary<int, string> filterableSpecificationTypes)
    {
        var query = source;
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(product => product.Name.Contains(term) || product.Sku.Contains(term));
        }

        if (filter.CategoryId is > 0)
        {
            query = query.Where(product => product.CategoryId == filter.CategoryId);
        }

        if (filter.BrandId is > 0)
        {
            query = query.Where(product => product.BrandId == filter.BrandId);
        }

        if (filter.MinimumPrice is >= 0)
        {
            query = query.Where(product => (product.DiscountPrice ?? product.Price) >= filter.MinimumPrice);
        }

        if (filter.MaximumPrice is >= 0)
        {
            query = query.Where(product => (product.DiscountPrice ?? product.Price) <= filter.MaximumPrice);
        }

        foreach (var requestedFilter in filter.SpecificationFilters)
        {
            if (!filterableSpecificationTypes.TryGetValue(requestedFilter.SpecificationId, out var dataType))
            {
                continue;
            }

            var specificationId = requestedFilter.SpecificationId;
            if (dataType == SpecificationDataTypes.Number)
            {
                if (requestedFilter.MinimumValue.HasValue || requestedFilter.MaximumValue.HasValue)
                {
                    query = query.Where(product => product.Specifications.Any(value =>
                        value.SpecificationId == specificationId && value.NumericValue.HasValue &&
                        (!requestedFilter.MinimumValue.HasValue || value.NumericValue >= requestedFilter.MinimumValue) &&
                        (!requestedFilter.MaximumValue.HasValue || value.NumericValue <= requestedFilter.MaximumValue)));
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(requestedFilter.Value))
            {
                continue;
            }

            var requestedValue = requestedFilter.Value.Trim();
            if (dataType is SpecificationDataTypes.Text or SpecificationDataTypes.Enumeration)
            {
                query = query.Where(product => product.Specifications.Any(value =>
                    value.SpecificationId == specificationId && value.Value == requestedValue));
            }
            else if (dataType == SpecificationDataTypes.Boolean && bool.TryParse(requestedValue, out var booleanValue))
            {
                var canonicalValue = booleanValue ? "true" : "false";
                query = query.Where(product => product.Specifications.Any(value =>
                    value.SpecificationId == specificationId && value.Value == canonicalValue));
            }
        }

        return query;
    }

    public static IOrderedQueryable<Product> ApplySorting(IQueryable<Product> query, string? sortBy) => sortBy switch
    {
        "price-low" => query.OrderBy(product => product.DiscountPrice ?? product.Price).ThenBy(product => product.Id),
        "price-high" => query.OrderByDescending(product => product.DiscountPrice ?? product.Price).ThenBy(product => product.Id),
        "name-desc" => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
        _ => query.OrderBy(product => product.Name).ThenBy(product => product.Id)
    };
}
