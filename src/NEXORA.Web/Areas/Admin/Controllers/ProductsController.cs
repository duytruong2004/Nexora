using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Entities;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;
using NEXORA.Web.Images;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ProductsController(
    IProductManagementService products,
    IProductImageManagementService images,
    ICategoryBrandManagementService lookups,
    ILogger<ProductsController> logger) : Controller
{
    public async Task<IActionResult> Index(
        string? search,
        int? categoryId,
        int? brandId,
        string? status,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var filter = new ProductAdminFilter(search, categoryId, brandId, status, page);
        var result = await products.SearchAsync(filter, cancellationToken);
        var categories = await lookups.SearchCategoriesAsync(null, cancellationToken);
        var brands = await lookups.SearchBrandsAsync(null, cancellationToken);
        return View(new ProductListViewModel
        {
            Page = result,
            Filter = filter with { Page = result.Page, PageSize = result.PageSize },
            CategoryOptions = categories.Select(item => new SelectListItem(
                item.IsActive ? item.Name : $"{item.Name} (đã ngừng)", item.Id.ToString(), item.Id == categoryId)).ToList(),
            BrandOptions = brands.Select(item => new SelectListItem(
                item.IsActive ? item.Name : $"{item.Name} (đã ngừng)", item.Id.ToString(), item.Id == brandId)).ToList()
        });
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new ProductFormViewModel();
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(48 * 1024 * 1024)]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken cancellationToken)
    {
        await using var uploads = await ReadAndValidateUploadsAsync(model, cancellationToken);
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            var productId = await products.SaveAsync(ToInput(model), cancellationToken);
            if (uploads.Primary is not null || uploads.Details.Count > 0)
            {
                try
                {
                    await images.SaveImagesAsync(productId, uploads.Primary, uploads.Details, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Product {ProductId} was saved, but its uploaded images could not be committed.", productId);
                    TempData["Error"] = "Sản phẩm đã lưu nhưng chưa gắn được ảnh. Vui lòng tải ảnh lại ở trang chỉnh sửa.";
                    return RedirectToAction(nameof(Edit), new { id = productId });
                }
            }

            TempData["Success"] = "Đã tạo sản phẩm.";
            return RedirectToAction(nameof(Details), new { id = productId });
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
        }

        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var model = new ProductFormViewModel
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            BrandId = product.BrandId,
            Name = product.Name,
            Sku = product.Sku,
            Description = product.Description,
            Price = product.Price,
            DiscountPrice = product.DiscountPrice,
            WarrantyMonths = product.WarrantyMonths,
            Status = product.Status,
            ExistingProduct = product
        };
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(48 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        await using var uploads = await ReadAndValidateUploadsAsync(model, cancellationToken);
        model.ExistingProduct = await products.GetAsync(id, cancellationToken);
        if (model.ExistingProduct is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(model, cancellationToken);
            return View(model);
        }

        try
        {
            await products.SaveAsync(ToInput(model, id), cancellationToken);
            if (uploads.Primary is not null || uploads.Details.Count > 0)
            {
                try
                {
                    await images.SaveImagesAsync(id, uploads.Primary, uploads.Details, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Product {ProductId} was updated, but its uploaded images could not be committed.", id);
                    TempData["Error"] = "Thông tin sản phẩm đã lưu nhưng chưa gắn được ảnh. Vui lòng thử tải ảnh lại.";
                    return RedirectToAction(nameof(Edit), new { id });
                }
            }

            TempData["Success"] = "Đã cập nhật sản phẩm.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
        }

        model.ExistingProduct = await products.GetAsync(id, cancellationToken);
        await PopulateOptionsAsync(model, cancellationToken);
        return View(model);
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await products.GetAsync(id, cancellationToken);
        return product is null ? NotFound() : View(new ProductDetailsViewModel { Product = product });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        TempData["Success"] = await products.DeactivateAsync(id, cancellationToken)
            ? "Sản phẩm đã được ngừng kinh doanh. Dữ liệu liên quan được giữ nguyên."
            : "Không tìm thấy sản phẩm.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteImage(int id, int imageId, CancellationToken cancellationToken)
    {
        TempData["Success"] = await images.DeleteImageAsync(id, imageId, cancellationToken)
            ? "Đã xóa ảnh sản phẩm."
            : "Không tìm thấy ảnh cần xóa.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task PopulateOptionsAsync(ProductFormViewModel model, CancellationToken cancellationToken)
    {
        var categories = await lookups.SearchCategoriesAsync(null, cancellationToken);
        var brands = await lookups.SearchBrandsAsync(null, cancellationToken);
        model.CategoryOptions = categories
            .Where(item => item.IsActive || item.Id == model.CategoryId)
            .Select(item => new SelectListItem(item.Name, item.Id.ToString(), item.Id == model.CategoryId))
            .ToList();
        model.BrandOptions = brands
            .Where(item => item.IsActive || item.Id == model.BrandId)
            .Select(item => new SelectListItem(item.Name, item.Id.ToString(), item.Id == model.BrandId))
            .ToList();
    }

    private static ProductInput ToInput(ProductFormViewModel model, int? id = null) => new(
        id,
        model.CategoryId,
        model.BrandId,
        model.Name,
        model.Sku,
        model.Description,
        model.Price,
        model.DiscountPrice,
        model.WarrantyMonths,
        model.Status);

    private async Task<ValidatedUploads> ReadAndValidateUploadsAsync(
        ProductFormViewModel model,
        CancellationToken cancellationToken)
    {
        ProductImageFile? primary = null;
        var details = new List<ProductImageFile>();
        if (model.PrimaryImage is { Length: > 0 } primaryFile)
        {
            primary = await ReadAndValidateAsync(primaryFile, nameof(model.PrimaryImage), cancellationToken);
        }

        var detailFiles = model.DetailImages.Where(file => file.Length > 0).ToList();
        if (detailFiles.Count > 8)
        {
            ModelState.AddModelError(nameof(model.DetailImages), "Mỗi lần chỉ được tải tối đa 8 ảnh chi tiết.");
        }

        foreach (var file in detailFiles.Take(8))
        {
            var valid = await ReadAndValidateAsync(file, nameof(model.DetailImages), cancellationToken);
            if (valid is not null)
            {
                details.Add(valid);
            }
        }

        return new ValidatedUploads(primary, details);
    }

    private async Task<ProductImageFile?> ReadAndValidateAsync(
        IFormFile file,
        string modelField,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > ProductImageFileValidation.MaximumFileSizeBytes)
        {
            ModelState.AddModelError(modelField, "Mỗi ảnh phải lớn hơn 0 và không vượt quá 5 MB.");
            return null;
        }

        await using var input = file.OpenReadStream();
        await using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, cancellationToken);
        var bytes = output.ToArray();
        try
        {
            ProductImageFileValidation.ValidateAndGetExtension(bytes, file.FileName);
            return new ProductImageFile(new MemoryStream(bytes, writable: false), file.FileName, bytes.LongLength);
        }
        catch (ProductImageValidationException exception)
        {
            ModelState.AddModelError(modelField, exception.Message);
            return null;
        }
    }

    private sealed record ValidatedUploads(ProductImageFile? Primary, IReadOnlyList<ProductImageFile> Details) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            if (Primary is not null)
            {
                await Primary.Content.DisposeAsync();
            }

            foreach (var detail in Details)
            {
                await detail.Content.DisposeAsync();
            }
        }
    }
}
