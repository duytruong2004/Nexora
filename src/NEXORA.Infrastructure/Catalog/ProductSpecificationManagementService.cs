using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

public sealed class ProductSpecificationManagementService(AppDbContext db) : IProductSpecificationManagementService
{
    public async Task<ProductSpecificationEditor?> GetEditorAsync(
        int productId,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking()
            .Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken);
        if (product is null)
        {
            return null;
        }

        var categorySpecifications = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.CategoryId == product.CategoryId && item.Specification.IsActive)
            .Include(item => item.Specification)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Specification.Name)
            .ToListAsync(cancellationToken);
        var existing = await db.ProductSpecifications.AsNoTracking()
            .Where(item => item.ProductId == productId)
            .ToDictionaryAsync(item => item.SpecificationId, cancellationToken);
        var enumSpecificationIds = categorySpecifications
            .Where(item => item.Specification.DataType == SpecificationDataTypes.Enumeration)
            .Select(item => item.SpecificationId)
            .ToArray();
        var enumValues = enumSpecificationIds.Length == 0
            ? []
            : await db.ProductSpecifications.AsNoTracking()
                .Where(value => enumSpecificationIds.Contains(value.SpecificationId) &&
                                value.Product.CategoryId == product.CategoryId)
                .Select(value => new { value.SpecificationId, value.Value })
                .Distinct()
                .ToListAsync(cancellationToken);

        var values = categorySpecifications.Select(assignment =>
        {
            existing.TryGetValue(assignment.SpecificationId, out var productValue);
            var options = enumValues.Where(value => value.SpecificationId == assignment.SpecificationId)
                .Select(value => value.Value)
                .OrderBy(value => value)
                .ToList();
            return new ProductSpecificationEditorValue(
                assignment.SpecificationId,
                assignment.Specification.Name,
                assignment.Specification.Unit,
                assignment.Specification.DataType,
                assignment.IsRequired,
                productValue?.Value,
                productValue?.DisplayValue,
                options);
        }).ToList();

        return new ProductSpecificationEditor(product.Id, product.Name, product.Category.Name, values);
    }

    public async Task SaveValuesAsync(
        int productId,
        IReadOnlyList<ProductSpecificationValueInput> values,
        CancellationToken cancellationToken = default)
    {
        var product = await db.Products.Include(item => item.Category)
            .SingleOrDefaultAsync(item => item.Id == productId, cancellationToken)
            ?? throw new CatalogValidationException(nameof(productId), "Không tìm thấy sản phẩm.");
        var assignments = await db.CategorySpecifications
            .Where(item => item.CategoryId == product.CategoryId && item.Specification.IsActive)
            .Include(item => item.Specification)
            .ToListAsync(cancellationToken);
        var assignmentById = assignments.ToDictionary(item => item.SpecificationId);
        var inputById = new Dictionary<int, ProductSpecificationValueInput>();
        foreach (var value in values)
        {
            inputById.TryAdd(value.SpecificationId, value);
        }

        var assignmentError = SpecificationAssignmentPolicy.ValidateSubmittedIds(
            assignmentById.Keys.ToArray(), values.Select(value => value.SpecificationId).ToArray());
        if (assignmentError is not null)
        {
            throw new CatalogValidationException(nameof(values), assignmentError);
        }

        foreach (var assignment in assignments.Where(item => item.IsRequired))
        {
            if (!inputById.TryGetValue(assignment.SpecificationId, out var requiredValue) ||
                string.IsNullOrWhiteSpace(requiredValue.Value))
            {
                throw new CatalogValidationException($"Values[{assignment.SpecificationId}]",
                    $"Thông số {assignment.Specification.Name} là bắt buộc.");
            }
        }

        var existing = await db.ProductSpecifications
            .Where(item => item.ProductId == productId)
            .ToDictionaryAsync(item => item.SpecificationId, cancellationToken);

        foreach (var assignment in assignments)
        {
            if (!inputById.TryGetValue(assignment.SpecificationId, out var input))
            {
                continue;
            }

            existing.TryGetValue(assignment.SpecificationId, out var productValue);
            if (string.IsNullOrWhiteSpace(input.Value))
            {
                if (assignment.IsRequired)
                {
                    continue; // The required-field validation above reports this case.
                }

                if (productValue is not null)
                {
                    db.ProductSpecifications.Remove(productValue);
                }

                continue;
            }

            var error = SpecificationValuePolicy.TryNormalize(
                assignment.Specification.DataType,
                input.Value,
                input.DisplayValue,
                assignment.Specification.Unit,
                out var normalized);
            if (error is not null || normalized is null)
            {
                throw new CatalogValidationException($"Values[{assignment.SpecificationId}]", error ?? "Giá trị không hợp lệ.");
            }

            if (productValue is null)
            {
                productValue = new ProductSpecification
                {
                    ProductId = productId,
                    SpecificationId = assignment.SpecificationId
                };
                db.ProductSpecifications.Add(productValue);
            }

            productValue.Value = normalized.Value;
            productValue.NumericValue = normalized.NumericValue;
            productValue.DisplayValue = normalized.DisplayValue;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
