using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

public sealed class SpecificationManagementService(AppDbContext db) : ISpecificationManagementService
{
    public async Task<IReadOnlyList<SpecificationAdminRow>> SearchAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.Specifications.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(specification => specification.Name.Contains(term));
        }

        return await query.OrderBy(specification => specification.Name)
            .Select(specification => new SpecificationAdminRow(
                specification.Id,
                specification.Name,
                specification.Unit,
                specification.DataType,
                specification.IsActive))
            .ToListAsync(cancellationToken);
    }

    public Task<SpecificationAdminRow?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        db.Specifications.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new SpecificationAdminRow(item.Id, item.Name, item.Unit, item.DataType, item.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> SaveAsync(SpecificationInput input, CancellationToken cancellationToken = default)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var unit = input.Unit?.Trim();
        var dataType = input.DataType?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            throw new CatalogValidationException(nameof(input.Name), "Tên thông số bắt buộc và không vượt quá 160 ký tự.");
        }

        if (unit is { Length: 0 })
        {
            unit = null;
        }

        if (unit?.Length > 40)
        {
            throw new CatalogValidationException(nameof(input.Unit), "Đơn vị đo không vượt quá 40 ký tự.");
        }

        if (dataType is not (SpecificationDataTypes.Number or SpecificationDataTypes.Text or
            SpecificationDataTypes.Enumeration or SpecificationDataTypes.Boolean))
        {
            throw new CatalogValidationException(nameof(input.DataType), "DataType phải là NUMBER, TEXT, ENUM hoặc BOOLEAN.");
        }

        Specification? specification = null;
        if (input.Id is int id)
        {
            if (id <= 0)
            {
                throw new CatalogValidationException(nameof(input.Id), "Mã thông số không hợp lệ.");
            }

            specification = await db.Specifications.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new CatalogValidationException(nameof(input.Id), "Không tìm thấy thông số.");
        }

        var excludedSpecificationId = specification?.Id ?? 0;
        var duplicate = await db.Specifications.AsNoTracking()
            .AnyAsync(item => item.Name == name && item.Id != excludedSpecificationId, cancellationToken);
        if (duplicate)
        {
            throw new CatalogValidationException(nameof(input.Name), "Tên thông số đã được sử dụng.");
        }

        if (specification is not null && specification.DataType != dataType)
        {
            var hasProductValues = await db.ProductSpecifications.AnyAsync(
                value => value.SpecificationId == specification.Id, cancellationToken);
            var hasRecommendationRules = await db.RecommendationRules.AnyAsync(
                rule => rule.SpecificationId == specification.Id, cancellationToken);
            if (hasProductValues || hasRecommendationRules)
            {
                throw new CatalogValidationException(nameof(input.DataType),
                    "Không thể đổi DataType khi thông số đã có giá trị sản phẩm hoặc quy tắc tư vấn.");
            }
        }

        if (specification is null)
        {
            specification = new Specification { Name = name, Unit = unit, DataType = dataType, IsActive = true };
            db.Specifications.Add(specification);
        }
        else
        {
            specification.Name = name;
            specification.Unit = unit;
            specification.DataType = dataType;
        }

        await db.SaveChangesAsync(cancellationToken);
        return specification.Id;
    }

    public async Task<bool> DeactivateAsync(int specificationId, CancellationToken cancellationToken = default)
    {
        var specification = await db.Specifications.SingleOrDefaultAsync(
            item => item.Id == specificationId, cancellationToken);
        if (specification is null)
        {
            return false;
        }

        specification.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CategorySpecificationConfigurationResult?> GetCategoryConfigurationAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken);
        if (category is null)
        {
            return null;
        }

        var assigned = await db.CategorySpecifications.AsNoTracking()
            .Where(item => item.CategoryId == categoryId)
            .Include(item => item.Specification)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Specification.Name)
            .Select(item => new CategorySpecificationAdminRow(
                item.SpecificationId,
                item.Specification.Name,
                item.Specification.Unit,
                item.Specification.DataType,
                item.IsFilterable,
                item.IsRequired,
                item.SortOrder))
            .ToListAsync(cancellationToken);
        var assignedIds = assigned.Select(item => item.SpecificationId).ToHashSet();
        var available = await db.Specifications.AsNoTracking()
            .Where(item => item.IsActive && !assignedIds.Contains(item.Id))
            .OrderBy(item => item.Name)
            .Select(item => new SpecificationAdminRow(item.Id, item.Name, item.Unit, item.DataType, item.IsActive))
            .ToListAsync(cancellationToken);

        return new CategorySpecificationConfigurationResult(categoryId, category.Name, assigned, available);
    }

    public async Task SaveCategorySpecificationAsync(
        CategorySpecificationInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.CategoryId <= 0 || input.SpecificationId <= 0 || input.SortOrder < 0)
        {
            throw new CatalogValidationException(nameof(input.SpecificationId), "Danh mục, thông số và thứ tự hiển thị phải hợp lệ.");
        }

        var categoryIsActive = await db.Categories.AnyAsync(
            item => item.Id == input.CategoryId && item.IsActive, cancellationToken);
        if (!categoryIsActive)
        {
            throw new CatalogValidationException(nameof(input.CategoryId), "Danh mục không tồn tại hoặc đã ngừng sử dụng.");
        }

        var specificationIsActive = await db.Specifications.AnyAsync(
            item => item.Id == input.SpecificationId && item.IsActive, cancellationToken);
        if (!specificationIsActive)
        {
            throw new CatalogValidationException(nameof(input.SpecificationId), "Thông số không tồn tại hoặc đã ngừng sử dụng.");
        }

        if (input.IsRequired)
        {
            var hasActiveProductsWithoutValue = await db.Products.AnyAsync(product =>
                product.CategoryId == input.CategoryId && product.Status == ProductStatuses.Active &&
                !db.ProductSpecifications.Any(value =>
                    value.ProductId == product.Id && value.SpecificationId == input.SpecificationId),
                cancellationToken);
            if (hasActiveProductsWithoutValue)
            {
                throw new CatalogValidationException(nameof(input.IsRequired),
                    "Không thể đặt bắt buộc khi còn sản phẩm đang kinh doanh thiếu giá trị. Hãy bổ sung dữ liệu trước.");
            }
        }

        var assignment = await db.CategorySpecifications.SingleOrDefaultAsync(
            item => item.CategoryId == input.CategoryId && item.SpecificationId == input.SpecificationId,
            cancellationToken);
        if (assignment is null)
        {
            assignment = new CategorySpecification
            {
                CategoryId = input.CategoryId,
                SpecificationId = input.SpecificationId
            };
            db.CategorySpecifications.Add(assignment);
        }

        assignment.IsFilterable = input.IsFilterable;
        assignment.IsRequired = input.IsRequired;
        assignment.SortOrder = input.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveCategorySpecificationAsync(
        int categoryId,
        int specificationId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await db.CategorySpecifications.SingleOrDefaultAsync(
            item => item.CategoryId == categoryId && item.SpecificationId == specificationId,
            cancellationToken);
        if (assignment is null)
        {
            return false;
        }

        var hasProductValues = await db.ProductSpecifications.AnyAsync(
            value => value.SpecificationId == specificationId && value.Product.CategoryId == categoryId,
            cancellationToken);
        var hasRecommendationRule = await db.RecommendationRules.AnyAsync(
            rule => rule.CategoryId == categoryId && rule.SpecificationId == specificationId,
            cancellationToken);
        if (hasProductValues || hasRecommendationRule)
        {
            throw new CatalogValidationException(nameof(specificationId),
                "Không thể gỡ thông số đang được dùng bởi sản phẩm hoặc quy tắc tư vấn của danh mục.");
        }

        db.CategorySpecifications.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
