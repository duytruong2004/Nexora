using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class WarehouseFulfillmentService(
    AppDbContext db,
    ILogger<WarehouseFulfillmentService> logger) : IWarehouseFulfillmentService
{
    private const int MaximumPageSize = 50;

    public async Task<WarehouseQueuePage> GetQueueAsync(
        WarehouseQueueFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var requestedPage = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        if (search is { Length: > 100 }) search = search[..100];

        IQueryable<Order> query = db.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatuses.Preparing);
        if (search is not null)
        {
            if (int.TryParse(search, out var orderId))
            {
                query = query.Where(order => order.Id == orderId ||
                    order.Details.Any(line => line.ProductName.Contains(search) || line.Product.Sku.Contains(search)));
            }
            else
            {
                query = query.Where(order => order.Details.Any(line =>
                    line.ProductName.Contains(search) || line.Product.Sku.Contains(search)));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, pageCount);
        var items = await query
            .OrderBy(order => order.OrderDate)
            .ThenBy(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new WarehouseQueueItem(
                order.Id,
                order.OrderDate,
                order.Details.Count,
                order.Details.Sum(line => line.Quantity),
                order.CancelRequestStatus,
                order.CancelRequestedAt))
            .ToListAsync(cancellationToken);

        return new WarehouseQueuePage(items, totalCount, page, pageSize);
    }

    public async Task<WarehouseOrderDetail?> GetOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0) return null;

        var order = await db.Orders.AsNoTracking()
            .Include(item => item.Details)
                .ThenInclude(line => line.Product)
            .SingleOrDefaultAsync(item => item.Id == orderId &&
                (item.Status == OrderStatuses.Preparing || item.Status == OrderStatuses.Exported), cancellationToken);
        if (order is null) return null;

        var productIds = order.Details.Select(line => line.ProductId).Distinct().Order().ToArray();
        var inventories = await db.Inventories.AsNoTracking()
            .Where(inventory => productIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(inventory => inventory.ProductId, cancellationToken);
        var ledger = await db.InventoryTransactions.AsNoTracking()
            .Where(item => item.OrderId == orderId && productIds.Contains(item.ProductId))
            .ToListAsync(cancellationToken);

        Dictionary<int, int> requiredByProduct;
        Dictionary<int, long> reservedByProduct;
        WarehouseExportFailure validationFailure;
        try
        {
            requiredByProduct = GetRequiredQuantities(order.Details);
            reservedByProduct = GetOrderReservationBalances(ledger);
            validationFailure = ValidateExportData(
                order.Status,
                order.CancelRequestStatus,
                order.TotalAmount,
                order.Details.ToArray(),
                inventories,
                reservedByProduct);
        }
        catch (OverflowException)
        {
            requiredByProduct = [];
            reservedByProduct = [];
            validationFailure = WarehouseExportFailure.InvalidOrderDetails;
        }
        var lines = order.Details.OrderBy(line => line.ProductId).ThenBy(line => line.Id)
            .Select(line =>
            {
                inventories.TryGetValue(line.ProductId, out var inventory);
                var required = requiredByProduct.GetValueOrDefault(line.ProductId);
                var ownReserved = reservedByProduct.GetValueOrDefault(line.ProductId);
                return new WarehouseOrderLine(
                    line.ProductId,
                    line.Product.Sku,
                    line.ProductName,
                    line.Quantity,
                    inventory?.QuantityInStock,
                    inventory?.ReservedQuantity,
                    ownReserved is >= int.MinValue and <= int.MaxValue ? (int)ownReserved : null,
                    inventory?.AvailableQuantity,
                    line.Quantity > 0 && inventory is not null &&
                    inventory.QuantityInStock >= required && inventory.ReservedQuantity >= required &&
                    ownReserved == required && inventory.AvailableQuantity >= 0);
            })
            .ToArray();

        return new WarehouseOrderDetail(
            order.Id,
            order.OrderDate,
            order.Status,
            order.CustomerName,
            order.ShippingAddress,
            order.Note,
            order.TotalAmount,
            order.CancelRequestStatus,
            order.CancelRequestReason,
            order.CancelRequestedAt,
            lines,
            validationFailure == WarehouseExportFailure.None,
            GetBlockedReason(validationFailure));
    }

    public async Task<WarehouseExportResult> ExportAsync(
        int orderId,
        int warehouseUserId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || warehouseUserId <= 0)
            return new WarehouseExportResult(WarehouseExportFailure.InvalidRequest);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                try
                {
                    // Claim the order row first. Customer request, cancellation review and export
                    // serialize on this conditional transition before any inventory row is locked.
                    var claimed = await db.Orders
                        .Where(order => order.Id == orderId &&
                            order.Status == OrderStatuses.Preparing &&
                            (order.CancelRequestStatus == null ||
                             order.CancelRequestStatus == OrderCancellationRequestStatuses.Rejected))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(order => order.Status, OrderStatuses.Exported), cancellationToken);

                    if (claimed != 1)
                    {
                        var current = await db.Orders.AsNoTracking()
                            .Where(order => order.Id == orderId)
                            .Select(order => new { order.Status, order.CancelRequestStatus })
                            .SingleOrDefaultAsync(cancellationToken);
                        await transaction.RollbackAsync(cancellationToken);
                        if (current is null) return new WarehouseExportResult(WarehouseExportFailure.NotFound);
                        if (current.Status == OrderStatuses.Exported)
                        {
                            var verified = await HasCompleteExportLedgerAsync(orderId, cancellationToken);
                            return new WarehouseExportResult(verified
                                ? WarehouseExportFailure.AlreadyExported
                                : WarehouseExportFailure.CommitResultUnknown);
                        }
                        if (OrderCancellationRequestStatuses.BlocksWarehouseExport(current.CancelRequestStatus))
                            return new WarehouseExportResult(WarehouseExportFailure.CancellationPending);
                        if (current.CancelRequestStatus == OrderCancellationRequestStatuses.Approved)
                            return new WarehouseExportResult(WarehouseExportFailure.InvalidState);
                        return new WarehouseExportResult(WarehouseExportFailure.InvalidState);
                    }

                    var order = await db.Orders.AsNoTracking()
                        .Where(item => item.Id == orderId)
                        .Select(item => new { item.TotalAmount })
                        .SingleAsync(cancellationToken);
                    var lines = await db.OrderDetails.AsNoTracking()
                        .Include(line => line.Product)
                        .Where(line => line.OrderId == orderId)
                        .OrderBy(line => line.ProductId)
                        .ThenBy(line => line.Id)
                        .ToListAsync(cancellationToken);
                    if (ValidateOrderLines(order.TotalAmount, lines) != WarehouseExportFailure.None)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new WarehouseExportResult(WarehouseExportFailure.InvalidOrderDetails);
                    }

                    var requiredByProduct = GetRequiredQuantities(lines);
                    if (requiredByProduct.Count == 0)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new WarehouseExportResult(WarehouseExportFailure.InvalidOrderDetails);
                    }

                    var ledger = await db.InventoryTransactions.AsNoTracking()
                        .Where(item => item.OrderId == orderId)
                        .ToListAsync(cancellationToken);
                    if (ledger.Any(item => item.TransactionType is not ("RESERVE" or "RELEASE" or "OUT")))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new WarehouseExportResult(WarehouseExportFailure.InvalidOrderDetails);
                    }

                    var reservedByProduct = GetOrderReservationBalances(ledger);
                    if (ledger.Any(item => item.TransactionType == "OUT"))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new WarehouseExportResult(WarehouseExportFailure.CommitResultUnknown);
                    }

                    var inventories = new Dictionary<int, Inventory>();
                    foreach (var productId in requiredByProduct.Keys.Order())
                    {
                        var inventory = await InventoryRowLocking.LoadForUpdateAsync(db, productId, cancellationToken);
                        if (inventory is null)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseExportResult(WarehouseExportFailure.InventoryMissing);
                        }

                        var required = requiredByProduct[productId];
                        if (required <= 0 || inventory.QuantityInStock < required ||
                            inventory.ReservedQuantity < required || inventory.AvailableQuantity < 0 ||
                            reservedByProduct.GetValueOrDefault(productId) != required)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return new WarehouseExportResult(WarehouseExportFailure.InsufficientReservation);
                        }

                        inventories.Add(productId, inventory);
                        inventory.ExportReserved(required);
                        db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            ProductId = productId,
                            UserId = warehouseUserId,
                            OrderId = orderId,
                            TransactionType = "OUT",
                            Quantity = required,
                            Note = $"Xuất kho toàn bộ đơn hàng #{orderId}.",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    logger.LogInformation(
                        "Warehouse exported order {OrderId} with {ProductCount} products by user {WarehouseUserId}.",
                        orderId,
                        requiredByProduct.Count,
                        warehouseUserId);
                    return new WarehouseExportResult(WarehouseExportFailure.None);
                }
                catch
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); }
                    catch { /* Keep the original failure; transaction disposal is the final rollback boundary. */ }
                    throw;
                }
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning("Warehouse export inventory rowversion conflict for order {OrderId}.", orderId);
                return new WarehouseExportResult(WarehouseExportFailure.ConcurrencyConflict);
            }
            catch (Exception exception) when (FindSqlException(exception)?.Number == 1205)
            {
                // SQL Server confirms a deadlock victim's transaction was rolled back. Verify
                // order/ledger before a bounded whole-transaction retry; never retry an unknown commit.
                db.ChangeTracker.Clear();
                var resolved = await ResolveFailedAttemptAsync(orderId, cancellationToken);
                if (resolved.Failure != WarehouseExportFailure.PersistenceFailure) return resolved;
                logger.LogWarning("Warehouse export deadlock for order {OrderId}; retry {Attempt} of 3.", orderId, attempt);
                if (attempt == 3) return new WarehouseExportResult(WarehouseExportFailure.ConcurrencyConflict);
                await Task.Delay(TimeSpan.FromMilliseconds(40 * attempt), cancellationToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or SqlException or
                                               RetryLimitExceededException or InvalidOperationException or OverflowException)
            {
                db.ChangeTracker.Clear();
                logger.LogError(
                    "Warehouse export persistence failed for order {OrderId} ({ExceptionType}, SQL error {SqlErrorNumber}).",
                    orderId,
                    exception.GetType().Name,
                    FindSqlException(exception)?.Number);
                return await ResolveFailedAttemptAsync(orderId, cancellationToken);
            }
        }

        return new WarehouseExportResult(WarehouseExportFailure.ConcurrencyConflict);
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException) return sqlException;
        }
        return null;
    }

    private async Task<WarehouseExportResult> ResolveFailedAttemptAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            db.ChangeTracker.Clear();
            var state = await db.Orders.AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => new { order.Status, order.CancelRequestStatus })
                .SingleOrDefaultAsync(cancellationToken);
            if (state is null) return new WarehouseExportResult(WarehouseExportFailure.NotFound);
            if (state.Status == OrderStatuses.Exported)
            {
                return new WarehouseExportResult(await HasCompleteExportLedgerAsync(orderId, cancellationToken)
                    ? WarehouseExportFailure.AlreadyExported
                    : WarehouseExportFailure.CommitResultUnknown);
            }

            if (OrderCancellationRequestStatuses.BlocksWarehouseExport(state.CancelRequestStatus))
                return new WarehouseExportResult(WarehouseExportFailure.CancellationPending);
            if (state.Status != OrderStatuses.Preparing)
                return new WarehouseExportResult(WarehouseExportFailure.InvalidState);

            var ledgerCount = await db.InventoryTransactions.AsNoTracking()
                .CountAsync(item => item.OrderId == orderId && item.TransactionType == "OUT", cancellationToken);
            return new WarehouseExportResult(ledgerCount == 0
                ? WarehouseExportFailure.PersistenceFailure
                : WarehouseExportFailure.CommitResultUnknown);
        }
        catch (Exception verificationException) when (verificationException is DbException or InvalidOperationException or
                                                       RetryLimitExceededException)
        {
            logger.LogError(
                "Unable to verify warehouse export commit outcome for order {OrderId} ({ExceptionType}).",
                orderId,
                verificationException.GetType().Name);
            return new WarehouseExportResult(WarehouseExportFailure.CommitResultUnknown);
        }
    }

    private async Task<bool> HasCompleteExportLedgerAsync(int orderId, CancellationToken cancellationToken)
    {
        var expected = await db.OrderDetails.AsNoTracking()
            .Where(line => line.OrderId == orderId)
            .GroupBy(line => line.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(line => (long)line.Quantity) })
            .ToListAsync(cancellationToken);
        if (expected.Count == 0) return false;

        var actual = await db.InventoryTransactions.AsNoTracking()
            .Where(item => item.OrderId == orderId && item.TransactionType == "OUT")
            .GroupBy(item => item.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Quantity = group.Sum(item => (long)item.Quantity),
                EntryCount = group.Count()
            })
            .ToListAsync(cancellationToken);
        if (actual.Count != expected.Count) return false;

        var actualByProduct = actual.ToDictionary(item => item.ProductId);
        return expected.All(item => actualByProduct.TryGetValue(item.ProductId, out var entry) &&
            entry.EntryCount == 1 && entry.Quantity == item.Quantity);
    }

    private static WarehouseExportFailure ValidateExportData(
        string status,
        string? cancellationStatus,
        decimal totalAmount,
        IReadOnlyCollection<OrderDetail> lines,
        IReadOnlyDictionary<int, Inventory> inventories,
        IReadOnlyDictionary<int, long> reservedByProduct)
    {
        if (!OrderCancellationRules.CanExport(status, cancellationStatus))
            return OrderCancellationRequestStatuses.BlocksWarehouseExport(cancellationStatus)
                ? WarehouseExportFailure.CancellationPending
                : WarehouseExportFailure.InvalidState;

        var lineFailure = ValidateOrderLines(totalAmount, lines);
        if (lineFailure != WarehouseExportFailure.None) return lineFailure;

        Dictionary<int, int> requiredByProduct;
        try { requiredByProduct = GetRequiredQuantities(lines); }
        catch (OverflowException) { return WarehouseExportFailure.InvalidOrderDetails; }

        foreach (var (productId, required) in requiredByProduct)
        {
            if (!inventories.TryGetValue(productId, out var inventory))
                return WarehouseExportFailure.InventoryMissing;
            if (inventory.QuantityInStock < required || inventory.ReservedQuantity < required ||
                inventory.AvailableQuantity < 0 || reservedByProduct.GetValueOrDefault(productId) != required)
                return WarehouseExportFailure.InsufficientReservation;
        }

        return WarehouseExportFailure.None;
    }

    private static WarehouseExportFailure ValidateOrderLines(
        decimal totalAmount,
        IReadOnlyCollection<OrderDetail> lines)
    {
        if (lines.Count == 0 || totalAmount < 0) return WarehouseExportFailure.InvalidOrderDetails;
        try
        {
            decimal sum = 0m;
            foreach (var line in lines)
            {
                if (line.ProductId <= 0 || line.Quantity <= 0 || line.Product is null ||
                    string.IsNullOrWhiteSpace(line.Product.Sku) || line.UnitPrice < 0 ||
                    line.DiscountAmount < 0 || line.TotalPrice < 0)
                    return WarehouseExportFailure.InvalidOrderDetails;

                var gross = checked(line.UnitPrice * line.Quantity);
                if (line.DiscountAmount > gross || line.TotalPrice != gross - line.DiscountAmount)
                    return WarehouseExportFailure.InvalidOrderDetails;
                sum = checked(sum + line.TotalPrice);
            }

            return sum == totalAmount ? WarehouseExportFailure.None : WarehouseExportFailure.InvalidOrderDetails;
        }
        catch (OverflowException)
        {
            return WarehouseExportFailure.InvalidOrderDetails;
        }
    }

    private static Dictionary<int, int> GetRequiredQuantities(IEnumerable<OrderDetail> lines)
    {
        var grouped = new Dictionary<int, long>();
        foreach (var line in lines)
        {
            grouped[line.ProductId] = checked(grouped.GetValueOrDefault(line.ProductId) + line.Quantity);
        }

        var result = new Dictionary<int, int>();
        foreach (var (productId, quantity) in grouped)
        {
            if (productId <= 0 || quantity <= 0 || quantity > int.MaxValue)
                throw new OverflowException("Invalid aggregate order quantity.");
            result.Add(productId, (int)quantity);
        }

        return result;
    }

    private static Dictionary<int, long> GetOrderReservationBalances(IEnumerable<InventoryTransaction> transactions)
    {
        var balances = new Dictionary<int, long>();
        foreach (var transaction in transactions)
        {
            var delta = transaction.TransactionType switch
            {
                "RESERVE" => transaction.Quantity,
                "RELEASE" or "OUT" => -(long)transaction.Quantity,
                _ => 0L
            };
            balances[transaction.ProductId] = checked(balances.GetValueOrDefault(transaction.ProductId) + delta);
        }

        return balances;
    }

    private static string? GetBlockedReason(WarehouseExportFailure failure) => failure switch
    {
        WarehouseExportFailure.None => null,
        WarehouseExportFailure.CancellationPending => "Đơn đang có yêu cầu hủy chờ xử lý. Kho có thể tiếp tục chuẩn bị nhưng chưa được xuất.",
        WarehouseExportFailure.InvalidState => "Chỉ đơn PREPARING mới được xuất kho.",
        WarehouseExportFailure.InvalidOrderDetails => "Dữ liệu chi tiết đơn không hợp lệ hoặc không khớp tổng tiền.",
        WarehouseExportFailure.InventoryMissing => "Một hoặc nhiều sản phẩm chưa có bản ghi tồn kho.",
        WarehouseExportFailure.InsufficientReservation => "Tồn kho hoặc lượng giữ cho đơn không đủ để xuất toàn bộ.",
        WarehouseExportFailure.CommitResultUnknown => "Không xác minh được đầy đủ kết quả xuất kho. Hãy tải lại đơn trước khi thao tác tiếp.",
        _ => "Không thể xuất kho ở thời điểm hiện tại. Hãy tải lại để kiểm tra trạng thái mới."
    };
}

internal static class InventoryRowLocking
{
    public static async Task<Inventory?> LoadForUpdateAsync(
        AppDbContext db,
        int productId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Inventories
            .FromSqlInterpolated($"SELECT * FROM [inventories] WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE [product_id] = {productId}")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
}
