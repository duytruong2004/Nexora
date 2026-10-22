using System.ComponentModel.DataAnnotations;
using NEXORA.Application.Identity;

namespace NEXORA.Web.Areas.Admin.Models;

public sealed class CreateStaffViewModel
{
    [Required, StringLength(200)]
    [Display(Name = "Họ và tên")]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    [Display(Name = "Email công việc")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Sales|Warehouse)$", ErrorMessage = "Chỉ được tạo tài khoản Sales hoặc Warehouse.")]
    [Display(Name = "Vai trò")]
    public string Role { get; set; } = string.Empty;
}

public sealed class ChangeStaffRoleViewModel
{
    [Required]
    [RegularExpression("^(Sales|Warehouse)$", ErrorMessage = "Chỉ được chọn Sales hoặc Warehouse.")]
    public string Role { get; set; } = string.Empty;
}

public sealed record AccountSearchViewModel(
    string? Search,
    string? Role,
    bool? IsActive,
    int Page,
    ManagedAccountPage Results);
