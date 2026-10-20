using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Domain.Security;
using NEXORA.Web.Models;

namespace NEXORA.Web.Areas.Sales.Controllers;

[Area("Sales")]
[Authorize(Roles = RoleNames.Sales)]
public sealed class OrdersController(
    ISalesOrderManagementService orders,
    IOrderCancellationReviewService cancellationReviews) : Controller
{
    public async Task<IActionResult> Index(
        string? search,
        string? status,
        string? cancelRequestStatus,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var results = await orders.SearchAsync(new StaffOrderFilter(search, status, page, CancelRequestStatus: cancelRequestStatus), cancellationToken);
        return View(new StaffOrderIndexViewModel
        {
            Search = search,
            Status = status,
            CancelRequestStatus = cancelRequestStatus,
            Page = results.Page,
            Results = results
        });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken);
        if (order is null) return NotFound();

        return View(new StaffOrderDetailsViewModel
        {
            Order = order,
            CanConfirm = order.Status == OrderStatuses.Pending,
            CanHandoff = order.Status == OrderStatuses.Confirmed,
            CanMarkShipping = order.Status == OrderStatuses.Exported,
            CanComplete = order.Status == OrderStatuses.Shipping,
            CanReviewCancellation = OrderCancellationRules.CanReview(order.Status, order.CancelRequestStatus)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Confirm(int id, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(id, OrderWorkflowAction.Confirm, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Handoff(int id, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(id, OrderWorkflowAction.HandoffToWarehouse, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> MarkShipping(int id, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(id, OrderWorkflowAction.MarkShipping, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Complete(int id, CancellationToken cancellationToken) =>
        ApplyTransitionAsync(id, OrderWorkflowAction.Complete, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ReviewCancellation(
        int id,
        string decision,
        string? decisionNote,
        CancellationToken cancellationToken) =>
        ApplyCancellationReviewAsync(id, decision, decisionNote, cancellationToken);

    private async Task<IActionResult> ApplyCancellationReviewAsync(
        int id,
        string decision,
        string? decisionNote,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) || actorUserId <= 0)
        {
            return Forbid();
        }
        if (decision is not ("approve" or "reject")) return BadRequest();

        var result = await cancellationReviews.ReviewAsync(
            id, actorUserId, decision == "approve", decisionNote, cancellationToken);
        if (result.Failure == CancellationReviewFailure.NotFound) return NotFound();
        TempData[result.Succeeded ? "Success" : "Error"] = result.Failure switch
        {
            CancellationReviewFailure.None => decision == "approve"
                ? "Đã duyệt hủy đơn và giải phóng reservation trong cùng transaction."
                : "Đã từ chối yêu cầu hủy; đơn hàng và reservation được giữ nguyên.",
            CancellationReviewFailure.AlreadyReviewed => "Yêu cầu này đã được xử lý trước đó.",
            CancellationReviewFailure.InvalidDecisionNote => "Từ chối cần có lý do; ghi chú tối đa 1.000 ký tự.",
            CancellationReviewFailure.InvalidState => "Đơn hàng không còn đủ điều kiện xét duyệt yêu cầu hủy.",
            CancellationReviewFailure.ConcurrencyConflict => "Có thao tác khác vừa xử lý đơn hàng. Hãy tải lại để xem trạng thái mới.",
            _ => "Không thể lưu quyết định. Mọi thay đổi kho và đơn hàng đã được rollback."
        };
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<IActionResult> ApplyTransitionAsync(
        int id,
        OrderWorkflowAction action,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorUserId) || actorUserId <= 0)
        {
            return Forbid();
        }

        var result = await orders.TransitionAsync(id, actorUserId, action, cancellationToken);
        if (result.Failure == OrderTransitionFailure.NotFound) return NotFound();
        if (result.Failure == OrderTransitionFailure.InvalidState)
        {
            TempData["Error"] = "Đơn hàng không ở trạng thái phù hợp để thực hiện thao tác này.";
            return RedirectToAction(nameof(Details), new { id });
        }

        TempData["Success"] = result.Failure == OrderTransitionFailure.AlreadyApplied
            ? "Trạng thái này đã được cập nhật trước đó."
            : "Đã cập nhật trạng thái đơn hàng.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
