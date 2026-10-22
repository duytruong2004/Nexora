using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Security;
using NEXORA.Web.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ReviewsController(IReviewModerationService moderation) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? status, string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var filter = new ReviewModerationFilter(status, search, page);
        return View(new ReviewModerationViewModel { Filter = filter, Page = await moderation.SearchAsync(filter, cancellationToken) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transition(
        int id,
        ReviewModerationAction action,
        string? status,
        string? search,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action)) return BadRequest();
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId) || actorId <= 0)
            return Forbid();

        var result = await moderation.TransitionAsync(id, actorId, action, cancellationToken);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Failure switch
        {
            ReviewModerationFailure.None => result.StatusAfter == NEXORA.Domain.Entities.ReviewStatuses.Approved
                ? "Đánh giá đã được duyệt và hiển thị công khai."
                : "Đánh giá đã được ẩn khỏi trang sản phẩm.",
            ReviewModerationFailure.AlreadyInState => "Đánh giá đã ở trạng thái yêu cầu.",
            ReviewModerationFailure.NotFound => "Không tìm thấy đánh giá.",
            ReviewModerationFailure.UnknownStatus => "Trạng thái đánh giá không xác định; không có thay đổi nào được áp dụng.",
            ReviewModerationFailure.InvalidTransition => "Không thể chuyển trạng thái đánh giá này.",
            _ => "Đánh giá vừa được thay đổi bởi thao tác khác. Hãy tải lại danh sách."
        };
        return RedirectToAction(nameof(Index), new { status, search, page });
    }
}
