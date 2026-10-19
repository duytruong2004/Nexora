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
    int ProductCount);

public sealed record OrderDetailLine(
    int ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal TotalPrice);

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
    decimal TotalAmount,
    IReadOnlyList<OrderDetailLine> Lines)
{
    public bool CanCancel => Status == "PENDING";
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
}
