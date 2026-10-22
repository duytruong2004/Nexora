using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Email;
using NEXORA.Application.Identity;
using NEXORA.Domain.Security;
using NEXORA.Infrastructure.Identity;
using NEXORA.Web.Models;

namespace NEXORA.Web.Controllers;

[Route("account/[action]")]
public sealed class AccountController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IEmailSender emailSender,
    ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var email = model.Email.Trim();
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            FullName = model.FullName.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(model);
        }

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.Customer);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            AddIdentityErrors(roleResult);
            return View(model);
        }

        logger.LogInformation("A customer account was registered.");
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) =>
        View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !user.IsActive)
        {
            return RedirectToAction(nameof(Login));
        }

        var roles = await userManager.GetRolesAsync(user);
        return View(new MyAccountViewModel(
            user.FullName,
            user.Email ?? string.Empty,
            user.CreatedAt,
            roles.OrderBy(role => role, StringComparer.Ordinal).ToArray()));
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
            return View(model);
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user, model.Password, lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var roles = await userManager.GetRolesAsync(user);
            if (!AccountAuthenticationPolicy.CanIssueCookie(user.IsActive, roles))
            {
                // Do not issue a cookie for inactive, role-less, multi-role, or unknown-role accounts.
                // Keep the public response generic so internal role anomalies are not disclosed.
                ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
                return View(model);
            }

            await signInManager.SignInAsync(user, model.RememberMe);
            logger.LogInformation("A user signed in.");
            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "Tài khoản tạm thời bị khóa. Vui lòng thử lại sau.");
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Email hoặc mật khẩu không đúng.");
        }

        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        logger.LogInformation("A user signed out.");
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is { IsActive: true })
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = Url.ActionLink(nameof(ResetPassword), "Account", new
            {
                email = user.Email,
                token
            });

            if (resetUrl is not null && user.Email is not null)
            {
                var encodedUrl = HtmlEncoder.Default.Encode(resetUrl);
                await emailSender.SendAsync(new EmailMessage(
                    user.Email,
                    "Đặt lại mật khẩu NEXORA",
                    $"<p>Bạn đã yêu cầu đặt lại mật khẩu NEXORA.</p><p><a href=\"{encodedUrl}\">Tạo mật khẩu mới</a></p><p>Nếu bạn không yêu cầu thao tác này, hãy bỏ qua email.</p>"));
            }
        }

        return View("ForgotPasswordConfirmation");
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(string? email, string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return View("ResetPasswordInvalid");
        }

        return View(new ResetPasswordViewModel { Email = email, Token = token });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return View("ResetPasswordInvalid");
        }

        var result = await userManager.ResetPasswordAsync(user, model.Token, model.Password);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        logger.LogInformation("A password reset completed.");
        return View("ResetPasswordConfirmation");
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Code switch
            {
                "DuplicateEmail" or "DuplicateUserName" => "Email này đã được sử dụng.",
                "PasswordTooShort" => "Mật khẩu phải có ít nhất 10 ký tự.",
                "PasswordRequiresDigit" => "Mật khẩu cần có ít nhất một chữ số.",
                "PasswordRequiresUpper" => "Mật khẩu cần có ít nhất một chữ in hoa.",
                "PasswordRequiresLower" => "Mật khẩu cần có ít nhất một chữ thường.",
                "PasswordRequiresNonAlphanumeric" => "Mật khẩu cần có ít nhất một ký tự đặc biệt.",
                _ => "Không thể hoàn tất thao tác. Vui lòng kiểm tra dữ liệu và thử lại."
            });
        }
    }
}
