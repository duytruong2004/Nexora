namespace NEXORA.Domain.Entities;

public sealed class Cart
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public sealed class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Cart Cart { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

public static class OrderStatuses
{
    public const string Pending = "PENDING";
    public const string Confirmed = "CONFIRMED";
    public const string Preparing = "PREPARING";
    public const string Exported = "EXPORTED";
    public const string Shipping = "SHIPPING";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
}

public static class OrderCancellationRequestStatuses
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";

    public static bool IsKnown(string? status) => status is Pending or Approved or Rejected;

    public static bool BlocksWarehouseExport(string? status) => status == Pending;
}

public static class OrderCancellationRules
{
    public static bool CanRequest(string orderStatus) =>
        orderStatus is OrderStatuses.Confirmed or OrderStatuses.Preparing;

    public static bool CanReview(string orderStatus, string? requestStatus) =>
        requestStatus == OrderCancellationRequestStatuses.Pending &&
        orderStatus is OrderStatuses.Confirmed or OrderStatuses.Preparing;

    public static bool CanExport(string orderStatus, string? requestStatus) =>
        orderStatus == OrderStatuses.Preparing &&
        requestStatus is null or OrderCancellationRequestStatuses.Rejected;
}

public enum OrderWorkflowAction
{
    Confirm,
    HandoffToWarehouse,
    MarkShipping,
    Complete
}

public readonly record struct OrderWorkflowTransition(string FromStatus, string ToStatus);

public static class OrderWorkflowRules
{
    public static bool TryGetTransition(OrderWorkflowAction action, out OrderWorkflowTransition transition)
    {
        transition = action switch
        {
            OrderWorkflowAction.Confirm => new(OrderStatuses.Pending, OrderStatuses.Confirmed),
            OrderWorkflowAction.HandoffToWarehouse => new(OrderStatuses.Confirmed, OrderStatuses.Preparing),
            OrderWorkflowAction.MarkShipping => new(OrderStatuses.Exported, OrderStatuses.Shipping),
            OrderWorkflowAction.Complete => new(OrderStatuses.Shipping, OrderStatuses.Completed),
            _ => default
        };

        return action is OrderWorkflowAction.Confirm or
            OrderWorkflowAction.HandoffToWarehouse or
            OrderWorkflowAction.MarkShipping or
            OrderWorkflowAction.Complete;
    }
}

public sealed class Order
{
    public int Id { get; set; }
    public Guid? CheckoutRequestId { get; set; }
    public int UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = OrderStatuses.Pending;
    public string? Note { get; set; }
    public int? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancelRequestStatus { get; set; }
    public string? CancelRequestReason { get; set; }
    public Guid? CancelRequestId { get; set; }
    public DateTime? CancelRequestedAt { get; set; }
    public int? CancelReviewedBy { get; set; }
    public DateTime? CancelReviewedAt { get; set; }
    public string? CancelDecisionNote { get; set; }
    public ICollection<OrderDetail> Details { get; set; } = new List<OrderDetail>();
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}

public sealed class OrderDetail
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalPrice { get; set; }
    public Order Order { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
