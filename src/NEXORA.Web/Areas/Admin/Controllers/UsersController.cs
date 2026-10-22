using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
[Route("Admin/Users/[action]")]
public sealed class UsersController(IAccountAdministrationService accounts) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        string? role,
        bool? isActive,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        ViewData["Title"] = "Tài khoản";
        var results = await accounts.SearchUsersAsync(
            new AccountSearchFilter(search, role, isActive, page), cancellationToken);
        return View(new AccountSearchViewModel(search, role, isActive, results.Page, results));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var account = await accounts.GetUserDetailsAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Chi tiết tài khoản";
        ViewData["CurrentUserId"] = CurrentUserId();
        return View(account);
    }

    [HttpGet]
    public IActionResult CreateStaff()
    {
        ViewData["Title"] = "Tạo nhân viên";
        return View(new CreateStaffViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStaff(CreateStaffViewModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Tạo nhân viên";
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var actorId = CurrentUserId();
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await accounts.CreateStaffAsync(
            new CreateStaffInput(model.FullName, model.Email, model.Role), actorId.Value, cancellationToken);
        if (result.Status == AccountOperationStatus.Success)
        {
            TempData["Success"] = "Tài khoản nhân viên đã được tạo và email được bàn giao cho dịch vụ gửi thư. Việc bàn giao không xác nhận email đã tới hộp thư.";
            return RedirectToAction(nameof(Details), new { id = result.UserId });
        }

        if (result.Status == AccountOperationStatus.DeliveryFailed)
        {
            TempData["Error"] = "Tài khoản đã được tạo nhưng lời mời chưa được gửi. Tài khoản vẫn chưa kích hoạt; có thể gửi lại sau.";
            return RedirectToAction(nameof(Details), new { id = result.UserId });
        }

        AddSafeError(result);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lock(int id, CancellationToken cancellationToken) =>
        await RunUserActionAsync(id, "LockUser", actorId => accounts.LockUserAsync(id, actorId, cancellationToken), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(int id, CancellationToken cancellationToken) =>
        await RunUserActionAsync(id, "UnlockUser", actorId => accounts.UnlockUserAsync(id, actorId, cancellationToken), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvitation(int id, CancellationToken cancellationToken) =>
        await RunUserActionAsync(id, "ResendInvitation", actorId => accounts.ResendInvitationAsync(id, actorId, cancellationToken), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(
        int id,
        ChangeStaffRoleViewModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Vai trò được gửi không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var actorId = CurrentUserId();
        if (actorId is null)
        {
            return Forbid();
        }

        return await RunUserActionAsync(id, "ChangeStaffRole",
            actor => accounts.ChangeStaffRoleAsync(id, model.Role, actor, cancellationToken), cancellationToken);
    }

    private async Task<IActionResult> RunUserActionAsync(
        int id,
        string action,
        Func<int, Task<AccountOperationResult>> operation,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null)
        {
            return Forbid();
        }

        var result = await operation(actorId.Value);
        TempData[result.Status is AccountOperationStatus.Success or AccountOperationStatus.AlreadyInState
            ? "Success"
            : "Error"] = MessageFor(action, result);
        var exists = await accounts.GetUserDetailsAsync(id, cancellationToken);
        return exists is null ? RedirectToAction(nameof(Index)) : RedirectToAction(nameof(Details), new { id });
    }

    private static string MessageFor(string action, AccountOperationResult result) => result.Status switch
    {
        AccountOperationStatus.Success when action == "LockUser" => "Tài khoản đã được khóa.",
        AccountOperationStatus.Success when action == "UnlockUser" => "Tài khoản đã được mở khóa.",
        AccountOperationStatus.Success when action == "ResendInvitation" => "Lời mời đã được bàn giao cho dịch vụ gửi thư; người nhận cần kiểm tra hộp thư. Không xác nhận việc email đã được giao.",
        AccountOperationStatus.Success => "Vai trò nhân viên đã được cập nhật; phiên đăng nhập cũ sẽ bị từ chối ở yêu cầu tiếp theo.",
        AccountOperationStatus.AlreadyInState => "Tài khoản hoặc vai trò đã ở trạng thái được yêu cầu.",
        AccountOperationStatus.DeliveryFailed => "Không gửi được lời mời. Tài khoản vẫn chưa kích hoạt và có thể gửi lại sau.",
        AccountOperationStatus.NotFound => "Không tìm thấy tài khoản.",
        AccountOperationStatus.Forbidden => "Thao tác này không được phép.",
        AccountOperationStatus.SecurityBlocked when result.ErrorCode == "LastActiveAdmin" => "Không thể khóa Admin đang hoạt động cuối cùng.",
        AccountOperationStatus.SecurityBlocked when result.ErrorCode == "AccountHasNotBeenActivated" => "Không thể mở khóa tài khoản chưa hoàn tất thiết lập mật khẩu.",
        AccountOperationStatus.SecurityBlocked when result.ErrorCode == "AccountRoleStateInconsistent" => "Tài khoản có trạng thái vai trò không nhất quán; cần xử lý theo quy trình riêng.",
        AccountOperationStatus.SecurityBlocked => "Tài khoản không đáp ứng điều kiện an toàn cho thao tác này.",
        AccountOperationStatus.Conflict => "Dữ liệu vừa được thay đổi hoặc thao tác đang bận. Hãy tải lại và thử lại.",
        _ => "Không thể hoàn tất thao tác. Hãy kiểm tra dữ liệu và thử lại."
    };

    private void AddSafeError(AccountOperationResult result)
    {
        ModelState.AddModelError(string.Empty, result.Status switch
        {
            AccountOperationStatus.Conflict => "Email đã được sử dụng hoặc dữ liệu vừa thay đổi.",
            AccountOperationStatus.SecurityBlocked => "Vai trò nhân viên chưa sẵn sàng. Hãy liên hệ quản trị viên hệ thống.",
            _ => "Thông tin nhân viên chưa hợp lệ. Hãy kiểm tra lại."
        });
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;
}
