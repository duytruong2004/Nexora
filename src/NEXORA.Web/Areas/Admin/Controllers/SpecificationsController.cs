using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class SpecificationsController(
    ISpecificationManagementService specifications) : Controller
{
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        return View(new SpecificationListViewModel
        {
            Search = search,
            Items = await specifications.SearchAsync(search, cancellationToken)
        });
    }

    public IActionResult Create() => View(new SpecificationFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SpecificationFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await specifications.SaveAsync(new SpecificationInput(null, model.Name, model.Unit, model.DataType), cancellationToken);
            TempData["Success"] = "Đã tạo thông số kỹ thuật.";
            return RedirectToAction(nameof(Index));
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var specification = await specifications.GetAsync(id, cancellationToken);
        return specification is null
            ? NotFound()
            : View(new SpecificationFormViewModel
            {
                Id = specification.Id,
                Name = specification.Name,
                Unit = specification.Unit,
                DataType = specification.DataType
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SpecificationFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await specifications.SaveAsync(new SpecificationInput(id, model.Name, model.Unit, model.DataType), cancellationToken);
            TempData["Success"] = "Đã cập nhật thông số kỹ thuật.";
            return RedirectToAction(nameof(Index));
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        TempData["Success"] = await specifications.DeactivateAsync(id, cancellationToken)
            ? "Thông số kỹ thuật đã ngừng sử dụng. Dữ liệu và liên kết hiện tại được giữ nguyên."
            : "Không tìm thấy thông số kỹ thuật.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ConfigureCategory(int categoryId, CancellationToken cancellationToken)
    {
        var config = await specifications.GetCategoryConfigurationAsync(categoryId, cancellationToken);
        return config is null ? NotFound() : View(ToViewModel(config));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCategorySpecification(
        int categoryId,
        int specificationId,
        bool isFilterable,
        bool isRequired,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        try
        {
            await specifications.SaveCategorySpecificationAsync(
                new CategorySpecificationInput(categoryId, specificationId, isFilterable, isRequired, sortOrder),
                cancellationToken);
            TempData["Success"] = "Đã lưu cấu hình thông số cho danh mục.";
        }
        catch (CatalogValidationException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(ConfigureCategory), new { categoryId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCategorySpecification(
        int categoryId,
        int specificationId,
        CancellationToken cancellationToken)
    {
        try
        {
            TempData["Success"] = await specifications.RemoveCategorySpecificationAsync(
                categoryId, specificationId, cancellationToken)
                ? "Đã gỡ thông số kỹ thuật khỏi danh mục."
                : "Thông số kỹ thuật không còn được gán cho danh mục.";
        }
        catch (CatalogValidationException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(ConfigureCategory), new { categoryId });
    }

    private static CategorySpecificationConfigurationViewModel ToViewModel(CategorySpecificationConfigurationResult config) => new()
    {
        CategoryId = config.CategoryId,
        CategoryName = config.CategoryName,
        Assigned = config.AssignedSpecifications,
        Available = config.AvailableSpecifications
    };
}
