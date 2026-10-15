namespace NEXORA.Domain.Entities;

public static class ProductComparisonPolicy
{
    public static string? ValidateSelection(IReadOnlyCollection<int> productIds)
    {
        if (productIds.Count is < 2 or > 3)
        {
            return "Chọn từ 2 đến 3 sản phẩm để so sánh.";
        }

        if (productIds.Any(id => id <= 0) || productIds.Count != productIds.Distinct().Count())
        {
            return "Danh sách sản phẩm so sánh không hợp lệ hoặc có mã bị lặp.";
        }

        return null;
    }

    public static string? ValidateCategoryCompatibility(IReadOnlyCollection<int> categoryIds) =>
        categoryIds.Distinct().Count() <= 1
            ? null
            : "Hãy so sánh các sản phẩm cùng danh mục để đối chiếu đúng bộ thông số.";

    public static bool AreSpecificationValuesDifferent(IReadOnlyList<string?> values)
    {
        var presentValues = values.Where(value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return presentValues > 1 || (presentValues > 0 && values.Any(value => value is null));
    }
}
