using System.Globalization;

namespace NEXORA.Domain.Entities;

public sealed record NormalizedSpecificationValue(string Value, decimal? NumericValue, string? DisplayValue);

public static class SpecificationValuePolicy
{
    private const decimal MaximumNumericValue = 99_999_999_999_999.9999m;

    public static string? TryNormalize(
        string dataType,
        string? value,
        string? displayValue,
        string? unit,
        out NormalizedSpecificationValue? normalized)
    {
        normalized = null;
        var rawValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return "Giá trị thông số không được để trống.";
        }

        if (rawValue.Length > 1000)
        {
            return "Giá trị lưu trữ không được vượt quá 1.000 ký tự.";
        }

        var display = string.IsNullOrWhiteSpace(displayValue) ? null : displayValue.Trim();
        if (display?.Length > 300)
        {
            return "Giá trị hiển thị không được vượt quá 300 ký tự.";
        }

        var type = dataType.Trim().ToUpperInvariant();
        if (type == SpecificationDataTypes.Number)
        {
            if (!decimal.TryParse(rawValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var numericValue))
            {
                return "Giá trị NUMBER phải là số thập phân dùng dấu chấm theo định dạng bất biến.";
            }

            if (numericValue < -MaximumNumericValue || numericValue > MaximumNumericValue ||
                decimal.Round(numericValue, 4) != numericValue)
            {
                return "Giá trị NUMBER vượt giới hạn decimal(18,4) hoặc có quá 4 chữ số thập phân.";
            }

            var canonical = numericValue.ToString("0.####", CultureInfo.InvariantCulture);
            var displayNumber = display ?? (string.IsNullOrWhiteSpace(unit) ? canonical : $"{canonical} {unit.Trim()}");
            normalized = new NormalizedSpecificationValue(canonical, numericValue, displayNumber);
            return null;
        }

        if (type == SpecificationDataTypes.Boolean)
        {
            if (!bool.TryParse(rawValue, out var booleanValue))
            {
                return "Giá trị BOOLEAN chỉ nhận true hoặc false.";
            }

            var canonical = booleanValue ? "true" : "false";
            normalized = new NormalizedSpecificationValue(canonical, null, display ?? (booleanValue ? "Có" : "Không"));
            return null;
        }

        if (type is SpecificationDataTypes.Text or SpecificationDataTypes.Enumeration)
        {
            normalized = new NormalizedSpecificationValue(rawValue, null, display ?? rawValue);
            return null;
        }

        return "Kiểu dữ liệu Specification không được hỗ trợ.";
    }
}

public static class SpecificationAssignmentPolicy
{
    public static string? ValidateSubmittedIds(
        IReadOnlyCollection<int> assignedSpecificationIds,
        IReadOnlyCollection<int> submittedSpecificationIds)
    {
        if (submittedSpecificationIds.Count != submittedSpecificationIds.Distinct().Count())
        {
            return "Không được nhập hai giá trị cho cùng một thông số.";
        }

        if (submittedSpecificationIds.Any(id => !assignedSpecificationIds.Contains(id)))
        {
            return "Sản phẩm chỉ nhận thông số được gán cho danh mục hiện tại.";
        }

        return null;
    }
}
