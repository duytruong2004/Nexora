using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class CustomerOrderService(AppDbContext db, ILogger<CustomerOrderService> logger) : ICustomerOrderService
{
    public async Task<IReadOnlyList<OrderListItem>> GetOrdersAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        return await db.Orders.AsNoTracking()
            .Where(order => order.UserId == userId)
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new OrderListItem(
                order.Id,
                order.OrderDate,
                order.Status,
                order.TotalAmount,
                order.Details.Sum(line => line.Quantity))
            {
                CancelRequestStatus = order.CancelRequestStatus
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrderDetailView?> GetOrderAsync(
        int userId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        if (orderId <= 0)
        {
            return null;
        }

        var order = await db.Orders.AsNoTracking()
            .Include(item => item.Details)
            .Where(order => order.Id == orderId && order.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null) return null;

        return new OrderDetailView(
            order.Id,
            order.OrderDate,
            order.Status,
            order.CustomerName,
            order.Phone,
            order.ShippingAddress,
            order.Note,
            order.CancelReason,
            order.CancelledAt,
            order.CancelRequestStatus,
            order.CancelRequestReason,
            order.CancelRequestedAt,
            order.CancelReviewedAt,
            order.CancelDecisionNote,
            order.TotalAmount,
            order.Details.OrderBy(line => line.Id)
                .Select(line => new OrderDetailLine(
                    line.ProductId,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TotalPrice))
                .ToArray());
    }

    public async Task<CancelOrderResult> CancelAsync(
        int userId,
        int orderId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        if (orderId <= 0)
        {
            return new CancelOrderResult(CancelOrderFailure.NotFound);
        }

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            db.ChangeTracker.Clear();
            var strategy = db.Database.CreateExecutionStrategy();
            try
            {
                return await strategy.ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
                    var order = await db.Orders.AsNoTracking()
                        .Include(item => item.Details)
                        .Where(item => item.Id == orderId && item.UserId == userId)
                        .SingleOrDefaultAsync(cancellationToken);

                    if (order is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new CancelOrderResult(CancelOrderFailure.NotFound);
                    }

                    if (order.Status != OrderStatuses.Pending)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new CancelOrderResult(CancelOrderFailure.CannotCancel);
                    }

                    var now = DateTime.UtcNow;
                    var changed = await db.Orders
                        .Where(item => item.Id == orderId && item.UserId == userId && item.Status == OrderStatuses.Pending)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.Status, OrderStatuses.Cancelled)
                            .SetProperty(item => item.CancelledAt, now)
                            .SetProperty(item => item.CancelReason, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()),
                            cancellationToken);
                    if (changed != 1)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return new CancelOrderResult(CancelOrderFailure.CannotCancel);
                    }

                    var lines = order.Details.Select(line => new { line.ProductId, line.Quantity }).ToArray();
                    var inventoryByProduct = new Dictionary<int, Inventory>();
                    foreach (var productId in lines.Select(line => line.ProductId).Distinct().Order())
                    {
                        var inventory = await InventoryRowLocking.LoadForUpdateAsync(db, productId, cancellationToken);
                        if (inventory is not null) inventoryByProduct.Add(productId, inventory);
                    }
                    foreach (var line in lines)
                    {
                        if (!inventoryByProduct.TryGetValue(line.ProductId, out var inventory))
                        {
                            throw new InvalidOperationException("Reserved inventory is missing for an order line.");
                        }

                        inventory.ReleaseReservation(line.Quantity);
                        db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            UserId = userId,
                            OrderId = orderId,
                            TransactionType = "RELEASE",
                            Quantity = line.Quantity,
                            Note = "Giải phóng hàng khi khách hủy đơn PENDING.",
                            CreatedAt = now
                        });
                    }

                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new CancelOrderResult(CancelOrderFailure.None);
                });
            }
            catch (DbUpdateConcurrencyException) when (attempt < 3)
            {
                // Reload the order and inventory on the next attempt; a concurrent cancellation becomes CannotCancel.
            }
            catch (Exception exception) when (exception is DbUpdateConcurrencyException or DbUpdateException or InvalidOperationException)
            {
                logger.LogWarning(exception, "Order cancellation could not be completed atomically.");
                return new CancelOrderResult(CancelOrderFailure.ConcurrencyConflict);
            }
        }

        return new CancelOrderResult(CancelOrderFailure.ConcurrencyConflict);
    }

    public async Task<CustomerCancellationResult> RequestCancellationAsync(
        int userId,
        int orderId,
        Guid requestId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        EnsureValidUserId(userId);
        if (orderId <= 0 || requestId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
        {
            return new CustomerCancellationResult(CustomerCancellationFailure.InvalidRequest);
        }

        var normalizedReason = reason.Trim();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var strategy = db.Database.CreateExecutionStrategy();
                var result = await strategy.ExecuteAsync(async () =>
                {
                    var current = await db.Orders.AsNoTracking()
                        .Where(order => order.Id == orderId && order.UserId == userId)
                        .Select(order => new
                        {
                            order.Status,
                            order.CancelRequestStatus,
                            order.CancelRequestId
                        })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (current is null)
                    {
                        return new CustomerCancellationResult(CustomerCancellationFailure.NotFound);
                    }

                    if (current.CancelRequestId == requestId)
                    {
                        return new CustomerCancellationResult(
                            CustomerCancellationFailure.AlreadyApplied,
                            current.CancelRequestStatus,
                            current.Status);
                    }

                    if (current.CancelRequestId is not null || current.CancelRequestStatus is not null)
                    {
                        return new CustomerCancellationResult(
                            CustomerCancellationFailure.AlreadyRequested,
                            current.CancelRequestStatus,
                            current.Status);
                    }

                    if (!OrderCancellationRules.CanRequest(current.Status))
                    {
                        return new CustomerCancellationResult(
                            CustomerCancellationFailure.InvalidState,
                            current.CancelRequestStatus,
                            current.Status);
                    }

                    var now = DateTime.UtcNow;
                    var changed = await db.Orders
                        .Where(order => order.Id == orderId && order.UserId == userId &&
                            (order.Status == OrderStatuses.Confirmed || order.Status == OrderStatuses.Preparing) &&
                            order.CancelRequestId == null && order.CancelRequestStatus == null)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(order => order.CancelRequestStatus, OrderCancellationRequestStatuses.Pending)
                            .SetProperty(order => order.CancelRequestReason, normalizedReason)
                            .SetProperty(order => order.CancelRequestId, requestId)
                            .SetProperty(order => order.CancelRequestedAt, now), cancellationToken);
                    if (changed == 1)
                    {
                        logger.LogInformation(
                            "Order {OrderId} cancellation request recorded for customer {CustomerUserId} at {RequestedAtUtc}.",
                            orderId,
                            userId,
                            now);
                        return new CustomerCancellationResult(
                            CustomerCancellationFailure.None,
                            OrderCancellationRequestStatuses.Pending,
                            current.Status);
                    }

                    var latest = await db.Orders.AsNoTracking()
                        .Where(order => order.Id == orderId && order.UserId == userId)
                        .Select(order => new
                        {
                            order.Status,
                            order.CancelRequestStatus,
                            order.CancelRequestId
                        })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (latest is null)
                    {
                        return new CustomerCancellationResult(CustomerCancellationFailure.NotFound);
                    }

                    if (latest.CancelRequestId == requestId)
                    {
                        return new CustomerCancellationResult(
                            CustomerCancellationFailure.AlreadyApplied,
                            latest.CancelRequestStatus,
                            latest.Status);
                    }

                    if (latest.CancelRequestId is not null || latest.CancelRequestStatus is not null)
                    {
                        return new CustomerCancellationResult(
                            CustomerCancellationFailure.AlreadyRequested,
                            latest.CancelRequestStatus,
                            latest.Status);
                    }

                    return new CustomerCancellationResult(
                        CustomerCancellationFailure.InvalidState,
                        latest.CancelRequestStatus,
                        latest.Status);
                });

                if (result.Failure == CustomerCancellationFailure.InvalidState && attempt < 3)
                {
                    var latestStatus = result.OrderStatus;
                    if (latestStatus is OrderStatuses.Confirmed or OrderStatuses.Preparing)
                    {
                        continue;
                    }
                }

                return result;
            }
            catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 2601 or 2627)
            {
                logger.LogWarning(
                    "Order cancellation request id conflicted with an existing request (order {OrderId}).",
                    orderId);
                return new CustomerCancellationResult(CustomerCancellationFailure.DuplicateRequestId);
            }
            catch (Exception exception) when (exception is DbUpdateException or Microsoft.Data.SqlClient.SqlException)
            {
                logger.LogWarning(
                    "Order cancellation request persistence failed for order {OrderId} ({ExceptionType}).",
                    orderId,
                    exception.GetType().Name);
                return new CustomerCancellationResult(CustomerCancellationFailure.PersistenceFailure);
            }
        }

        return new CustomerCancellationResult(CustomerCancellationFailure.InvalidState);
    }

    private static void EnsureValidUserId(int userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }
    }
}
