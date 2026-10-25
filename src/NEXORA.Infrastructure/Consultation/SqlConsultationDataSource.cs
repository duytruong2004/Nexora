using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Consultation;

/// <summary>Loads consultation configuration and candidates from SQL Server; no catalog data is hardcoded here.</summary>
public sealed class SqlConsultationDataSource(AppDbContext db) : IConsultationDataSource
{
    public async Task<IReadOnlyList<string>> GetActiveBrandNamesAsync(CancellationToken cancellationToken = default) =>
        await db.Brands.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => item.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ConsultationNeedOption>> GetActiveUsageNeedsAsync(CancellationToken cancellationToken = default) =>
        await db.UsageNeeds.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new ConsultationNeedOption(item.Code, item.Name))
            .ToListAsync(cancellationToken);

    public async Task<ConsultationDataContext?> GetContextAsync(
        string categoryName,
        string usageNeedCode,
        CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.AsNoTracking()
            .Where(item => item.Name == categoryName && item.IsActive)
            .Select(item => new { item.Id, item.Name })
            .SingleOrDefaultAsync(cancellationToken);
        var usageNeed = await db.UsageNeeds.AsNoTracking()
            .Where(item => item.Code == usageNeedCode && item.IsActive)
            .Select(item => new { item.Id, item.Code, item.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (category is null || usageNeed is null) return null;

        var specifications = await db.CategorySpecifications.AsNoTracking()
            .Where(assignment => assignment.CategoryId == category.Id && assignment.Specification.IsActive)
            .OrderBy(assignment => assignment.SortOrder)
            .Select(assignment => new ConsultationSpecificationDefinition(
                assignment.SpecificationId,
                assignment.Specification.Name,
                assignment.Specification.DataType,
                assignment.Specification.Unit))
            .ToListAsync(cancellationToken);
        var rules = await db.RecommendationRules.AsNoTracking()
            .Where(rule => rule.UsageNeedId == usageNeed.Id && rule.CategoryId == category.Id &&
                           rule.IsActive && rule.Specification.IsActive)
            .OrderBy(rule => rule.Id)
            .Select(rule => new ConsultationRuleData(
                rule.Id,
                rule.SpecificationId,
                rule.Specification.Name,
                rule.Specification.DataType,
                rule.Specification.Unit,
                rule.Operator,
                rule.ThresholdValue,
                rule.Weight,
                rule.IsRequired))
            .ToListAsync(cancellationToken);

        return new ConsultationDataContext(category.Id, category.Name, usageNeed.Id, usageNeed.Code,
            usageNeed.Name, specifications, rules);
    }

    public async Task<IReadOnlyList<ConsultationProduct>> FindCandidatesAsync(
        CandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Product> products = db.Products.AsNoTracking()
            .Where(product => product.CategoryId == query.CategoryId &&
                              product.Status == ProductStatuses.Active &&
                              product.Category.IsActive && product.Brand.IsActive);

        if (!string.IsNullOrWhiteSpace(query.BrandName))
        {
            products = products.Where(product => product.Brand.Name == query.BrandName);
        }

        if (query.MinimumPrice is decimal minimumPrice)
        {
            products = products.Where(product => (product.DiscountPrice ?? product.Price) >= minimumPrice);
        }

        if (query.MaximumPrice is decimal maximumPrice)
        {
            products = products.Where(product => (product.DiscountPrice ?? product.Price) <= maximumPrice);
        }

        foreach (var (specificationId, operation, value) in query.NumericConstraints)
        {
            products = operation switch
            {
                RecommendationRuleOperators.Equal => products.Where(product => product.Specifications.Any(specification =>
                    specification.SpecificationId == specificationId && specification.NumericValue == value)),
                RecommendationRuleOperators.NotEqual => products.Where(product => product.Specifications.Any(specification =>
                    specification.SpecificationId == specificationId && specification.NumericValue.HasValue &&
                    specification.NumericValue != value)),
                RecommendationRuleOperators.GreaterThan => products.Where(product => product.Specifications.Any(specification =>
                    specification.SpecificationId == specificationId && specification.NumericValue > value)),
                RecommendationRuleOperators.GreaterThanOrEqual => products.Where(product => product.Specifications.Any(specification =>
                    specification.SpecificationId == specificationId && specification.NumericValue >= value)),
                RecommendationRuleOperators.LessThan => products.Where(product => product.Specifications.Any(specification =>
                    specification.SpecificationId == specificationId && specification.NumericValue < value)),
                RecommendationRuleOperators.LessThanOrEqual => products.Where(product => product.Specifications.Any(specification =>
                    specification.SpecificationId == specificationId && specification.NumericValue <= value)),
                _ => products.Where(_ => false)
            };
        }

        return await products.OrderBy(product => product.Id)
            .Select(product => new ConsultationProduct(
                product.Id,
                product.Name,
                product.Sku,
                product.CategoryId,
                product.Category.Name,
                product.Brand.Name,
                product.Price,
                product.DiscountPrice,
                product.Images.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder)
                    .Select(image => image.ImageUrl).FirstOrDefault(),
                product.Description,
                product.Specifications
                    .Where(value => value.Specification.IsActive && value.Specification.Categories.Any(category => category.CategoryId == product.CategoryId))
                    .OrderBy(value => value.SpecificationId)
                    .Select(value => new ConsultationSpecificationValue(
                        value.SpecificationId,
                        value.Specification.Name,
                        value.Specification.DataType,
                        value.Specification.Unit,
                        value.Value,
                        value.NumericValue,
                        value.DisplayValue))
                    .ToList())
            {
                AvailableQuantity = product.Inventory == null
                    ? null
                    : product.Inventory.QuantityInStock - product.Inventory.ReservedQuantity
            })
            .ToListAsync(cancellationToken);
    }
}
