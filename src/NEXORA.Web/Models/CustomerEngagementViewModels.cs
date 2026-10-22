using System.ComponentModel.DataAnnotations;
using NEXORA.Application.Engagement;

namespace NEXORA.Web.Models;

public sealed class CustomerReviewsViewModel
{
    public IReadOnlyList<EligibleReviewOrderLine> EligibleLines { get; init; } = [];
    public IReadOnlyList<CustomerReviewItem> Reviews { get; init; } = [];
}

public sealed class SubmitReviewViewModel
{
    [Range(1, int.MaxValue)]
    public int OrderDetailId { get; set; }

    [Range(1, 5, ErrorMessage = "Vui lòng chọn số sao từ 1 đến 5.")]
    [Display(Name = "Đánh giá")]
    public int Rating { get; set; }

    [StringLength(3000, ErrorMessage = "Nội dung không được vượt quá 3.000 ký tự.")]
    [Display(Name = "Nhận xét")]
    public string? Comment { get; set; }
}

public sealed class ReviewModerationViewModel
{
    public ReviewModerationFilter Filter { get; init; } = new(null, null);
    public ReviewModerationPage Page { get; init; } = new([], 0, 0, 1, 25);
}
