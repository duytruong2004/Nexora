using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using System.Security.Claims;

namespace NEXORA.Web.ViewComponents;

public sealed class CartSummaryViewComponent(ICartService carts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userId, out var parsedId) || parsedId <= 0)
        {
            return Content(string.Empty);
        }

        var count = await carts.GetItemCountAsync(parsedId, cancellationToken);
        return View(count);
    }
}
