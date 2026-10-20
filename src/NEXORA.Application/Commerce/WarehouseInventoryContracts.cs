namespace NEXORA.Application.Commerce;

public sealed record WarehouseInventoryFilter(
    string? Search = null,
    bool OutOfStockOnly = false,
    int Page = 1,
    int PageSize = 25);

public sealed record WarehouseInventoryRow(
    int ProductId,
    string Sku,
    string ProductName,
    string CategoryName,
    int? QuantityInStock,
    int? ReservedQuantity,
    int? AvailableQuantity)
{
    public bool HasInventory => QuantityInStock.HasValue && ReservedQuantity.HasValue && AvailableQuantity.HasValue;
}

public sealed record WarehouseInventoryPage(
    IReadOnlyList<WarehouseInventoryRow> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record WarehouseInventoryProductOption(int ProductId, string Sku, string ProductName);

public sealed record WarehouseInventoryHistoryFilter(
    int? ProductId = null,
    string? TransactionType = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 25);

public sealed record WarehouseInventoryHistoryRow(
    int TransactionId,
    DateTime CreatedAt,
    int ProductId,
    string Sku,
    string ProductName,
    string TransactionType,
    int Quantity,
    string Direction,
    string ActorName,
    int? OrderId,
    string? Note);

public sealed record WarehouseInventoryHistoryPage(
    IReadOnlyList<WarehouseInventoryHistoryRow> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record WarehouseStockReceiptInput(
    Guid RequestId,
    int ProductId,
    int Quantity,
    string? Reference,
    string? Note);

public sealed record WarehouseStockAdjustmentInput(
    Guid RequestId,
    int ProductId,
    string? Direction,
    int Quantity,
    string? Reason,
    string? Note);

public enum WarehouseInventoryOperationFailure
{
    None,
    AlreadyApplied,
    InvalidRequest,
    ProductNotFound,
    InventoryMissing,
    BelowReservedQuantity,
    IdempotencyConflict,
    ConcurrencyConflict,
    CommitOutcomeUnknown,
    PersistenceFailure
}

public sealed record WarehouseInventoryOperationResult(
    WarehouseInventoryOperationFailure Failure,
    int? QuantityInStock = null,
    int? ReservedQuantity = null,
    int? AvailableQuantity = null)
{
    public bool Succeeded => Failure is WarehouseInventoryOperationFailure.None or WarehouseInventoryOperationFailure.AlreadyApplied;
}

public interface IWarehouseInventoryManagementService
{
    Task<WarehouseInventoryPage> SearchAsync(
        WarehouseInventoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WarehouseInventoryProductOption>> GetProductOptionsAsync(
        CancellationToken cancellationToken = default);

    Task<WarehouseInventoryHistoryPage> GetHistoryAsync(
        WarehouseInventoryHistoryFilter filter,
        CancellationToken cancellationToken = default);

    Task<WarehouseInventoryOperationResult> ReceiveAsync(
        WarehouseStockReceiptInput input,
        int warehouseUserId,
        CancellationToken cancellationToken = default);

    Task<WarehouseInventoryOperationResult> AdjustAsync(
        WarehouseStockAdjustmentInput input,
        int warehouseUserId,
        CancellationToken cancellationToken = default);
}
