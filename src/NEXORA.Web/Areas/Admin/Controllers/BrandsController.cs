using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class BrandsController(ICategoryBrandManagementService catalog) : Controller
{
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var brands = await catalog.SearchBrandsAsync(search, cancellationToken);
        return View(new BrandListViewModel
        {
            Search = search,
            Items = brands.Select(x => new BrandListRow(x.Id, x.Name, x.Description, x.IsActive)).ToList()
        });
    }

    public IActionResult Create() => View(new BrandFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BrandFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            await catalog.SaveBrandAsync(new BrandInput(null, model.Name, model.Description, model.LogoUrl), cancellationToken);
            TempData["Success"] = "Đã tạo thương hiệu.";
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
        var brand = await catalog.GetBrandAsync(id, cancellationToken);
        return brand is null
            ? NotFound()
            : View(new BrandFormViewModel
            {
                Id = brand.Id,
                Name = brand.Name,
                Description = brand.Description,
                LogoUrl = brand.LogoUrl
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, BrandFormViewModel model, CancellationToken cancellationToken)
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
            await catalog.SaveBrandAsync(new BrandInput(id, model.Name, model.Description, model.LogoUrl), cancellationToken);
            TempData["Success"] = "Đã cập nhật thương hiệu.";
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
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken)
    {
        var outcome = await catalog.RemoveOrDeactivateBrandAsync(id, cancellationToken);
        TempData["Success"] = outcome switch
        {
            CatalogRemovalOutcome.Deleted => "Đã xóa thương hiệu chưa có sản phẩm.",
            CatalogRemovalOutcome.Deactivated => "Thương hiệu có sản phẩm nên đã được ngừng sử dụng.",
            _ => "Thương hiệu không còn tồn tại."
        };
        return RedirectToAction(nameof(Index));
    }
}
