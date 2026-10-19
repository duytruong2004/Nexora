using System.ComponentModel.DataAnnotations;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;

namespace NEXORA.Web.Areas.Admin.Models;

public sealed class PromotionListViewModel
{
    public IReadOnlyList<PromotionAdminRow> Items { get; init; } = [];
}

public sealed class PromotionFormViewModel : IValidatableObject
{
    public int? Id { get; set; }

    [Required, StringLength(200, MinimumLength = 2)]
    [Display(Name = "Tên chương trình")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Required]
    [Display(Name = "Loại giảm giá")]
    public string DiscountType { get; set; } = "PERCENTAGE";

    [Range(
        typeof(decimal),
        "0",
        "9999999999999999.99",
        ParseLimitsInInvariantCulture = true,
        ConvertValueInInvariantCulture = true,
        ErrorMessage = "Mức giảm phải từ 0 đến 9.999.999.999.999.999,99.")]
    [Display(Name = "Mức giảm")]
    public decimal DiscountValue { get; set; }

    [Required]
    [Display(Name = "Bắt đầu theo giờ địa phương")]
    public DateTime StartDate { get; set; }

    [Required]
    [Display(Name = "Kết thúc theo giờ địa phương")]
    public DateTime EndDate { get; set; }

    [Display(Name = "Đang áp dụng")]
    public bool IsActive { get; set; } = true;

    [MinLength(1, ErrorMessage = "Chọn ít nhất một sản phẩm.")]
    [Display(Name = "Sản phẩm áp dụng")]
    public List<int> SelectedProductIds { get; set; } = [];

    public IReadOnlyList<PromotionProductOption> Products { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DiscountType is not PromotionDiscountTypes.Percentage and not PromotionDiscountTypes.FixedAmount)
        {
            yield return new ValidationResult("Loại giảm giá không hợp lệ.", [nameof(DiscountType)]);
        }

        if (DiscountType == PromotionDiscountTypes.Percentage && DiscountValue > 100m)
        {
            yield return new ValidationResult("Phần trăm giảm phải từ 0 đến 100.", [nameof(DiscountValue)]);
        }

        if (decimal.Round(DiscountValue, 2) != DiscountValue)
        {
            yield return new ValidationResult(
                "Mức giảm chỉ được tối đa hai chữ số thập phân.",
                [nameof(DiscountValue)]);
        }
    }

    public static PromotionFormViewModel From(PromotionAdminForm form, IReadOnlyList<PromotionProductOption> products) => new()
    {
        Id = form.Id,
        Name = form.Name,
        Description = form.Description,
        DiscountType = form.DiscountType,
        DiscountValue = form.DiscountValue,
        StartDate = DateTime.SpecifyKind(form.StartDate, DateTimeKind.Utc).ToLocalTime(),
        EndDate = DateTime.SpecifyKind(form.EndDate, DateTimeKind.Utc).ToLocalTime(),
        IsActive = form.IsActive,
        SelectedProductIds = form.ProductIds.ToList(),
        Products = products
    };
}
