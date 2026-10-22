using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Security;

namespace NEXORA.Web.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class FavoritesController(IFavoriteService favorites) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await favorites.GetFavoritesAsync(GetCurrentUserId(), cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int productId, string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await favorites.AddAsync(GetCurrentUserId(), productId, cancellationToken);
        var message = result.Failure switch
        {
            FavoriteFailure.None => result.AlreadyApplied ? "Sản phẩm đã có trong danh sách yêu thích." : "Đã thêm sản phẩm vào danh sách yêu thích.",
            FavoriteFailure.ProductNotFound => "Sản phẩm không tồn tại hoặc đã ngừng kinh doanh.",
            _ => "Chưa thể lưu sản phẩm yêu thích. Vui lòng thử lại."
        };

        if (WantsJsonResponse())
        {
            if (!result.Succeeded)
            {
                var statusCode = result.Failure == FavoriteFailure.ProductNotFound
                    ? StatusCodes.Status404NotFound
                    : StatusCodes.Status503ServiceUnavailable;
                return StatusCode(statusCode, new { message });
            }

            return Json(new { message, alreadyApplied = result.AlreadyApplied });
        }

        TempData[result.Succeeded ? "Success" : "Error"] = message;
        return LocalOrIndex(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int productId, CancellationToken cancellationToken)
    {
        var result = await favorites.RemoveAsync(GetCurrentUserId(), productId, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? "Đã xóa sản phẩm khỏi danh sách yêu thích."
            : "Chưa thể xóa sản phẩm. Vui lòng thử lại.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult LocalOrIndex(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private bool WantsJsonResponse() => Request.Headers.Accept
        .ToString()
        .Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private int GetCurrentUserId()
    {
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && userId > 0
            ? userId
            : throw new InvalidOperationException("Authenticated account has no valid NEXORA user ID.");
    }
}
