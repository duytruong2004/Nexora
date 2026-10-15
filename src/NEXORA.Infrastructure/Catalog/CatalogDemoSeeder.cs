using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Catalog;

/// <summary>Additive Development-only catalog seed. Existing matching rows are never overwritten or deleted.</summary>
public sealed class CatalogDemoSeeder(AppDbContext db, ILogger<CatalogDemoSeeder> logger) : ICatalogDemoSeeder
{
    private const string DemoDisclaimer = "Dữ liệu mô phỏng do NEXORA tạo để kiểm thử; không đại diện thông số sản phẩm của nhà sản xuất.";

    private static readonly string[] CategoryNames =
    [
        "Điện thoại", "Laptop", "Máy tính bảng", "Màn hình", "Bàn phím",
        "Chuột", "Tai nghe", "Đồng hồ thông minh", "Máy ảnh", "Phụ kiện công nghệ"
    ];

    private static readonly DemoSpecification[] Specifications =
    [
        new("CPU", SpecificationDataTypes.Enumeration, null),
        new("RAM", SpecificationDataTypes.Number, "GB"),
        new("SSD", SpecificationDataTypes.Number, "GB"),
        new("GPU", SpecificationDataTypes.Enumeration, null),
        new("Trọng lượng", SpecificationDataTypes.Number, "kg"),
        new("Kích thước màn hình", SpecificationDataTypes.Number, "inch"),
        new("Chipset", SpecificationDataTypes.Enumeration, null),
        new("Bộ nhớ lưu trữ", SpecificationDataTypes.Number, "GB"),
        new("Dung lượng pin", SpecificationDataTypes.Number, "mAh"),
        new("Chuẩn kết nối", SpecificationDataTypes.Enumeration, null),
        new("Công suất", SpecificationDataTypes.Number, "W")
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CategorySpecifications =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["Laptop"] = ["CPU", "RAM", "SSD", "GPU", "Trọng lượng", "Kích thước màn hình"],
            ["Điện thoại"] = ["Chipset", "RAM", "Bộ nhớ lưu trữ", "Kích thước màn hình", "Dung lượng pin"],
            ["Máy tính bảng"] = ["Chipset", "RAM", "Bộ nhớ lưu trữ", "Kích thước màn hình", "Dung lượng pin"],
            ["Phụ kiện công nghệ"] = ["Chuẩn kết nối", "Công suất"]
        };

