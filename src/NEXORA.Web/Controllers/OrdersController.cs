using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Web.Models;

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
        return order is null
            ? NotFound()
            : View(new CustomerOrderDetailsViewModel { Order = order, RequestId = Guid.NewGuid() });
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestCancellation(
        int id,
        CustomerCancellationRequestInput input,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (!ModelState.IsValid || input.RequestId == Guid.Empty)
        {
            if (input.RequestId == Guid.Empty)
            {
                ModelState.AddModelError(nameof(input.RequestId), "Mã yêu cầu không hợp lệ. Vui lòng tải lại trang.");
            }

            var invalidOrder = await orders.GetOrderAsync(userId, id, cancellationToken);
            if (invalidOrder is null) return NotFound();
            return View("Details", new CustomerOrderDetailsViewModel
            {
                Order = invalidOrder,
                RequestId = input.RequestId == Guid.Empty ? Guid.NewGuid() : input.RequestId
            });
        }

        var result = await orders.RequestCancellationAsync(userId, id, input.RequestId, input.Reason, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Failure switch
        {
            CustomerCancellationFailure.None => "Yêu cầu hủy đã được gửi. Đơn hàng vẫn được giữ nguyên cho đến khi Sales hoặc Admin xét duyệt.",
            CustomerCancellationFailure.AlreadyApplied => "Yêu cầu này đã được ghi nhận trước đó.",
            CustomerCancellationFailure.NotFound => "Không tìm thấy đơn hàng của bạn.",
            CustomerCancellationFailure.InvalidState => "Chỉ đơn đã xác nhận hoặc đang chuẩn bị mới được gửi yêu cầu hủy.",
            CustomerCancellationFailure.AlreadyRequested => "Đơn hàng đã từng có yêu cầu hủy và không thể gửi lại.",
            CustomerCancellationFailure.DuplicateRequestId => "Mã yêu cầu đã được sử dụng. Vui lòng tải lại trang và thử lại.",
            CustomerCancellationFailure.InvalidRequest => "Vui lòng nhập lý do hợp lệ, tối đa 1.000 ký tự.",
            _ => "Chưa thể gửi yêu cầu do xung đột dữ liệu. Vui lòng tải lại đơn hàng và kiểm tra trạng thái hiện tại."
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
