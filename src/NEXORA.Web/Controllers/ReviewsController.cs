using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Security;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

[Authorize(Roles = RoleNames.Customer)]
public sealed class ReviewsController(IReviewService reviews) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await BuildPageAsync(cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SubmitReviewViewModel input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", await BuildPageAsync(cancellationToken));
        }

        var result = await reviews.SubmitAsync(
            GetCurrentUserId(), input.OrderDetailId, input.Rating, input.Comment, cancellationToken);
        if (result.Succeeded)
        {
            TempData["Success"] = "Đánh giá đã được gửi và đang chờ kiểm duyệt.";
            return RedirectToAction(nameof(Index));
        }

        TempData["Error"] = result.Failure switch
        {
            ReviewSubmissionFailure.OrderLineNotFound => "Không tìm thấy sản phẩm trong đơn hàng của bạn.",
            ReviewSubmissionFailure.IneligibleOrder => "Chỉ có thể đánh giá sản phẩm thuộc đơn đã hoàn thành.",
            ReviewSubmissionFailure.AlreadyReviewed => "Sản phẩm trong dòng đơn này đã được đánh giá.",
            ReviewSubmissionFailure.InvalidRating => "Số sao phải nằm trong khoảng từ 1 đến 5.",
            ReviewSubmissionFailure.InvalidComment => "Nhận xét không được vượt quá 3.000 ký tự.",
            _ => "Chưa thể gửi đánh giá. Vui lòng tải lại và thử lại."
        };
        return RedirectToAction(nameof(Index));
    }

    private async Task<CustomerReviewsViewModel> BuildPageAsync(CancellationToken cancellationToken) => new()
    {
        EligibleLines = await reviews.GetEligibleOrderLinesAsync(GetCurrentUserId(), cancellationToken),
        Reviews = await reviews.GetCustomerReviewsAsync(GetCurrentUserId(), cancellationToken)
    };

    private int GetCurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && userId > 0
            ? userId
            : throw new InvalidOperationException("Authenticated account has no valid NEXORA user ID.");
}
