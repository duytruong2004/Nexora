using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Web.Images;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using NEXORA.Infrastructure.Catalog;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Tests;

public sealed class ProductCatalogTests
{
    [Theory]
    [InlineData("Laptop X", "SKU-X", "10000000", "9000000", 12, "ACTIVE", true)]
    [InlineData(" ", "SKU-X", "10000000", null, 12, "ACTIVE", false)]
    [InlineData("Phone X", "SKU-X", "-1", null, 12, "ACTIVE", false)]
    [InlineData("Phone X", "SKU-X", "10000000", "10000001", 12, "ACTIVE", false)]
    [InlineData("Phone X", "SKU-X", "10000000.001", null, 12, "ACTIVE", false)]
    [InlineData("Phone X", "SKU-X", "10000000000000000", null, 12, "ACTIVE", false)]
    [InlineData("Phone X", "SKU-X", "10000000", null, -1, "ACTIVE", false)]
    [InlineData("Phone X", "SKU-X", "10000000", null, 12, "DELETED", false)]
    public void ProductCatalogPolicy_ValidatesProductFields(
        string name,
        string sku,
        string price,
        string? discountPrice,
        int warrantyMonths,
        string status,
        bool expectedValid)
    {
        var parsedPrice = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);
        var parsedDiscount = discountPrice is null
            ? (decimal?)null
            : decimal.Parse(discountPrice, System.Globalization.CultureInfo.InvariantCulture);
        var error = ProductCatalogPolicy.Validate(name, sku, parsedPrice, parsedDiscount, warrantyMonths, status);

