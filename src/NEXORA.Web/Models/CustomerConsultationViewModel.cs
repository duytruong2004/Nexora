using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using NEXORA.Application.Consultation;

namespace NEXORA.Web.Models;

public sealed class CustomerConsultationViewModel
{
    [Required(ErrorMessage = "Vui lòng mô tả nhu cầu của bạn.")]
    [StringLength(1200, ErrorMessage = "Nội dung không được vượt quá 1.200 ký tự.")]
    [Display(Name = "Mô tả nhu cầu")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Danh mục xác nhận")]
    public string? ConfirmedCategory { get; set; }

    [Display(Name = "Mục đích xác nhận")]
    public string? ConfirmedUsage { get; set; }

    public IReadOnlyList<SelectListItem> CategoryOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> UsageOptions { get; set; } = [];
    public ConsultationOutcome? Outcome { get; set; }
}
