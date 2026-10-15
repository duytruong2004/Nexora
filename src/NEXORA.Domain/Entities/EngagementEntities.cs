namespace NEXORA.Domain.Entities;

public sealed class Favorite
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Product Product { get; set; } = null!;
}

public sealed class Review
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ProductId { get; set; }
    public int OrderId { get; set; }
    public int OrderDetailId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string Status { get; set; } = "PENDING";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Product Product { get; set; } = null!;
    public OrderDetail OrderDetail { get; set; } = null!;
}

public static class ReviewEligibilityPolicy
{
    public static string? Validate(Review review, Order order, OrderDetail detail)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(detail);

        if (order.UserId != review.UserId)
        {
            return "Only the order owner can review an order detail.";
        }

        if (detail.OrderId != order.Id || detail.ProductId != review.ProductId ||
            detail.Id != review.OrderDetailId || review.OrderId != order.Id)
        {
            return "The review product and order detail must belong to the same order.";
        }

        return order.Status == OrderStatuses.Completed
            ? null
            : "A product can only be reviewed after its order is completed.";
    }
}

public static class PromotionDiscountTypes
{
    public const string Percentage = "PERCENTAGE";
    public const string FixedAmount = "FIXED_AMOUNT";
}

public sealed class Promotion
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; }
    public ICollection<PromotionProduct> Products { get; set; } = new List<PromotionProduct>();

    public bool IsApplicableAt(DateTime utcNow) =>
        IsActive && utcNow >= StartDate && utcNow <= EndDate;
}

public sealed class PromotionProduct
{
    public int PromotionId { get; set; }
    public int ProductId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
