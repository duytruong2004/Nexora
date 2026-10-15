using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Web.Areas.Admin.Controllers;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Tests;

public sealed class ProductSpecificationEditorControllerTests
{
    [Fact]
    public async Task InvalidSubmission_RehydratesMetadataAndKeepsPostedValues()
    {
        var service = new StubProductSpecificationManagementService();
        var controller = new ProductSpecificationsController(service);
        var submitted = new ProductSpecificationEditorViewModel
        {
            ProductId = 12,
            Values =
            [
                new ProductSpecificationValueViewModel
                {
                    SpecificationId = 44,
                    Value = string.Empty,
                    DisplayValue = "giá trị nhập thử"
                }
            ]
        };

        var result = await controller.Edit(12, submitted, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductSpecificationEditorViewModel>(view.Model);
        Assert.Equal("Sản phẩm kiểm thử", model.ProductName);
        Assert.Equal("Danh mục kiểm thử", model.CategoryName);
        var value = Assert.Single(model.Values);
        Assert.Equal("P2ACCEPT RAM", value.Name);
        Assert.Equal("NUMBER", value.DataType);
        Assert.True(value.IsRequired);
        Assert.Equal(string.Empty, value.Value);
        Assert.Equal("giá trị nhập thử", value.DisplayValue);
        Assert.Contains(controller.ModelState[string.Empty]!.Errors, error =>
            error.ErrorMessage.Contains("bắt buộc", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StubProductSpecificationManagementService : IProductSpecificationManagementService
    {
        public Task<ProductSpecificationEditor?> GetEditorAsync(int productId, CancellationToken cancellationToken = default)
        {
            ProductSpecificationEditor editor = new(
                12,
                "Sản phẩm kiểm thử",
                "Danh mục kiểm thử",
                [new ProductSpecificationEditorValue(44, "P2ACCEPT RAM", "GB", "NUMBER", true, "8", "8 GB", [])]);
            return Task.FromResult<ProductSpecificationEditor?>(editor);
        }

        public Task SaveValuesAsync(
            int productId,
            IReadOnlyList<ProductSpecificationValueInput> values,
            CancellationToken cancellationToken = default) =>
            throw new CatalogValidationException("", "Thông số P2ACCEPT RAM là bắt buộc.");
    }
}
