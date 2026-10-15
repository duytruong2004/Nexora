namespace NEXORA.Domain.Entities;

public static class ProductStatuses
{
    public const string Active = "ACTIVE";
    public const string Inactive = "INACTIVE";
}

public static class ProductCatalogPolicy
{
    private const decimal MaximumStoredPrice = 9_999_999_999_999_999.99m;

    public static string? Validate(
        string? name,
        string? sku,
        decimal price,
        decimal? discountPrice,
        int warrantyMonths,
        string? status)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 240)
        {
            return "Tên sản phẩm bắt buộc và không được vượt quá 240 ký tự.";
        }

        if (string.IsNullOrWhiteSpace(sku) || sku.Trim().Length > 80)
        {
            return "Mã sản phẩm bắt buộc và không được vượt quá 80 ký tự.";
        }

        if (price < 0)
        {
            return "Giá bán không được âm.";
        }

        if (price > MaximumStoredPrice || decimal.Round(price, 2) != price)
        {
            return "Giá bán phải nằm trong giới hạn lưu trữ và có tối đa hai chữ số thập phân.";
        }

        if (discountPrice is < 0 || (discountPrice.HasValue && discountPrice > price))
        {
            return "Giá khuyến mại phải không âm và không vượt giá bán.";
        }

        if (discountPrice > MaximumStoredPrice ||
            (discountPrice.HasValue && decimal.Round(discountPrice.Value, 2) != discountPrice.Value))
        {
            return "Giá khuyến mại phải có tối đa hai chữ số thập phân và nằm trong giới hạn lưu trữ.";
        }

        if (warrantyMonths < 0)
        {
            return "Thời hạn bảo hành không được âm.";
        }

        if (status is not (ProductStatuses.Active or ProductStatuses.Inactive))
        {
            return "Trạng thái sản phẩm không hợp lệ.";
        }

        return null;
    }
}
