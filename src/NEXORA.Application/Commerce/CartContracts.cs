namespace NEXORA.Application.Commerce;

public sealed record CartLine(
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
    bool IsAvailable)
{
    public decimal GrossAmount => decimal.Round(UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
    public decimal LineTotal => Math.Max(0m, GrossAmount - DiscountAmount);
}

public sealed record CartSnapshot(
    int? CartId,
    IReadOnlyList<CartLine> Items)
{
    public int ItemCount => Items.Sum(item => item.Quantity);
    public decimal Subtotal => decimal.Round(Items.Sum(item => item.GrossAmount), 2, MidpointRounding.AwayFromZero);
    public decimal PromotionDiscount => decimal.Round(Items.Sum(item => item.DiscountAmount), 2, MidpointRounding.AwayFromZero);
    public decimal Total => decimal.Round(Items.Sum(item => item.LineTotal), 2, MidpointRounding.AwayFromZero);
    public bool IsEmpty => Items.Count == 0;
    public bool CanCheckout => !IsEmpty && Items.All(item => item.IsAvailable && item.Quantity <= item.AvailableQuantity);
}

public enum CartMutationFailure
{
    None,
    InvalidQuantity,
    ProductUnavailable,
    OutOfStock,
    InsufficientStock,
    ItemNotFound,
    ConcurrentUpdate
}

public sealed record CartMutationResult(CartMutationFailure Failure)
{
    public bool Succeeded => Failure == CartMutationFailure.None;
    public static CartMutationResult Success { get; } = new(CartMutationFailure.None);
}

public interface ICartService
{
    Task<CartSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task<int> GetItemCountAsync(int userId, CancellationToken cancellationToken = default);
    Task<CartMutationResult> AddAsync(int userId, int productId, int quantity, CancellationToken cancellationToken = default);
    Task<CartMutationResult> SetQuantityAsync(int userId, int cartItemId, int quantity, CancellationToken cancellationToken = default);
    Task<CartMutationResult> RemoveAsync(int userId, int cartItemId, CancellationToken cancellationToken = default);
}
