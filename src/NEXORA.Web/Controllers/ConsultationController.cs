using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Catalog;
using NEXORA.Application.Consultation;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

[Route("tu-van-san-pham")]
public sealed class ConsultationController(
    IConsultationService consultation,
    IConsultationDataSource consultationData,
    ICustomerCatalogQueryService catalog,
    ILogger<ConsultationController> logger) : Controller
{
    private static readonly HashSet<string> SupportedCategories = new(StringComparer.Ordinal)
    {
        "Laptop", "Điện thoại", "Máy tính bảng"
    };

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View("Index", await BuildModelAsync(new CustomerConsultationViewModel(), null, cancellationToken));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(CustomerConsultationViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildModelAsync(model, null, cancellationToken));
        }

        var outcome = await consultation.ConsultAsync(
            new ConsultationRequest(model.Description, model.ConfirmedCategory, model.ConfirmedUsage), cancellationToken);
        logger.LogInformation("Consultation request completed with outcome {OutcomeKind}, result count {ResultCount}, elapsed {ElapsedMilliseconds} ms.",
            outcome.Kind, outcome.Recommendations.Count, outcome.ElapsedMilliseconds);
        return View("Index", await BuildModelAsync(model, outcome, cancellationToken));
    }

    private async Task<CustomerConsultationViewModel> BuildModelAsync(
        CustomerConsultationViewModel model,
        ConsultationOutcome? outcome,
        CancellationToken cancellationToken)
    {
        var home = await catalog.GetHomeAsync(cancellationToken);
        var usageNeeds = await consultationData.GetActiveUsageNeedsAsync(cancellationToken);
        var selectedCategory = model.ConfirmedCategory ?? outcome?.Profile.Category.Value;
        var selectedUsage = model.ConfirmedUsage;
        if (selectedUsage is null && outcome?.Profile.UsageNeed.Value is string profileUsage)
        {
            selectedUsage = usageNeeds.FirstOrDefault(item => item.Code == profileUsage || item.Name == profileUsage)?.Code;
        }

        model.ConfirmedCategory = selectedCategory;
        model.ConfirmedUsage = selectedUsage;
        model.Outcome = outcome;
        model.CategoryOptions = home.Categories
            .Where(item => SupportedCategories.Contains(item.Name))
            .OrderBy(item => item.Name)
            .Select(item => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(item.Name, item.Name,
                string.Equals(item.Name, selectedCategory, StringComparison.Ordinal)))
            .ToArray();
        model.UsageOptions = usageNeeds.Select(item => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(
            item.Name, item.Code, item.Code == selectedUsage)).ToArray();
        return model;
    }
}
