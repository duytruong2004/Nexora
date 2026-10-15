using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class CategoriesController(ICategoryBrandManagementService catalog) : Controller
{
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var categories = await catalog.SearchCategoriesAsync(search, cancellationToken);
        return View(new CategoryListViewModel
        {
            Search = search,
            Items = categories.Select(x => new CategoryListRow(x.Id, x.Name, x.Parent?.Name, x.IsActive)).ToList()
        });
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new CategoryFormViewModel();
        await PopulateParentOptionsAsync(model, null, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateParentOptionsAsync(model, null, cancellationToken);
            return View(model);
        }

        try
        {
            await catalog.SaveCategoryAsync(new CategoryInput(null, model.Name, model.Description, model.ParentId), cancellationToken);
            TempData["Success"] = "Đã tạo danh mục.";
            return RedirectToAction(nameof(Index));
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            await PopulateParentOptionsAsync(model, null, cancellationToken);
            return View(model);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var category = await catalog.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var model = new CategoryFormViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ParentId = category.ParentId
        };
        await PopulateParentOptionsAsync(model, id, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            await PopulateParentOptionsAsync(model, id, cancellationToken);
            return View(model);
        }

        try
        {
            await catalog.SaveCategoryAsync(new CategoryInput(id, model.Name, model.Description, model.ParentId), cancellationToken);
            TempData["Success"] = "Đã cập nhật danh mục.";
            return RedirectToAction(nameof(Index));
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            await PopulateParentOptionsAsync(model, id, cancellationToken);
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken)
    {
        var outcome = await catalog.RemoveOrDeactivateCategoryAsync(id, cancellationToken);
        TempData["Success"] = outcome switch
        {
            CatalogRemovalOutcome.Deleted => "Đã xóa danh mục chưa có dữ liệu liên quan.",
            CatalogRemovalOutcome.Deactivated => "Danh mục có sản phẩm hoặc danh mục con nên đã được ngừng sử dụng.",
            _ => "Danh mục không còn tồn tại."
        };
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateParentOptionsAsync(
        CategoryFormViewModel model,
        int? excludedId,
        CancellationToken cancellationToken)
    {
        var categories = await catalog.SearchCategoriesAsync(null, cancellationToken);
        model.ParentOptions = categories
            .Where(x => x.IsActive && x.Id != excludedId)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString(), x.Id == model.ParentId))
            .ToList();
    }
}