    private static readonly DemoProduct[] Products =
    [
        new("Laptop", "NEXORA Demo Laptop Alpha", "NXD-LAP-001", "NEXORA Demo", 16_990_000m,
            Values(("CPU", "Demo CPU A"), ("RAM", "8"), ("SSD", "512"), ("GPU", "Demo GPU Integrated"), ("Trọng lượng", "1.45"), ("Kích thước màn hình", "14"))),
        new("Laptop", "NEXORA Demo Laptop Beta", "NXD-LAP-002", "NEXORA Lab", 23_490_000m,
            Values(("CPU", "Demo CPU B"), ("RAM", "16"), ("SSD", "512"), ("GPU", "Demo GPU Integrated"), ("Trọng lượng", "1.62"), ("Kích thước màn hình", "15.6"))),
        new("Laptop", "NEXORA Demo Laptop Gamma", "NXD-LAP-003", "NEXORA Demo", 31_990_000m,
            Values(("CPU", "Demo CPU C"), ("RAM", "32"), ("SSD", "1024"), ("GPU", "Demo GPU Dedicated"), ("Trọng lượng", "2.05"), ("Kích thước màn hình", "16"))),
        new("Điện thoại", "NEXORA Demo Phone Alpha", "NXD-PHN-001", "NEXORA Demo", 6_990_000m,
            Values(("Chipset", "Demo Chipset A"), ("RAM", "8"), ("Bộ nhớ lưu trữ", "128"), ("Kích thước màn hình", "6.2"), ("Dung lượng pin", "4500"))),
        new("Điện thoại", "NEXORA Demo Phone Beta", "NXD-PHN-002", "NEXORA Lab", 10_990_000m,
            Values(("Chipset", "Demo Chipset B"), ("RAM", "12"), ("Bộ nhớ lưu trữ", "256"), ("Kích thước màn hình", "6.7"), ("Dung lượng pin", "5000"))),
        new("Điện thoại", "NEXORA Demo Phone Gamma", "NXD-PHN-003", "NEXORA Demo", 14_990_000m,
            Values(("Chipset", "Demo Chipset C"), ("RAM", "16"), ("Bộ nhớ lưu trữ", "512"), ("Kích thước màn hình", "6.8"), ("Dung lượng pin", "5500"))),
        new("Máy tính bảng", "NEXORA Demo Tablet Alpha", "NXD-TAB-001", "NEXORA Lab", 5_990_000m,
            Values(("Chipset", "Demo Tablet Chip A"), ("RAM", "6"), ("Bộ nhớ lưu trữ", "128"), ("Kích thước màn hình", "10.1"), ("Dung lượng pin", "7000"))),
        new("Máy tính bảng", "NEXORA Demo Tablet Beta", "NXD-TAB-002", "NEXORA Demo", 9_990_000m,
            Values(("Chipset", "Demo Tablet Chip B"), ("RAM", "8"), ("Bộ nhớ lưu trữ", "256"), ("Kích thước màn hình", "11"), ("Dung lượng pin", "8500"))),
        new("Máy tính bảng", "NEXORA Demo Tablet Gamma", "NXD-TAB-003", "NEXORA Lab", 12_990_000m,
            Values(("Chipset", "Demo Tablet Chip C"), ("RAM", "12"), ("Bộ nhớ lưu trữ", "512"), ("Kích thước màn hình", "12.4"), ("Dung lượng pin", "10000"))),
        new("Phụ kiện công nghệ", "NEXORA Demo USB-C Adapter Alpha", "NXD-ACC-001", "NEXORA Demo", 490_000m,
            Values(("Chuẩn kết nối", "USB-C"), ("Công suất", "30"))),
        new("Phụ kiện công nghệ", "NEXORA Demo USB-C Adapter Beta", "NXD-ACC-002", "NEXORA Lab", 790_000m,
            Values(("Chuẩn kết nối", "USB-C"), ("Công suất", "65")))
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var insertedCategories = 0;
        var insertedBrands = 0;
        var insertedSpecifications = 0;
        var insertedAssignments = 0;
        var insertedProducts = 0;
        var insertedValues = 0;
        var availableDemoProductCount = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
            insertedCategories = await EnsureCategoriesAsync(cancellationToken);
            insertedBrands = await EnsureBrandsAsync(cancellationToken);
            var (specificationsByKey, specificationCount) = await EnsureSpecificationsAsync(cancellationToken);
            insertedSpecifications = specificationCount;
            insertedAssignments = await EnsureCategoryAssignmentsAsync(specificationsByKey, cancellationToken);
            var (demoProducts, productCount) = await EnsureProductsAsync(cancellationToken);
            insertedProducts = productCount;
            insertedValues = await EnsureProductSpecificationsAsync(demoProducts, specificationsByKey, cancellationToken);
            availableDemoProductCount = demoProducts.Count;
            await transaction.CommitAsync(cancellationToken);
        });

        logger.LogInformation(
            "Development catalog seed checked. Added {CategoryCount} categories, {BrandCount} brands, {SpecificationCount} specifications, {AssignmentCount} category assignments, {ProductCount} products, and {ValueCount} product specification values; {AvailableDemoProductCount} demo products are available.",
            insertedCategories, insertedBrands, insertedSpecifications, insertedAssignments,
            insertedProducts, insertedValues, availableDemoProductCount);
    }

    private async Task<int> EnsureCategoriesAsync(CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var name in CategoryNames)
        {
            var exists = await db.Categories.AnyAsync(category => category.ParentId == null && category.Name == name, cancellationToken);
            if (exists)
            {
                continue;
            }

            db.Categories.Add(new Category
            {
                Name = name,
                Description = "Nhóm danh mục thuộc phạm vi khóa luận NEXORA.",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private async Task<int> EnsureBrandsAsync(CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var name in new[] { "NEXORA Demo", "NEXORA Lab" })
        {
            var exists = await db.Brands.AnyAsync(brand => brand.Name == name, cancellationToken);
            if (exists)
            {
                continue;
            }

            db.Brands.Add(new Brand
            {
                Name = name,
                Description = "Thương hiệu mô phỏng, chỉ dùng cho dữ liệu kiểm thử NEXORA.",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private async Task<(Dictionary<string, Specification> Specifications, int Added)> EnsureSpecificationsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await db.Specifications.ToListAsync(cancellationToken);
        var result = new Dictionary<string, Specification>(StringComparer.Ordinal);
        var inserted = 0;
        foreach (var blueprint in Specifications)
        {
            var byName = existing.FirstOrDefault(item => string.Equals(item.Name, blueprint.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null && byName.DataType != blueprint.DataType)
            {
                logger.LogWarning(
                    "Skipped demo Specification {SpecificationName} because an existing definition uses another DataType.",
                    blueprint.Name);
                continue;
            }

            if (byName is { IsActive: false })
            {
                logger.LogWarning("Skipped demo Specification {SpecificationName} because it is inactive.", blueprint.Name);
                continue;
            }

            var specification = byName ?? new Specification
            {
                Name = blueprint.Name,
                Unit = blueprint.Unit,
                DataType = blueprint.DataType,
                IsActive = true
            };
            if (byName is null)
            {
                db.Specifications.Add(specification);
                existing.Add(specification);
                inserted++;
            }

            result[blueprint.Name] = specification;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (result, inserted);
    }

    private async Task<int> EnsureCategoryAssignmentsAsync(
        IReadOnlyDictionary<string, Specification> specificationsByKey,
        CancellationToken cancellationToken)
    {
        var categories = (await db.Categories.Where(category => category.ParentId == null && category.IsActive)
                .ToListAsync(cancellationToken))
            .Where(category => CategorySpecifications.ContainsKey(category.Name))
            .ToDictionary(category => category.Name, StringComparer.Ordinal);
        var inserted = 0;
        foreach (var (categoryName, specificationNames) in CategorySpecifications)
        {
            if (!categories.TryGetValue(categoryName, out var category))
            {
                continue;
            }

            foreach (var (specificationName, sortOrder) in specificationNames.Select((name, index) => (name, index)))
            {
                if (!specificationsByKey.TryGetValue(specificationName, out var specification))
                {
                    continue;
                }

                var exists = await db.CategorySpecifications.AnyAsync(
                    assignment => assignment.CategoryId == category.Id && assignment.SpecificationId == specification.Id,
                    cancellationToken);
                if (exists)
                {
                    continue;
                }

                db.CategorySpecifications.Add(new CategorySpecification
                {
                    CategoryId = category.Id,
                    SpecificationId = specification.Id,
                    IsFilterable = true,
                    IsRequired = false,
                    SortOrder = sortOrder
                });
                inserted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private async Task<(Dictionary<string, Product> Products, int Added)> EnsureProductsAsync(
        CancellationToken cancellationToken)
    {
        var productCategoryNames = Products.Select(product => product.CategoryName).Distinct().ToArray();
        var categories = await db.Categories.Where(category =>
                category.ParentId == null && category.IsActive && productCategoryNames.Contains(category.Name))
            .ToDictionaryAsync(category => category.Name, cancellationToken);
        var brands = await db.Brands.Where(brand => brand.Name == "NEXORA Demo" || brand.Name == "NEXORA Lab")
            .Where(brand => brand.IsActive)
            .ToDictionaryAsync(brand => brand.Name, cancellationToken);
        var results = new Dictionary<string, Product>(StringComparer.Ordinal);
        var inserted = 0;
        foreach (var blueprint in Products)
        {
            if (!categories.TryGetValue(blueprint.CategoryName, out var category) ||
                !brands.TryGetValue(blueprint.BrandName, out var brand))
            {
                continue;
            }

            var existing = await db.Products.SingleOrDefaultAsync(product => product.Sku == blueprint.Sku, cancellationToken);
            if (existing is not null)
            {
                if (existing.Name == blueprint.Name && existing.CategoryId == category.Id && existing.BrandId == brand.Id &&
                    existing.Description?.StartsWith(DemoDisclaimer, StringComparison.Ordinal) == true)
                {
                    results[blueprint.Sku] = existing;
                }
                else
                {
                    logger.LogWarning("Skipped demo product SKU {Sku} because the code already belongs to another product.", blueprint.Sku);
                }

                continue;
            }

            var slug = $"nexora-demo-{blueprint.Sku.ToLowerInvariant()}";
            if (await db.Products.AnyAsync(product => product.Slug == slug, cancellationToken))
            {
                logger.LogWarning("Skipped demo product SKU {Sku} because its generated URL is already in use.", blueprint.Sku);
                continue;
            }

            var now = DateTime.UtcNow;
            var product = new Product
            {
                CategoryId = category.Id,
                BrandId = brand.Id,
                Name = blueprint.Name,
                Slug = slug,
                Sku = blueprint.Sku,
                Description = $"{DemoDisclaimer} Không phải dữ liệu thương mại hoặc cam kết từ nhà sản xuất.",
                Price = blueprint.Price,
                WarrantyMonths = 12,
                Status = ProductStatuses.Active,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Products.Add(product);
            results[blueprint.Sku] = product;
            inserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (results, inserted);
    }

    private async Task<int> EnsureProductSpecificationsAsync(
        IReadOnlyDictionary<string, Product> products,
        IReadOnlyDictionary<string, Specification> specifications,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        foreach (var blueprint in Products)
        {
            if (!products.TryGetValue(blueprint.Sku, out var product))
            {
                continue;
            }

            foreach (var (specificationName, rawValue) in blueprint.Values)
            {
                if (!specifications.TryGetValue(specificationName, out var specification))
                {
                    continue;
                }

                var exists = await db.ProductSpecifications.AnyAsync(
                    value => value.ProductId == product.Id && value.SpecificationId == specification.Id,
                    cancellationToken);
                if (exists)
                {
                    continue;
                }

                var error = SpecificationValuePolicy.TryNormalize(
                    specification.DataType, rawValue, null, specification.Unit, out var normalized);
                if (error is not null || normalized is null)
                {
                    logger.LogWarning(
                        "Skipped invalid demo value for product {ProductId} and Specification {SpecificationId}.",
                        product.Id,
                        specification.Id);
                    continue;
                }

                db.ProductSpecifications.Add(new ProductSpecification
                {
                    ProductId = product.Id,
                    SpecificationId = specification.Id,
                    Value = normalized.Value,
                    NumericValue = normalized.NumericValue,
                    DisplayValue = normalized.DisplayValue
                });
                inserted++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return inserted;
    }

    private static IReadOnlyDictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(item => item.Name, item => item.Value, StringComparer.Ordinal);

    private sealed record DemoSpecification(string Name, string DataType, string? Unit);

    private sealed record DemoProduct(
        string CategoryName,
        string Name,
        string Sku,
        string BrandName,
        decimal Price,
        IReadOnlyDictionary<string, string> Values);
}
