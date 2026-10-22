using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Identity;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

[AllowAnonymous]
[Route("account/[action]")]
public sealed class StaffActivationController(IAccountAdministrationService accounts) : Controller
{
    [HttpGet]
    public IActionResult ActivateStaff()
    {
        SetSensitivePageHeaders();
        return View(new StaffActivationViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateStaffInvitation(
        StaffInvitationValidationInput input,
        CancellationToken cancellationToken)
    {
        SetSensitivePageHeaders();
        var valid = ModelState.IsValid &&
                    await accounts.IsInvitationValidAsync(input.Email, input.Token, cancellationToken);
        return Json(new { valid });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateStaff(StaffActivationViewModel model, CancellationToken cancellationToken)
    {
        SetSensitivePageHeaders();
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await accounts.ActivateStaffAsync(model.Email, model.Token, model.Password, cancellationToken);
        if (result.Status == AccountOperationStatus.Success)
        {
            TempData["StatusMessage"] = "Tài khoản nhân viên đã được kích hoạt. Bạn có thể đăng nhập bằng mật khẩu vừa tạo.";
            return RedirectToAction("Login", "Account");
        }

        if (result.Status == AccountOperationStatus.ValidationError)
        {
            ModelState.AddModelError(nameof(model.Password), result.ErrorCode switch
            {
                "PasswordTooShort" => "Mật khẩu phải có ít nhất 10 ký tự.",
                "PasswordRequiresDigit" => "Mật khẩu cần có ít nhất một chữ số.",
                "PasswordRequiresUpper" => "Mật khẩu cần có ít nhất một chữ in hoa.",
                "PasswordRequiresLower" => "Mật khẩu cần có ít nhất một chữ thường.",
                "PasswordRequiresNonAlphanumeric" => "Mật khẩu cần có ít nhất một ký tự đặc biệt.",
                "PasswordRequiresUniqueChars" => "Mật khẩu phải có ít nhất 4 ký tự khác nhau.",
                _ => "Mật khẩu chưa đáp ứng chính sách bảo mật."
            });
            return View(model);
        }

        return View("Invalid");
    }

    private void SetSensitivePageHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
