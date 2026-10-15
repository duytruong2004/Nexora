using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

public sealed class CategoryBrandManagementService(AppDbContext db) : ICategoryBrandManagementService
{
    public async Task<IReadOnlyList<Category>> SearchCategoriesAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.Categories.AsNoTracking().Include(category => category.Parent).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(category => category.Name.Contains(term));
        }

        return await query.OrderBy(category => category.ParentId)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<Category?> GetCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        db.Categories.AsNoTracking().Include(category => category.Parent)
            .SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    public async Task<int> SaveCategoryAsync(CategoryInput input, CancellationToken cancellationToken = default)
    {
        var name = RequireText(input.Name, nameof(input.Name), 120);
        var description = OptionalText(input.Description, nameof(input.Description), 1000);
        if (input.Id is not null && input.Id <= 0)
        {
            throw new CatalogValidationException(nameof(input.Id), "Mã danh mục không hợp lệ.");
        }

        Category? category = null;
        if (input.Id is int categoryId)
        {
            category = await db.Categories.SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken)
                ?? throw new CatalogValidationException(nameof(input.Id), "Không tìm thấy danh mục.");
        }

        if (input.ParentId is int parentId)
        {
            if (parentId == input.Id)
            {
                throw new CatalogValidationException(nameof(input.ParentId), "Danh mục không thể là cha của chính nó.");
            }

            var parent = await db.Categories.SingleOrDefaultAsync(x => x.Id == parentId && x.IsActive, cancellationToken);
            if (parent is null)
            {
                throw new CatalogValidationException(nameof(input.ParentId), "Danh mục cha không tồn tại hoặc đã ngừng sử dụng.");
            }

            var visited = new HashSet<int>();
            var ancestorId = parent.ParentId;
            while (ancestorId is int currentAncestorId && visited.Add(currentAncestorId))
            {
                if (currentAncestorId == input.Id)
                {
                    throw new CatalogValidationException(nameof(input.ParentId), "Không thể chuyển danh mục xuống bên dưới danh mục con của nó.");
                }

                ancestorId = await db.Categories.AsNoTracking()
                    .Where(x => x.Id == currentAncestorId)
                    .Select(x => x.ParentId)
                    .SingleOrDefaultAsync(cancellationToken);
            }
        }

        var siblingQuery = db.Categories.AsNoTracking().Where(x => x.ParentId == input.ParentId);
        if (input.Id is int existingCategoryId)
        {
            siblingQuery = siblingQuery.Where(x => x.Id != existingCategoryId);
        }

        var siblings = await siblingQuery.Select(x => x.Name).ToListAsync(cancellationToken);
        if (siblings.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new CatalogValidationException(nameof(input.Name), "Danh mục cùng cấp đã có tên này.");
        }

        if (category is null)
        {
            category = new Category
            {
                Name = name,
                Description = description,
                ParentId = input.ParentId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
        }
        else
        {
            category.Name = name;
            category.Description = description;
            category.ParentId = input.ParentId;
            category.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return category.Id;
    }

    public async Task<CatalogRemovalOutcome> RemoveOrDeactivateCategoryAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (category is null)
        {
            return CatalogRemovalOutcome.NotFound;
        }

        var hasProducts = await db.Products.AnyAsync(x => x.CategoryId == id, cancellationToken);
        var hasChildren = await db.Categories.AnyAsync(x => x.ParentId == id, cancellationToken);
        if (hasProducts || hasChildren)
        {
            category.IsActive = false;
            category.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return CatalogRemovalOutcome.Deactivated;
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return CatalogRemovalOutcome.Deleted;
    }

    public async Task<IReadOnlyList<Brand>> SearchBrandsAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.Brands.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(brand => brand.Name.Contains(term));
        }

        return await query.OrderBy(brand => brand.Name).ToListAsync(cancellationToken);
    }

    public Task<Brand?> GetBrandAsync(int id, CancellationToken cancellationToken = default) =>
        db.Brands.AsNoTracking().SingleOrDefaultAsync(brand => brand.Id == id, cancellationToken);

    public async Task<int> SaveBrandAsync(BrandInput input, CancellationToken cancellationToken = default)
    {
        var name = RequireText(input.Name, nameof(input.Name), 120);
        var description = OptionalText(input.Description, nameof(input.Description), 1000);
        var logoUrl = ValidateLogoUrl(input.LogoUrl);

        Brand? brand = null;
        if (input.Id is int brandId)
        {
            brand = await db.Brands.SingleOrDefaultAsync(x => x.Id == brandId, cancellationToken)
                ?? throw new CatalogValidationException(nameof(input.Id), "Không tìm thấy thương hiệu.");
        }

        var duplicateQuery = db.Brands.AsNoTracking().Where(x => x.Name == name);
        if (input.Id is int existingBrandId)
        {
            duplicateQuery = duplicateQuery.Where(x => x.Id != existingBrandId);
        }

        var duplicate = await duplicateQuery.AnyAsync(cancellationToken);
        if (duplicate)
        {
            throw new CatalogValidationException(nameof(input.Name), "Tên thương hiệu đã tồn tại.");
        }

        if (brand is null)
        {
            brand = new Brand
            {
                Name = name,
                Description = description,
                LogoUrl = logoUrl,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Brands.Add(brand);
        }
        else
        {
            brand.Name = name;
            brand.Description = description;
            brand.LogoUrl = logoUrl;
            brand.UpdatedAt = DateTime.UtcNow;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            throw new CatalogValidationException(nameof(input.Name), "Tên thương hiệu đã tồn tại.");
        }

        return brand.Id;
    }

    public async Task<CatalogRemovalOutcome> RemoveOrDeactivateBrandAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var brand = await db.Brands.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (brand is null)
        {
            return CatalogRemovalOutcome.NotFound;
        }

        if (await db.Products.AnyAsync(x => x.BrandId == id, cancellationToken))
        {
            brand.IsActive = false;
            brand.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return CatalogRemovalOutcome.Deactivated;
        }

        db.Brands.Remove(brand);
        await db.SaveChangesAsync(cancellationToken);
        return CatalogRemovalOutcome.Deleted;
    }

    private static string RequireText(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new CatalogValidationException(field, "Trường này là bắt buộc.");
        }

        if (normalized.Length > maxLength)
        {
            throw new CatalogValidationException(field, $"Không được vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string? OptionalText(string? value, string field, int maxLength)
    {
        var normalized = value?.Trim();
        if (normalized is { Length: 0 })
        {
            return null;
        }

        if (normalized?.Length > maxLength)
        {
            throw new CatalogValidationException(field, $"Không được vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string? ValidateLogoUrl(string? value)
    {
        var url = OptionalText(value, nameof(Brand.LogoUrl), 2048);
        if (url is null)
        {
            return null;
        }

        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.Contains("..", StringComparison.Ordinal))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            return url;
        }

        throw new CatalogValidationException(nameof(Brand.LogoUrl), "Logo chỉ được dùng đường dẫn nội bộ hoặc URL HTTPS.");
    }
}
