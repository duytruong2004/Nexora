using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class CheckoutController(
    IOrderPricingService pricing,
    ICheckoutService checkout,
    UserManager<AppUser> users,
    IDataProtectionProvider protectionProvider) : Controller
{
    private readonly IDataProtector _quoteProtector = protectionProvider.CreateProtector("NEXORA.CheckoutQuote.v1");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var userId = GetCurrentUserId();
        var quote = await pricing.GetCurrentQuoteAsync(userId, cancellationToken);
        var model = new CheckoutViewModel
        {
            CustomerName = user.FullName,
            Phone = user.PhoneNumber ?? string.Empty,
            ShippingAddress = user.Address ?? string.Empty,
            RequestId = Guid.NewGuid(),
            Quote = quote
        };
        model.QuoteToken = ProtectQuote(quote, userId);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Place(CheckoutViewModel model, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var currentQuote = await pricing.GetCurrentQuoteAsync(userId, cancellationToken);
        var expectedFingerprint = TryReadQuote(model.QuoteToken, userId);
        if (expectedFingerprint is null)
        {
            ModelState.AddModelError(string.Empty, "Báo giá đã hết hạn hoặc không hợp lệ. Vui lòng kiểm tra lại.");
        }

        if (!ModelState.IsValid)
        {
            return Render(model, currentQuote);
        }

        var result = await checkout.PlaceOrderAsync(userId, new PlaceOrderInput(
            model.RequestId,
            model.CustomerName,
            model.Phone,
            model.ShippingAddress,
            model.Note,
            expectedFingerprint!), cancellationToken);

        if (result.Succeeded)
        {
            TempData["Success"] = "Đơn hàng đã được tạo và tồn kho đã được giữ.";
            return RedirectToAction("Details", "Orders", new { id = result.OrderId });
        }

        switch (result.Failure)
        {
            case PlaceOrderFailure.QuoteChanged:
                ModelState.AddModelError(string.Empty,
                    "Giá hoặc khuyến mại đã thay đổi. Hãy kiểm tra báo giá mới và xác nhận lại.");
                currentQuote = result.CurrentQuote ?? currentQuote;
                break;
            case PlaceOrderFailure.CartEmpty:
                ModelState.AddModelError(string.Empty, "Giỏ hàng đang trống.");
                break;
            case PlaceOrderFailure.ProductUnavailable:
                ModelState.AddModelError(string.Empty, "Một sản phẩm trong giỏ không còn được kinh doanh.");
                break;
            case PlaceOrderFailure.InsufficientStock:
                ModelState.AddModelError(string.Empty, "Tồn kho vừa thay đổi và không đủ cho số lượng yêu cầu.");
                break;
            case PlaceOrderFailure.InvalidRequest:
                ModelState.AddModelError(string.Empty, "Yêu cầu thanh toán không hợp lệ. Vui lòng tải lại trang.");
                break;
            case PlaceOrderFailure.TransientFailure:
                ModelState.AddModelError(string.Empty, "Cơ sở dữ liệu đang bận. Vui lòng thử lại bằng yêu cầu hiện tại.");
                break;
            case PlaceOrderFailure.CommitResultUnknown:
                ModelState.AddModelError(string.Empty,
                    "Chưa xác minh được kết quả xử lý. Hãy gửi lại yêu cầu hiện tại để kiểm tra, không tạo yêu cầu thanh toán mới.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "Chưa thể tạo đơn hàng. Vui lòng thử lại sau.");
                break;
        }

        return Render(model, currentQuote);
    }

    private IActionResult Render(CheckoutViewModel model, CheckoutQuote quote)
    {
        model.Quote = quote;
        if (model.RequestId == Guid.Empty) model.RequestId = Guid.NewGuid();
        model.QuoteToken = ProtectQuote(quote, GetCurrentUserId());
        return View("Index", model);
    }

    private string ProtectQuote(CheckoutQuote quote, int userId)
    {
        var payload = JsonSerializer.Serialize(new ProtectedQuote(
            quote.Fingerprint,
            DateTimeOffset.UtcNow.AddMinutes(20).ToUnixTimeSeconds(),
            userId));
        return _quoteProtector.Protect(payload);
    }

    private string? TryReadQuote(string protectedQuote, int userId)
    {
        if (string.IsNullOrWhiteSpace(protectedQuote)) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<ProtectedQuote>(_quoteProtector.Unprotect(protectedQuote));
            if (payload is null || payload.UserId != userId ||
                payload.ExpiresAtUnixSeconds < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            return payload.Fingerprint;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private int GetCurrentUserId()
    {
        var userId = users.GetUserId(User);
        if (!int.TryParse(userId, out var parsed) || parsed <= 0)
            throw new InvalidOperationException("The authenticated account has no valid NEXORA user ID.");
        return parsed;
    }

    private sealed record ProtectedQuote(string Fingerprint, long ExpiresAtUnixSeconds, int UserId);
}
