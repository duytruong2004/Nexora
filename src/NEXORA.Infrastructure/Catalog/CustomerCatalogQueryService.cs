using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

public sealed class CustomerCatalogQueryService(AppDbContext db) : ICustomerCatalogQueryService
{
    private const int MaximumPageSize = 48;

    public async Task<CatalogHomeData> GetHomeAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.ParentId)
            .ThenBy(category => category.Name)
            .Select(category => new CatalogCategoryOption(category.Id, category.Name, category.ParentId))
            .ToListAsync(cancellationToken);

        var products = await VisibleProducts()
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Take(8)
            .Select(ToCard())
            .ToListAsync(cancellationToken);

        return new CatalogHomeData(categories, products);
    }

    public async Task<CatalogProductPage> SearchAsync(
        CatalogSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var filterableSpecifications = filter.CategoryId is > 0
            ? await db.CategorySpecifications.AsNoTracking()
                .Where(assignment => assignment.CategoryId == filter.CategoryId &&
                                     assignment.IsFilterable && assignment.Specification.IsActive)
                .Include(assignment => assignment.Specification)
                .OrderBy(assignment => assignment.SortOrder)
                .ToListAsync(cancellationToken)
            : [];
        var specificationTypes = filterableSpecifications.ToDictionary(
            assignment => assignment.SpecificationId,
            assignment => assignment.Specification.DataType);
        var query = CustomerCatalogQueryComposer.ApplyFilters(VisibleProducts(), filter, specificationTypes);
        var orderedQuery = CustomerCatalogQueryComposer.ApplySorting(query, filter.SortBy);

        var totalCount = await orderedQuery.CountAsync(cancellationToken);
        var pageWindow = CatalogPageWindow.Create(filter.Page, filter.PageSize, totalCount, MaximumPageSize);
        var items = await orderedQuery.Skip(pageWindow.Skip)
            .Take(pageWindow.PageSize)
            .Select(ToCard())
            .ToListAsync(cancellationToken);

        var categories = await db.Categories.AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.ParentId)
            .ThenBy(category => category.Name)
            .Select(category => new CatalogCategoryOption(category.Id, category.Name, category.ParentId))
            .ToListAsync(cancellationToken);
        var brands = await db.Brands.AsNoTracking()
            .Where(brand => brand.IsActive)
            .OrderBy(brand => brand.Name)
            .Select(brand => new CatalogBrandOption(brand.Id, brand.Name))
            .ToListAsync(cancellationToken);
        var selectedCategoryName = filter.CategoryId is > 0
            ? await db.Categories.AsNoTracking()
                .Where(category => category.Id == filter.CategoryId && category.IsActive)
                .Select(category => category.Name)
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var specificationFacets = await BuildFacetsAsync(filter.CategoryId, filterableSpecifications, cancellationToken);

        return new CatalogProductPage(items, categories, brands, specificationFacets,
            selectedCategoryName, totalCount, pageWindow.Page, pageWindow.PageSize);
    }

    public Task<CatalogCategoryOption?> GetCategoryAsync(int categoryId, CancellationToken cancellationToken = default) =>
        db.Categories.AsNoTracking()
            .Where(category => category.Id == categoryId && category.IsActive)
            .Select(category => new CatalogCategoryOption(category.Id, category.Name, category.ParentId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<CatalogProductDetails?> GetProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        var product = await VisibleProducts()
            .Where(item => item.Id == productId)
            .AsSplitQuery()
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Slug,
                item.Sku,
                CategoryId = item.Category.Id,
                CategoryName = item.Category.Name,
                BrandName = item.Brand.Name,
                item.Price,
                item.DiscountPrice,
                item.Description,
                item.WarrantyMonths,
                AvailableQuantity = item.Inventory == null
                    ? (int?)null
                    : item.Inventory.QuantityInStock - item.Inventory.ReservedQuantity,
                Images = item.Images.OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => new ProductImageItem(image.Id, image.ImageUrl, image.IsPrimary, image.SortOrder))
                    .ToList(),
                Specifications = item.Specifications
                    .Where(value => value.Specification.IsActive)
                    .OrderBy(value => value.Specification.Name)
                    .Select(value => new CatalogProductSpecification(
                        value.SpecificationId,
                        value.Specification.Name,
                        value.Specification.DataType,
                        value.Specification.Unit,
                        value.Value,
                        value.NumericValue,
                        value.DisplayValue))
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return product is null
            ? null
            : new CatalogProductDetails(
                product.Id,
                product.Name,
                product.Slug,
                product.Sku,
                product.CategoryName,
                product.CategoryId,
                product.BrandName,
                product.Price,
                product.DiscountPrice,
                product.Description,
                product.WarrantyMonths,
                product.Images,
                product.Specifications)
            {
                AvailableQuantity = product.AvailableQuantity
            };
    }

    public async Task<CatalogComparison> CompareAsync(
        IReadOnlyList<int> productIds,
        CancellationToken cancellationToken = default)
    {
        var selectionError = ProductComparisonPolicy.ValidateSelection(productIds);
        if (selectionError is not null)
        {
            throw new CatalogValidationException(nameof(productIds), selectionError);
        }

        var products = new List<CatalogProductDetails>(productIds.Count);
        foreach (var productId in productIds)
        {
            var product = await GetProductAsync(productId, cancellationToken);
            if (product is null)
            {
                throw new CatalogValidationException(nameof(productIds), "Có sản phẩm không còn được kinh doanh hoặc không tồn tại.");
            }

            products.Add(product);
        }

        var categoryError = ProductComparisonPolicy.ValidateCategoryCompatibility(
            products.Select(product => product.CategoryId).ToArray());
        if (categoryError is not null)
        {
            throw new CatalogValidationException(nameof(productIds), categoryError.Trim());
        }

        var allSpecifications = products.SelectMany(product => product.Specifications)
            .GroupBy(specification => specification.SpecificationId)
            .Select(group => group.First())
            .OrderBy(specification => specification.Name)
            .ToList();
        var comparisonRows = allSpecifications.Select(specification =>
        {
            var values = products.Select(product =>
            {
                var productValue = product.Specifications.SingleOrDefault(
                    item => item.SpecificationId == specification.SpecificationId);
                return productValue is null ? null : FormatSpecificationValue(productValue);
            }).ToList();
            var isDifferent = ProductComparisonPolicy.AreSpecificationValuesDifferent(values);
            return new CatalogComparisonRow(
                specification.SpecificationId,
                specification.Name,
                specification.Unit,
                values,
                isDifferent);
        }).ToList();

        return new CatalogComparison(products, comparisonRows);
    }

    private async Task<IReadOnlyList<CatalogSpecificationFacet>> BuildFacetsAsync(
        int? categoryId,
        IReadOnlyList<CategorySpecification> assignments,
        CancellationToken cancellationToken)
    {
        if (categoryId is not > 0 || assignments.Count == 0)
        {
            return [];
        }

        var numericIds = assignments
            .Where(item => item.Specification.DataType == SpecificationDataTypes.Number)
            .Select(item => item.SpecificationId)
            .ToArray();
        var numericAggregates = numericIds.Length == 0
            ? []
            : await db.ProductSpecifications.AsNoTracking()
                .Where(value => numericIds.Contains(value.SpecificationId) && value.NumericValue.HasValue &&
                                value.Product.CategoryId == categoryId && value.Product.Status == ProductStatuses.Active &&
                                value.Product.Category.IsActive && value.Product.Brand.IsActive)
                .GroupBy(value => value.SpecificationId)
                .Select(group => new
                {
                    SpecificationId = group.Key,
                    Minimum = group.Min(value => value.NumericValue),
                    Maximum = group.Max(value => value.NumericValue)
                })
                .ToListAsync(cancellationToken);
        var enumIds = assignments
            .Where(item => item.Specification.DataType is SpecificationDataTypes.Enumeration or SpecificationDataTypes.Boolean)
            .Select(item => item.SpecificationId)
            .ToArray();
        var enumOptions = enumIds.Length == 0
            ? []
            : await db.ProductSpecifications.AsNoTracking()
                .Where(value => enumIds.Contains(value.SpecificationId) &&
                                value.Product.CategoryId == categoryId && value.Product.Status == ProductStatuses.Active &&
                                value.Product.Category.IsActive && value.Product.Brand.IsActive)
                .Select(value => new { value.SpecificationId, value.Value, value.DisplayValue })
                .Distinct()
                .OrderBy(value => value.SpecificationId)
                .ThenBy(value => value.Value)
                .ToListAsync(cancellationToken);

        var facets = new List<CatalogSpecificationFacet>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var specification = assignment.Specification;
            if (specification.DataType == SpecificationDataTypes.Number)
            {
                var aggregate = numericAggregates.SingleOrDefault(value => value.SpecificationId == assignment.SpecificationId);
                facets.Add(new CatalogSpecificationFacet(
                    specification.Id, specification.Name, specification.DataType, specification.Unit,
                    aggregate?.Minimum, aggregate?.Maximum, []));
            }
            else
            {
                var options = enumOptions
                    .Where(value => value.SpecificationId == assignment.SpecificationId)
                    .Take(100)
                    .Select(value => new CatalogSpecificationOption(value.Value, value.DisplayValue ?? value.Value))
                    .ToList();
                facets.Add(new CatalogSpecificationFacet(
                    specification.Id, specification.Name, specification.DataType, specification.Unit,
                    null, null, options));
            }
        }

        return facets;
    }

    private static string FormatSpecificationValue(CatalogProductSpecification value)
    {
        if (!string.IsNullOrWhiteSpace(value.DisplayValue))
        {
            return value.DisplayValue;
        }

        if (value.DataType == SpecificationDataTypes.Boolean && bool.TryParse(value.Value, out var booleanValue))
        {
            return booleanValue ? "Có" : "Không";
        }

        if (value.DataType == SpecificationDataTypes.Number && !string.IsNullOrWhiteSpace(value.Unit))
        {
            return $"{value.Value} {value.Unit}";
        }

        return value.Value;
    }

    private IQueryable<Product> VisibleProducts() => db.Products.AsNoTracking()
        .Where(product => product.Status == ProductStatuses.Active &&
                          product.Category.IsActive && product.Brand.IsActive);

    private static System.Linq.Expressions.Expression<Func<Product, CatalogProductCard>> ToCard() => product =>
        new CatalogProductCard(
            product.Id,
            product.CategoryId,
            product.Name,
            product.Sku,
            product.Category.Name,
            product.Brand.Name,
            product.Price,
            product.DiscountPrice,
            product.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder)
                .Select(image => image.ImageUrl)
                .FirstOrDefault(),
            product.Description)
        {
            AvailableQuantity = product.Inventory == null
                ? (int?)null
                : product.Inventory.QuantityInStock - product.Inventory.ReservedQuantity,
            KeySpecifications = product.Specifications
                .Where(value => value.Specification.IsActive)
                .OrderBy(value => value.Specification.Name)
                .Take(3)
                .Select(value => new CatalogProductSpecification(
                    value.SpecificationId,
                    value.Specification.Name,
                    value.Specification.DataType,
                    value.Specification.Unit,
                    value.Value,
                    value.NumericValue,
                    value.DisplayValue))
                .ToList()
        };
}
