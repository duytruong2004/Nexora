using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;

namespace NEXORA.Web.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class OrdersController(ICustomerOrderService orders, UserManager<AppUser> users) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await orders.GetOrdersAsync(GetCurrentUserId(), cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await orders.GetOrderAsync(GetCurrentUserId(), id, cancellationToken);
        return order is null ? NotFound() : View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason, CancellationToken cancellationToken)
    {
        if (reason?.Length > 1000)
        {
            TempData["Error"] = "Lý do hủy không được vượt quá 1.000 ký tự.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await orders.CancelAsync(GetCurrentUserId(), id, reason, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Failure switch
        {
            CancelOrderFailure.None => "Đơn hàng đã được hủy và hàng giữ đã được giải phóng.",
            CancelOrderFailure.NotFound => "Không tìm thấy đơn hàng của bạn.",
            CancelOrderFailure.CannotCancel => "Chỉ đơn hàng đang chờ xác nhận (PENDING) mới có thể tự hủy.",
            _ => "Đơn hàng chưa thể hủy do dữ liệu kho vừa thay đổi. Vui lòng tải lại và thử lại."
        };
        return RedirectToAction(nameof(Details), new { id });
    }

    private int GetCurrentUserId()
    {
        var userId = users.GetUserId(User);
        if (!int.TryParse(userId, out var parsed) || parsed <= 0)
            throw new InvalidOperationException("The authenticated account has no valid NEXORA user ID.");
        return parsed;
    }
}
