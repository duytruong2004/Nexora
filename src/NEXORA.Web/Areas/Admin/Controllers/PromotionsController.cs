using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class PromotionsController(IAdminPromotionManagementService promotions) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(new PromotionListViewModel { Items = await promotions.GetPromotionsAsync(cancellationToken) });

    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View("Form", new PromotionFormViewModel
        {
            StartDate = DateTime.Now,
            EndDate = DateTime.Now.AddDays(7),
            Products = await promotions.GetProductsAsync(cancellationToken)
        });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PromotionFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return await RenderFormAsync(model, cancellationToken);
        try
        {
            await promotions.SaveAsync(ToInput(model), cancellationToken);
            TempData["Success"] = "Đã tạo chương trình khuyến mại.";
            return RedirectToAction(nameof(Index));
        }
        catch (PromotionValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            return await RenderFormAsync(model, cancellationToken);
        }
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var promotion = await promotions.GetPromotionAsync(id, cancellationToken);
        if (promotion is null) return NotFound();
        var products = await promotions.GetProductsAsync(cancellationToken);
        return View("Form", PromotionFormViewModel.From(promotion, products));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PromotionFormViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.Id) return BadRequest();
        if (!ModelState.IsValid) return await RenderFormAsync(model, cancellationToken);
        try
        {
            await promotions.SaveAsync(ToInput(model), cancellationToken);
            TempData["Success"] = "Đã cập nhật chương trình khuyến mại.";
            return RedirectToAction(nameof(Index));
        }
        catch (PromotionValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            return await RenderFormAsync(model, cancellationToken);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        var found = await promotions.SetActiveAsync(id, isActive, cancellationToken);
        TempData[found ? "Success" : "Error"] = found
            ? (isActive ? "Đã bật chương trình khuyến mại." : "Đã tắt chương trình khuyến mại.")
            : "Không tìm thấy chương trình khuyến mại.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> RenderFormAsync(PromotionFormViewModel model, CancellationToken cancellationToken)
    {
        model.Products = await promotions.GetProductsAsync(cancellationToken);
        return View("Form", model);
    }

    private static PromotionAdminForm ToInput(PromotionFormViewModel model) => new(
        model.Id,
        model.Name,
        model.Description,
        model.DiscountType,
        model.DiscountValue,
        model.StartDate,
        model.EndDate,
        model.IsActive,
        model.SelectedProductIds);
}
