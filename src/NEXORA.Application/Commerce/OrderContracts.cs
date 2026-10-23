using NEXORA.Domain.Entities;

namespace NEXORA.Application.Commerce;

public sealed record CheckoutQuoteLine(
    int CartItemId,
    int ProductId,
    string ProductName,
    string Sku,
    string? ImageUrl,
    decimal UnitPrice,
    decimal DiscountAmount,
    string? PromotionName,
    int Quantity,
    int AvailableQuantity,
    bool IsProductActive)
{
    public decimal GrossAmount => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
    public decimal TotalPrice => Math.Max(0m, GrossAmount - DiscountAmount);
    public bool IsAvailable => IsProductActive && Quantity <= AvailableQuantity;
}

public sealed record CheckoutQuote(
    IReadOnlyList<CheckoutQuoteLine> Lines,
    DateTime CreatedAtUtc,
    string Fingerprint)
{
    public decimal Subtotal => decimal.Round(Lines.Sum(line => line.GrossAmount), 2, MidpointRounding.AwayFromZero);
    public decimal PromotionDiscount => decimal.Round(Lines.Sum(line => line.DiscountAmount), 2, MidpointRounding.AwayFromZero);
    public decimal Total => decimal.Round(Lines.Sum(line => line.TotalPrice), 2, MidpointRounding.AwayFromZero);
    public int ItemCount => Lines.Sum(line => line.Quantity);
    public bool IsEmpty => Lines.Count == 0;
    public bool CanCheckout => !IsEmpty && Lines.All(line => line.IsAvailable);
}

public sealed record PlaceOrderInput(
    Guid RequestId,
    string CustomerName,
    string Phone,
    string ShippingAddress,
    string? Note,
    string ExpectedQuoteFingerprint);

public enum PlaceOrderFailure
{
    None,
    CartEmpty,
    ProductUnavailable,
    InsufficientStock,
    QuoteChanged,
    InvalidRequest,
    TransientFailure,
    CommitResultUnknown,
    PersistenceFailure
}

public sealed record PlaceOrderResult(PlaceOrderFailure Failure, int? OrderId = null, CheckoutQuote? CurrentQuote = null)
{
    public bool Succeeded => Failure == PlaceOrderFailure.None && OrderId is not null;
}

public sealed record OrderListItem(
    int Id,
    DateTime OrderDate,
    string Status,
    decimal TotalAmount,
    int ProductCount)
{
    public string? CancelRequestStatus { get; init; }
}

public sealed record OrderDetailLine(
    int ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TotalPrice)
{
    public string? ImageUrl { get; init; }
}

public sealed record OrderDetailView(
    int Id,
    DateTime OrderDate,
    string Status,
    string CustomerName,
    string Phone,
    string ShippingAddress,
    string? Note,
    string? CancelReason,
    DateTime? CancelledAt,
    string? CancelRequestStatus,
    string? CancelRequestReason,
    DateTime? CancelRequestedAt,
    DateTime? CancelReviewedAt,
    string? CancelDecisionNote,
    decimal TotalAmount,
    IReadOnlyList<OrderDetailLine> Lines)
{
    public bool CanCancel => Status == "PENDING";
    public bool CanRequestCancellation =>
        (Status is "CONFIRMED" or "PREPARING") && CancelRequestStatus is null;
}

public enum CancelOrderFailure
{
    None,
    NotFound,
    CannotCancel,
    ConcurrencyConflict
}

public sealed record CancelOrderResult(CancelOrderFailure Failure)
{
    public bool Succeeded => Failure == CancelOrderFailure.None;
}

public enum CustomerCancellationFailure
{
    None,
    AlreadyApplied,
    NotFound,
    InvalidState,
    AlreadyRequested,
    DuplicateRequestId,
    InvalidRequest,
    PersistenceFailure
}

