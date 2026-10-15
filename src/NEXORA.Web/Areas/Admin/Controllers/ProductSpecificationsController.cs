using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ProductSpecificationsController(IProductSpecificationManagementService specifications) : Controller
{
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var editor = await specifications.GetEditorAsync(id, cancellationToken);
        return editor is null ? NotFound() : View(ToViewModel(editor));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductSpecificationEditorViewModel model, CancellationToken cancellationToken)
    {
        if (id != model.ProductId)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return await RenderEditorAsync(id, model, cancellationToken);
        }

        try
        {
            var input = model.Values.Select(value => new ProductSpecificationValueInput(
                value.SpecificationId, value.Value, value.DisplayValue)).ToList();
            await specifications.SaveValuesAsync(id, input, cancellationToken);
            TempData["Success"] = "Đã lưu thông số kỹ thuật theo cấu hình động của danh mục.";
            return RedirectToAction("Details", "Products", new { area = "Admin", id });
        }
        catch (CatalogValidationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return await RenderEditorAsync(id, model, cancellationToken);
        }
    }

    private async Task<IActionResult> RenderEditorAsync(
        int id,
        ProductSpecificationEditorViewModel submitted,
        CancellationToken cancellationToken)
    {
        var editor = await specifications.GetEditorAsync(id, cancellationToken);
        if (editor is null)
        {
            return NotFound();
        }

        var model = ToViewModel(editor);
        var submittedValues = submitted.Values
            .GroupBy(value => value.SpecificationId)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var value in model.Values)
        {
            if (submittedValues.TryGetValue(value.SpecificationId, out var posted))
            {
                value.Value = posted.Value;
                value.DisplayValue = posted.DisplayValue;
            }
        }

        return View("Edit", model);
    }

    private static ProductSpecificationEditorViewModel ToViewModel(ProductSpecificationEditor editor) => new()
    {
        ProductId = editor.ProductId,
        ProductName = editor.ProductName,
        CategoryName = editor.CategoryName,
        Values = editor.Values.Select(value => new ProductSpecificationValueViewModel
        {
            SpecificationId = value.SpecificationId,
            Name = value.Name,
            Unit = value.Unit,
            DataType = value.DataType,
            IsRequired = value.IsRequired,
            EnumOptions = value.EnumOptions,
            Value = value.Value,
            DisplayValue = value.DisplayValue
        }).ToList()
    };
}
