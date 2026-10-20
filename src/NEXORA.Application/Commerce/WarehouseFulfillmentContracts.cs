using NEXORA.Domain.Entities;

namespace NEXORA.Application.Commerce;

public sealed record WarehouseQueueFilter(string? Search = null, int Page = 1, int PageSize = 25);

public sealed record WarehouseQueueItem(
    int OrderId,
    DateTime OrderDate,
    int LineCount,
    int TotalQuantity,
    string? CancellationRequestStatus,
    DateTime? CancellationRequestedAt);

public sealed record WarehouseQueuePage(
    IReadOnlyList<WarehouseQueueItem> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record WarehouseOrderLine(
    int ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    int? QuantityInStock,
    int? ReservedQuantity,
    int? ReservedForThisOrder,
    int? AvailableQuantity,
    bool CanFulfill);

public sealed record WarehouseOrderDetail(
    int OrderId,
    DateTime OrderDate,
    string Status,
    string RecipientName,
    string ShippingAddress,
    string? ShippingNote,
    decimal TotalAmount,
    string? CancellationRequestStatus,
    string? CancellationRequestReason,
    DateTime? CancellationRequestedAt,
    IReadOnlyList<WarehouseOrderLine> Lines,
    bool CanExport,
    string? ExportBlockedReason);

public enum WarehouseExportFailure
{
    None,
    AlreadyExported,
    NotFound,
    InvalidRequest,
    InvalidState,
    CancellationPending,
    InvalidOrderDetails,
    InventoryMissing,
    InsufficientReservation,
    ConcurrencyConflict,
    CommitResultUnknown,
    PersistenceFailure
}

public sealed record WarehouseExportResult(WarehouseExportFailure Failure)
{
    public bool Succeeded => Failure == WarehouseExportFailure.None;
}

public interface IWarehouseFulfillmentService
{
    Task<WarehouseQueuePage> GetQueueAsync(
        WarehouseQueueFilter filter,
        CancellationToken cancellationToken = default);

    Task<WarehouseOrderDetail?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<WarehouseExportResult> ExportAsync(
        int orderId,
        int warehouseUserId,
        CancellationToken cancellationToken = default);
}