        Assert.Equal(expectedValid, error is null);
    }

    [Fact]
    public void ProductImageValidator_AcceptsJpegBySignatureAndMatchingExtension()
    {
        var extension = ProductImageFileValidation.ValidateAndGetExtension(
            [0xFF, 0xD8, 0xFF, 0xE0], "customer-name.jpeg");

        Assert.Equal(".jpg", extension);
    }

    [Fact]
    public void ProductImageValidator_RejectsExtensionThatDoesNotMatchContent()
    {
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        Assert.Throws<ProductImageValidationException>(() =>
            ProductImageFileValidation.ValidateAndGetExtension(pngHeader, "photo.jpg"));
    }

    [Fact]
    public void ProductImageValidator_RejectsUnsupportedActiveContent()
    {
        Assert.Throws<ProductImageValidationException>(() =>
            ProductImageFileValidation.ValidateAndGetExtension("<svg/>"u8, "logo.svg"));
    }

    [Fact]
    public void ProductAdminPage_ComputesServerPagedCount()
    {
        var page = new ProductAdminPage([], 41, 1, 20);

        Assert.Equal(3, page.PageCount);
    }

    [Fact]
    public async Task ProductImageStorage_UsesGeneratedRelativeNameAndDeletesOnlyManagedFile()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"nexora-image-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var storage = new LocalProductImageStorage(
                new TestWebHostEnvironment(temporaryRoot),
                NullLogger<LocalProductImageStorage>.Instance);
            var content = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            var url = await storage.SaveAsync(new ProductImageFile(
                new MemoryStream(content), "user supplied name.png", content.Length));
            var path = Path.Combine(temporaryRoot, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            Assert.StartsWith("/uploads/products/", url);
            Assert.DoesNotContain("user supplied name", url, StringComparison.Ordinal);
            Assert.True(File.Exists(path));

            await storage.DeleteAsync(url);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void NumberSpecification_StoresInvariantNumericValueAndSeparateDisplayValue()
    {
        var error = SpecificationValuePolicy.TryNormalize("NUMBER", "16.50", null, "GB", out var result);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("16.5", result.Value);
        Assert.Equal(16.5m, result.NumericValue);
        Assert.Equal("16.5 GB", result.DisplayValue);
    }

    [Theory]
    [InlineData("16,5")]
    [InlineData("1.12345")]
    public void NumberSpecification_RejectsLocaleDependentOrUnstorableValues(string value)
    {
        var error = SpecificationValuePolicy.TryNormalize("NUMBER", value, null, null, out var result);

        Assert.NotNull(error);
        Assert.Null(result);
    }

    [Fact]
    public void EnumSpecification_RemainsOpenToNewDynamicValues()
    {
        var error = SpecificationValuePolicy.TryNormalize("ENUM", "Wi-Fi 7", null, null, out var result);

        Assert.Null(error);
        Assert.Equal("Wi-Fi 7", result?.Value);
        Assert.Null(result?.NumericValue);
    }

    [Fact]
    public void BooleanSpecification_StoresCanonicalValueAndVietnameseDisplay()
    {
        var error = SpecificationValuePolicy.TryNormalize("BOOLEAN", "true", null, null, out var result);

        Assert.Null(error);
        Assert.Equal("true", result?.Value);
        Assert.Equal("Có", result?.DisplayValue);
    }

    [Fact]
    public void SpecificationAssignmentPolicy_RejectsValuesOutsideCurrentCategoryAndDuplicates()
    {
        Assert.NotNull(SpecificationAssignmentPolicy.ValidateSubmittedIds([4, 9], [4, 12]));
        Assert.NotNull(SpecificationAssignmentPolicy.ValidateSubmittedIds([4, 9], [4, 4]));
        Assert.Null(SpecificationAssignmentPolicy.ValidateSubmittedIds([4, 9], [4, 9]));
    }

    [Fact]
    public void ProductComparisonPolicy_EnforcesSelectionLimitCategoryAndMissingValueDifference()
    {
        Assert.NotNull(ProductComparisonPolicy.ValidateSelection([1]));
        Assert.Null(ProductComparisonPolicy.ValidateSelection([1, 2]));
        Assert.Null(ProductComparisonPolicy.ValidateSelection([1, 2, 3]));
        Assert.NotNull(ProductComparisonPolicy.ValidateSelection([1, 2, 3, 4]));
        Assert.NotNull(ProductComparisonPolicy.ValidateSelection([1, 1]));
        Assert.Null(ProductComparisonPolicy.ValidateCategoryCompatibility([8, 8, 8]));
        Assert.NotNull(ProductComparisonPolicy.ValidateCategoryCompatibility([8, 9]));
        Assert.True(ProductComparisonPolicy.AreSpecificationValuesDifferent(["16 GB", "32 GB"]));
        Assert.True(ProductComparisonPolicy.AreSpecificationValuesDifferent(["16 GB", null]));
        Assert.False(ProductComparisonPolicy.AreSpecificationValuesDifferent(["16 GB", "16 GB"]));
        Assert.False(ProductComparisonPolicy.AreSpecificationValuesDifferent([null, null]));
    }

    [Fact]
    public void CatalogFilter_ComposesCombinedDynamicConditionsIntoServerSideSql()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=localhost;Database=NEXORA_QueryOnly;Integrated Security=True;TrustServerCertificate=True")
            .Options);
        var filter = new CatalogSearchFilter("work", 12, 4, 10_000, 30_000, "price-low", 2, 10)
        {
            SpecificationFilters =
            [
                new CatalogSpecificationFilter { SpecificationId = 21, MinimumValue = 8, MaximumValue = 16 },
                new CatalogSpecificationFilter { SpecificationId = 34, Value = "OLED" },
                new CatalogSpecificationFilter { SpecificationId = 99, Value = "not-assigned" }
            ]
        };
        var typeMap = new Dictionary<int, string>
        {
            [21] = SpecificationDataTypes.Number,
            [34] = SpecificationDataTypes.Enumeration
        };
        var visible = context.Products.AsNoTracking().Where(product =>
            product.Status == ProductStatuses.Active && product.Category.IsActive && product.Brand.IsActive);
        var sql = CustomerCatalogQueryComposer.ApplySorting(
                CustomerCatalogQueryComposer.ApplyFilters(visible, filter, typeMap), filter.SortBy)
            .Skip(10)
            .Take(10)
            .ToQueryString();

        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("category_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brand_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discount_price", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("numeric_value", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[value]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-assigned", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CatalogPageWindow_ClampsRequestedPageAndSize()
    {
        var firstPage = CatalogPageWindow.Create(0, 500, 100, 48);
        var lastPage = CatalogPageWindow.Create(99, 48, 100, 48);

        Assert.Equal((1, 48, 3, 0), (firstPage.Page, firstPage.PageSize, firstPage.PageCount, firstPage.Skip));
        Assert.Equal((3, 48, 3, 96), (lastPage.Page, lastPage.PageSize, lastPage.PageCount, lastPage.Skip));
    }

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "NEXORA.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
