using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Web.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class CartController(ICartService carts, UserManager<AppUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var snapshot = await carts.GetAsync(GetCurrentUserId(), cancellationToken);
        return View(snapshot);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        int productId,
        int quantity = 1,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        var result = await carts.AddAsync(userId, productId, quantity, cancellationToken);
        if (WantsJsonResponse())
        {
            if (!result.Succeeded)
            {
                return JsonAddFailure(result);
            }

            var itemCount = await carts.GetItemCountAsync(userId, cancellationToken);
            return Json(new { message = "Đã thêm vào giỏ hàng.", itemCount });
        }

        SetFeedback(result, adding: true);
        return RedirectToLocal(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int cartItemId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await carts.SetQuantityAsync(userId, cartItemId, quantity, cancellationToken);
        if (WantsJsonResponse())
        {
            if (!result.Succeeded)
            {
                var currentSnapshot = await carts.GetAsync(userId, cancellationToken);
                return JsonCartFailure(result, currentSnapshot);
            }

            var snapshot = await carts.GetAsync(userId, cancellationToken);
            return JsonCart("Đã cập nhật số lượng.", snapshot);
        }

        SetFeedback(result, adding: false);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int cartItemId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var result = await carts.RemoveAsync(userId, cartItemId, cancellationToken);
        if (WantsJsonResponse())
        {
            if (!result.Succeeded)
            {
                return JsonFailure(result, removing: true);
            }

            var snapshot = await carts.GetAsync(userId, cancellationToken);
            return JsonCart("Đã xóa sản phẩm khỏi giỏ hàng.", snapshot);
        }

        if (result.Succeeded)
        {
            TempData["Success"] = "Đã xóa sản phẩm khỏi giỏ hàng.";
        }
        else
        {
            TempData["Error"] = "Sản phẩm không còn trong giỏ hàng của bạn.";
        }

        return RedirectToAction(nameof(Index));
    }

    private int GetCurrentUserId()
    {
        var userId = users.GetUserId(User);
        if (!int.TryParse(userId, out var parsed) || parsed <= 0)
        {
            throw new InvalidOperationException("The authenticated account has no valid NEXORA user ID.");
        }

        return parsed;
    }

    private void SetFeedback(CartMutationResult result, bool adding)
    {
        if (result.Succeeded)
        {
            TempData["Success"] = adding ? "Đã thêm sản phẩm vào giỏ hàng." : "Đã cập nhật số lượng.";
            return;
        }

        TempData["Error"] = FailureMessage(result.Failure);
    }

    private IActionResult JsonFailure(CartMutationResult result, bool removing = false)
    {
        var message = removing && result.Failure == CartMutationFailure.ItemNotFound
            ? "Sản phẩm không còn trong giỏ hàng của bạn."
            : FailureMessage(result.Failure);
        var statusCode = result.Failure switch
        {
            CartMutationFailure.InvalidQuantity => StatusCodes.Status400BadRequest,
            CartMutationFailure.ItemNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        return StatusCode(statusCode, new { message });
    }

    private IActionResult JsonCartFailure(CartMutationResult result, CartSnapshot snapshot)
    {
        var statusCode = result.Failure switch
        {
            CartMutationFailure.InvalidQuantity => StatusCodes.Status400BadRequest,
            CartMutationFailure.ItemNotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        return StatusCode(statusCode, new
        {
            message = FailureMessage(result.Failure),
            cart = CartData(snapshot)
        });
    }

    private IActionResult JsonAddFailure(CartMutationResult result)
    {
        var statusCode = result.Failure switch
        {
            CartMutationFailure.InvalidQuantity => StatusCodes.Status400BadRequest,
            CartMutationFailure.ProductUnavailable => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        return StatusCode(statusCode, new { message = FailureMessage(result.Failure) });
    }

    private static string FailureMessage(CartMutationFailure failure) => failure switch
    {
        CartMutationFailure.InvalidQuantity => "Số lượng phải là số nguyên dương.",
        CartMutationFailure.ProductUnavailable => "Sản phẩm không còn được kinh doanh.",
        CartMutationFailure.OutOfStock => "Sản phẩm hiện đã hết hàng.",
        CartMutationFailure.InsufficientStock => "Số lượng yêu cầu vượt quá số lượng có thể bán. Tồn kho có thể đã thay đổi; vui lòng cập nhật giỏ hàng.",
        CartMutationFailure.ItemNotFound => "Không tìm thấy sản phẩm trong giỏ hàng của bạn.",
        _ => "Giỏ hàng vừa được cập nhật ở một yêu cầu khác. Vui lòng tải lại và thử lại."
    };

    private bool WantsJsonResponse() => Request.Headers.Accept
        .ToString()
        .Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private IActionResult JsonCart(string message, CartSnapshot snapshot) => Json(new
    {
        message,
        cart = CartData(snapshot)
    });

    private static object CartData(CartSnapshot snapshot) => new
    {
        isEmpty = snapshot.IsEmpty,
        itemCount = snapshot.ItemCount,
        subtotal = snapshot.Subtotal,
        promotionDiscount = snapshot.PromotionDiscount,
        total = snapshot.Total,
        canCheckout = snapshot.CanCheckout,
        items = snapshot.Items.Select(item => new
        {
            cartItemId = item.CartItemId,
            productId = item.ProductId,
            productName = item.ProductName,
            unitPrice = item.UnitPrice,
            discountAmount = item.DiscountAmount,
            promotionName = item.PromotionName,
            quantity = item.Quantity,
            availableQuantity = item.AvailableQuantity,
            isAvailable = item.IsAvailable,
            lineTotal = item.LineTotal
        })
    };

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(Index));
    }
}
