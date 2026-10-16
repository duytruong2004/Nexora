using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;
using NEXORA.Application.Consultation;

namespace NEXORA.Web.Areas.Admin.Models;

public sealed class UsageNeedAdminPageViewModel
{
    public IReadOnlyList<UsageNeedAdminRow> Items { get; init; } = [];
}

public sealed class UsageNeedFormViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(80, MinimumLength = 2)]
    [RegularExpression("^[A-Za-z][A-Za-z0-9_]*$", ErrorMessage = "Mã chỉ gồm chữ, số, dấu gạch dưới.")]
    [Display(Name = "Mã nhu cầu")]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(160)]
    [Display(Name = "Tên nhu cầu")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;
}

public sealed class RecommendationRuleAdminPageViewModel
{
    public IReadOnlyList<RecommendationRuleAdminRow> Items { get; init; } = [];
    public int? UsageNeedId { get; init; }
    public int? CategoryId { get; init; }
    public bool? IsActive { get; init; }
    public IReadOnlyList<SelectListItem> UsageNeedOptions { get; init; } = [];
    public IReadOnlyList<SelectListItem> CategoryOptions { get; init; } = [];
}

public sealed class RecommendationSpecificationOption
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string? Unit { get; init; }
}

public sealed class RecommendationRuleFormViewModel
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn nhu cầu.")]
    [Display(Name = "Nhu cầu sử dụng")]
    public int UsageNeedId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    [Display(Name = "Danh mục")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn thông số kỹ thuật.")]
    [Display(Name = "Thông số kỹ thuật")]
    public int SpecificationId { get; set; }

    [Required, StringLength(30)]
    [Display(Name = "Toán tử")]
    public string Operator { get; set; } = "GTE";

    [Required, StringLength(300)]
    [Display(Name = "Ngưỡng")]
    public string ThresholdValue { get; set; } = string.Empty;

    [InvariantDecimalRange("0", "99999.9999", ErrorMessage = "Trọng số phải từ 0 đến 99.999,9999.")]
    [Display(Name = "Trọng số")]
    public decimal Weight { get; set; } = 1m;

    [Display(Name = "Điều kiện bắt buộc")]
    public bool IsRequired { get; set; }

    [Display(Name = "Đang hoạt động")]
    public bool IsActive { get; set; } = true;

    public IReadOnlyList<SelectListItem> UsageNeedOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> CategoryOptions { get; set; } = [];
    public IReadOnlyList<RecommendationSpecificationOption> SpecificationOptions { get; set; } = [];
    public IReadOnlyList<SelectListItem> OperatorOptions { get; set; } = [];
}

public sealed class InvariantDecimalRangeAttribute : ValidationAttribute
{
    private readonly decimal _minimum;
    private readonly decimal _maximum;

    public InvariantDecimalRangeAttribute(string minimum, string maximum)
    {
        if (!decimal.TryParse(minimum, NumberStyles.Number, CultureInfo.InvariantCulture, out _minimum) ||
            !decimal.TryParse(maximum, NumberStyles.Number, CultureInfo.InvariantCulture, out _maximum) ||
            _minimum > _maximum)
        {
            throw new ArgumentException("The decimal range must use invariant formatting and an ordered range.");
        }
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;

        var candidate = value switch
        {
            decimal decimalValue => decimalValue,
            _ when decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => (decimal?)null
        };

        return candidate is decimal number && number >= _minimum && number <= _maximum
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
    }
}