public sealed record CustomerCancellationResult(
    CustomerCancellationFailure Failure,
    string? RequestStatus = null,
    string? OrderStatus = null)
{
    public bool Succeeded => Failure is CustomerCancellationFailure.None or CustomerCancellationFailure.AlreadyApplied;
}

public enum CancellationReviewFailure
{
    None,
    AlreadyReviewed,
    NotFound,
    InvalidState,
    InvalidDecisionNote,
    ConcurrencyConflict,
    PersistenceFailure
}

public sealed record CancellationReviewResult(CancellationReviewFailure Failure, string? RequestStatus = null)
{
    public bool Succeeded => Failure == CancellationReviewFailure.None;
}

public sealed record StaffOrderFilter(
    string? Search,
    string? Status,
    int Page = 1,
    int PageSize = 25,
    string? CancelRequestStatus = null);

public sealed record StaffOrderListItem(
    int Id,
    DateTime OrderDate,
    string Status,
    string CustomerName,
    string Phone,
    decimal TotalAmount,
    int ItemCount)
{
    public string? CancelRequestStatus { get; init; }
    public DateTime? CancelRequestedAt { get; init; }
}

public sealed record StaffOrderPage(
    IReadOnlyList<StaffOrderListItem> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record StaffOrderDetailLine(
    int ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TotalPrice);

public sealed record StaffOrderDetail(
    int Id,
    int UserId,
    DateTime OrderDate,
    string Status,
    string CustomerName,
    string Phone,
    string ShippingAddress,
    string? Note,
    int? ConfirmedBy,
    DateTime? ConfirmedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    DateTime? CompletedAt,
    decimal TotalAmount,
    IReadOnlyList<StaffOrderDetailLine> Lines)
{
    public string? CancelRequestStatus { get; init; }
    public string? CancelRequestReason { get; init; }
    public Guid? CancelRequestId { get; init; }
    public DateTime? CancelRequestedAt { get; init; }
    public int? CancelReviewedBy { get; init; }
    public DateTime? CancelReviewedAt { get; init; }
    public string? CancelDecisionNote { get; init; }
}

public enum OrderTransitionFailure
{
    None,
    AlreadyApplied,
    NotFound,
    InvalidState
}

public sealed record OrderTransitionResult(OrderTransitionFailure Failure)
{
    public bool Succeeded => Failure is OrderTransitionFailure.None or OrderTransitionFailure.AlreadyApplied;
}

public interface ISalesOrderManagementService
{
    Task<StaffOrderPage> SearchAsync(StaffOrderFilter filter, CancellationToken cancellationToken = default);
    Task<StaffOrderDetail?> GetAsync(int orderId, CancellationToken cancellationToken = default);
    Task<OrderTransitionResult> TransitionAsync(
        int orderId,
        int actorUserId,
        OrderWorkflowAction action,
        CancellationToken cancellationToken = default);
}

public interface IOrderPricingService
{
    Task<CheckoutQuote> GetCurrentQuoteAsync(int userId, CancellationToken cancellationToken = default);
}

public interface ICheckoutService
{
    Task<PlaceOrderResult> PlaceOrderAsync(int userId, PlaceOrderInput input, CancellationToken cancellationToken = default);
}

public interface ICustomerOrderService
{
    Task<IReadOnlyList<OrderListItem>> GetOrdersAsync(int userId, CancellationToken cancellationToken = default);
    Task<OrderDetailView?> GetOrderAsync(int userId, int orderId, CancellationToken cancellationToken = default);
    Task<CancelOrderResult> CancelAsync(int userId, int orderId, string? reason, CancellationToken cancellationToken = default);
    Task<CustomerCancellationResult> RequestCancellationAsync(
        int userId,
        int orderId,
        Guid requestId,
        string? reason,
        CancellationToken cancellationToken = default);
}

public interface IOrderCancellationReviewService
{
    Task<CancellationReviewResult> ReviewAsync(
        int orderId,
        int reviewerUserId,
        bool approve,
        string? decisionNote,
        CancellationToken cancellationToken = default);
}
