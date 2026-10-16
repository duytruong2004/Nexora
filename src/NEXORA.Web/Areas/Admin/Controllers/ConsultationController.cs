using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NEXORA.Application.Consultation;
using NEXORA.Domain.Entities;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ConsultationController(IConsultationManagementService management) : Controller
{
    public IActionResult Index() => RedirectToAction(nameof(UsageNeeds));

    [HttpGet]
    public async Task<IActionResult> UsageNeeds(CancellationToken cancellationToken) =>
        View(new UsageNeedAdminPageViewModel { Items = await management.GetUsageNeedsAsync(cancellationToken) });

    [HttpGet]
    public IActionResult CreateUsageNeed() => View("UsageNeedForm", new UsageNeedFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateUsageNeed(UsageNeedFormViewModel model, CancellationToken cancellationToken) =>
        SaveUsageNeed(null, model, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> EditUsageNeed(int id, CancellationToken cancellationToken)
    {
        var item = (await management.GetUsageNeedsAsync(cancellationToken)).SingleOrDefault(value => value.Id == id);
        return item is null ? NotFound() : View("UsageNeedForm", new UsageNeedFormViewModel
        {
            Id = item.Id, Code = item.Code, Name = item.Name, Description = item.Description, IsActive = item.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> EditUsageNeed(int id, UsageNeedFormViewModel model, CancellationToken cancellationToken) =>
        id != model.Id ? Task.FromResult<IActionResult>(BadRequest()) : SaveUsageNeed(id, model, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUsageNeed(int id, bool isActive, CancellationToken cancellationToken)
    {
        TempData[await management.SetUsageNeedActiveAsync(id, isActive, cancellationToken) ? "Success" : "Error"] =
            isActive ? "Đã bật nhu cầu tư vấn." : "Đã ngừng nhu cầu và các rule đang hoạt động của nhu cầu đó.";
        return RedirectToAction(nameof(UsageNeeds));
    }

    [HttpGet]
    public async Task<IActionResult> Rules(
        int? usageNeedId,
        int? categoryId,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var allRules = await management.GetRulesAsync(cancellationToken);
        return View(new RecommendationRuleAdminPageViewModel
        {
            UsageNeedId = usageNeedId,
            CategoryId = categoryId,
            IsActive = isActive,
            UsageNeedOptions = allRules
                .GroupBy(item => new { item.UsageNeedId, item.UsageNeedName })
                .OrderBy(group => group.Key.UsageNeedName)
                .Select(group => new SelectListItem(group.Key.UsageNeedName, group.Key.UsageNeedId.ToString(), group.Key.UsageNeedId == usageNeedId))
                .ToArray(),
            CategoryOptions = allRules
                .GroupBy(item => new { item.CategoryId, item.CategoryName })
                .OrderBy(group => group.Key.CategoryName)
                .Select(group => new SelectListItem(group.Key.CategoryName, group.Key.CategoryId.ToString(), group.Key.CategoryId == categoryId))
                .ToArray(),
            Items = allRules
                .Where(item => usageNeedId is null || item.UsageNeedId == usageNeedId)
                .Where(item => categoryId is null || item.CategoryId == categoryId)
                .Where(item => isActive is null || item.IsActive == isActive)
                .ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> CreateRule(CancellationToken cancellationToken) =>
        View("RuleForm", await BuildRuleFormAsync(new RecommendationRuleFormViewModel(), cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> CreateRule(RecommendationRuleFormViewModel model, CancellationToken cancellationToken) =>
        SaveRule(null, model, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> EditRule(int id, CancellationToken cancellationToken)
    {
        var rule = (await management.GetRulesAsync(cancellationToken)).SingleOrDefault(item => item.Id == id);
        if (rule is null) return NotFound();
        var model = new RecommendationRuleFormViewModel
        {
            Id = rule.Id, UsageNeedId = rule.UsageNeedId, CategoryId = rule.CategoryId,
            SpecificationId = rule.SpecificationId, Operator = rule.Operator, ThresholdValue = rule.ThresholdValue,
            Weight = rule.Weight, IsRequired = rule.IsRequired, IsActive = rule.IsActive
        };
        return View("RuleForm", await BuildRuleFormAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> EditRule(int id, RecommendationRuleFormViewModel model, CancellationToken cancellationToken) =>
        id != model.Id ? Task.FromResult<IActionResult>(BadRequest()) : SaveRule(id, model, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRule(int id, bool isActive, CancellationToken cancellationToken)
    {
        try
        {
            TempData[await management.SetRecommendationRuleActiveAsync(id, isActive, cancellationToken) ? "Success" : "Error"] =
                isActive ? "Đã bật rule tư vấn." : "Đã ngừng rule tư vấn.";
        }
        catch (ConsultationValidationException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToAction(nameof(Rules));
    }

    private async Task<IActionResult> SaveUsageNeed(int? id, UsageNeedFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("UsageNeedForm", model);
        try
        {
            await management.SaveUsageNeedAsync(
                new UsageNeedInput(id, model.Code, model.Name, model.Description, model.IsActive), cancellationToken);
            TempData["Success"] = "Đã lưu nhu cầu tư vấn.";
            return RedirectToAction(nameof(UsageNeeds));
        }
        catch (ConsultationValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            return View("UsageNeedForm", model);
        }
    }

    private async Task<IActionResult> SaveRule(int? id, RecommendationRuleFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return View("RuleForm", await BuildRuleFormAsync(model, cancellationToken));
        try
        {
            await management.SaveRuleAsync(new RecommendationRuleInput(
                id, model.UsageNeedId, model.CategoryId, model.SpecificationId, model.Operator,
                model.ThresholdValue, model.Weight, model.IsRequired, model.IsActive), cancellationToken);
            TempData["Success"] = "Đã lưu quy tắc tư vấn.";
            return RedirectToAction(nameof(Rules));
        }
        catch (ConsultationValidationException exception)
        {
            ModelState.AddModelError(exception.Field, exception.Message);
            return View("RuleForm", await BuildRuleFormAsync(model, cancellationToken));
        }
    }

    private async Task<RecommendationRuleFormViewModel> BuildRuleFormAsync(
        RecommendationRuleFormViewModel model,
        CancellationToken cancellationToken)
    {
        var options = await management.GetRuleFormOptionsAsync(cancellationToken);
        model.UsageNeedOptions = options.UsageNeeds
            .Select(item => new SelectListItem($"{item.Name} ({item.Detail})", item.Id.ToString(), item.Id == model.UsageNeedId)).ToArray();
        model.CategoryOptions = options.Categories
            .Select(item => new SelectListItem(item.Name, item.Id.ToString(), item.Id == model.CategoryId)).ToArray();
        model.OperatorOptions = RecommendationRuleOperators.All.OrderBy(item => item)
            .Select(item => new SelectListItem(item, item, item == model.Operator)).ToArray();
        model.SpecificationOptions = options.Specifications.Select(option =>
        {
            var details = (option.Detail ?? string.Empty).Split('|', 4);
            return new RecommendationSpecificationOption
            {
                Id = option.Id,
                Name = option.Name,
                CategoryId = int.TryParse(details.ElementAtOrDefault(0), out var categoryId) ? categoryId : 0,
                CategoryName = details.ElementAtOrDefault(1) ?? string.Empty,
                DataType = details.ElementAtOrDefault(2) ?? string.Empty,
                Unit = details.ElementAtOrDefault(3)
            };
        }).ToArray();
        return model;
    }
}
