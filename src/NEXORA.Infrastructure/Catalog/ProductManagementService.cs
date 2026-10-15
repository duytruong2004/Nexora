using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

public sealed class ProductManagementService(AppDbContext db) : IProductManagementService
{
    private const int MaximumPageSize = 100;

    public async Task<ProductAdminPage> SearchAsync(ProductAdminFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Products.AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.Brand)
            .AsQueryable();

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

        if (filter.Status is ProductStatuses.Active or ProductStatuses.Inactive)
        {
            query = query.Where(product => product.Status == filter.Status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var items = await query
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductAdminRow(
                product.Id,
                product.Name,
                product.Sku,
                product.Category.Name,
                product.Brand.Name,
                product.Price,
                product.DiscountPrice,
                product.Status,
                product.Images.Where(image => image.IsPrimary)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new ProductAdminPage(items, totalCount, page, pageSize);
    }

    public async Task<ProductAdminDetails?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking()
            .Include(item => item.Category)
            .Include(item => item.Brand)
            .Include(item => item.Images)
            .Include(item => item.Specifications)
                .ThenInclude(value => value.Specification)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        return product is null
            ? null
            : new ProductAdminDetails(
                product.Id,
                product.CategoryId,
                product.Category.Name,
                product.BrandId,
                product.Brand.Name,
                product.Name,
                product.Slug,
                product.Sku,
                product.Description,
                product.Price,
                product.DiscountPrice,
                product.WarrantyMonths,
                product.Status,
                product.Images.OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => new ProductImageItem(image.Id, image.ImageUrl, image.IsPrimary, image.SortOrder))
                    .ToList(),
                product.Specifications.OrderBy(value => value.Specification.Name)
                    .Select(value => new ProductAdminSpecificationItem(
                        value.Specification.Name,
                        value.Specification.DataType,
                        value.Specification.Unit,
                        value.Value,
                        value.DisplayValue))
                    .ToList());
    }

    public async Task<int> SaveAsync(ProductInput input, CancellationToken cancellationToken = default)
    {
        if (input.CategoryId <= 0 || input.BrandId <= 0)
        {
            throw new CatalogValidationException(nameof(input.CategoryId), "Vui lòng chọn danh mục và thương hiệu hợp lệ.");
        }

        if (input.Id is <= 0)
        {
            throw new CatalogValidationException(nameof(input.Id), "Mã sản phẩm không hợp lệ.");
        }

        var name = input.Name?.Trim() ?? string.Empty;
        var sku = input.Sku?.Trim() ?? string.Empty;
        var description = input.Description?.Trim();
        if (description is { Length: 0 })
        {
            description = null;
        }

        if (description?.Length > 4000)
        {
            throw new CatalogValidationException(nameof(input.Description), "Mô tả không được vượt quá 4.000 ký tự.");
        }

        var status = input.Status?.Trim().ToUpperInvariant() ?? string.Empty;
        var policyError = ProductCatalogPolicy.Validate(
            name, sku, input.Price, input.DiscountPrice, input.WarrantyMonths, status);
        if (policyError is not null)
        {
            var field = string.IsNullOrWhiteSpace(name) || name.Length > 240
                ? nameof(input.Name)
                : string.IsNullOrWhiteSpace(sku) || sku.Length > 80
                    ? nameof(input.Sku)
                    : input.Price < 0 || input.DiscountPrice is < 0 || input.DiscountPrice > input.Price
                        ? nameof(input.Price)
                        : input.WarrantyMonths < 0
                            ? nameof(input.WarrantyMonths)
                            : nameof(input.Status);
            throw new CatalogValidationException(field, policyError);
        }

        var product = input.Id is int id
            ? await db.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
                ?? throw new CatalogValidationException(nameof(input.Id), "Không tìm thấy sản phẩm.")
            : new Product();

        var categoryIsActive = await db.Categories.AsNoTracking()
            .AnyAsync(category => category.Id == input.CategoryId &&
                                  (category.IsActive || (input.Id.HasValue && product.CategoryId == category.Id)), cancellationToken);
        if (!categoryIsActive)
        {
            throw new CatalogValidationException(nameof(input.CategoryId), "Danh mục không tồn tại hoặc đã ngừng sử dụng.");
        }

        var brandIsActive = await db.Brands.AsNoTracking()
            .AnyAsync(brand => brand.Id == input.BrandId &&
                               (brand.IsActive || (input.Id.HasValue && product.BrandId == brand.Id)), cancellationToken);
        if (!brandIsActive)
        {
            throw new CatalogValidationException(nameof(input.BrandId), "Thương hiệu không tồn tại hoặc đã ngừng sử dụng.");
        }

        if (status == ProductStatuses.Active)
        {
            var isMissingRequiredSpecification = await db.CategorySpecifications.AsNoTracking()
                .Where(assignment => assignment.CategoryId == input.CategoryId &&
                                     assignment.IsRequired && assignment.Specification.IsActive)
                .AnyAsync(assignment => !db.ProductSpecifications.Any(value =>
                    value.ProductId == product.Id && value.SpecificationId == assignment.SpecificationId),
                    cancellationToken);
            if (isMissingRequiredSpecification)
            {
                throw new CatalogValidationException(nameof(input.Status),
                    "Hãy nhập đầy đủ Specification bắt buộc theo danh mục trước khi đưa sản phẩm vào kinh doanh.");
            }
        }

        if (input.Id.HasValue && product.CategoryId != input.CategoryId)
        {
            var wouldDetachExistingSpecification = await db.ProductSpecifications.AnyAsync(value =>
                value.ProductId == product.Id && !db.CategorySpecifications.Any(assignment =>
                    assignment.CategoryId == input.CategoryId && assignment.SpecificationId == value.SpecificationId),
                cancellationToken);
            if (wouldDetachExistingSpecification)
            {
                throw new CatalogValidationException(nameof(input.CategoryId),
                    "Hãy gỡ hoặc gán các thông số hiện có cho danh mục mới trước khi chuyển sản phẩm.");
            }
        }

        var duplicateSku = await db.Products.AsNoTracking()
            .AnyAsync(item => item.Sku == sku && item.Id != product.Id, cancellationToken);
        if (duplicateSku)
        {
            throw new CatalogValidationException(nameof(input.Sku), "Mã sản phẩm đã tồn tại.");
        }

        product.CategoryId = input.CategoryId;
        product.BrandId = input.BrandId;
        product.Name = name;
        product.Sku = sku;
        product.Slug = CreateSlug(name, sku);
        product.Description = description;
        product.Price = input.Price;
        product.DiscountPrice = input.DiscountPrice;
        product.WarrantyMonths = input.WarrantyMonths;
        product.Status = status;
        product.UpdatedAt = DateTime.UtcNow;

        if (input.Id is null)
        {
            product.CreatedAt = product.UpdatedAt;
            db.Products.Add(product);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            throw new CatalogValidationException(nameof(input.Sku), "Mã sản phẩm hoặc địa chỉ sản phẩm đã tồn tại.");
        }

        return product.Id;
    }

    public async Task<bool> DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null)
        {
            return false;
        }

        product.Status = ProductStatuses.Inactive;
        product.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static string CreateSlug(string name, string sku) =>
        $"{ToSlugPart(name)}-{ToSlugPart(sku)}";

    private static string ToSlugPart(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var separatorAdded = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                separatorAdded = false;
            }
            else if (!separatorAdded && builder.Length > 0)
            {
                builder.Append('-');
                separatorAdded = true;
            }
        }

        return builder.ToString().Trim('-');
    }
}
