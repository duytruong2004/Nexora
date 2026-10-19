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
                order.Details.Sum(line => line.Quantity)))
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
                    var productIds = lines.Select(line => line.ProductId).Distinct().ToArray();
                    var inventories = await db.Inventories
                        .Where(inventory => productIds.Contains(inventory.ProductId))
                        .OrderBy(inventory => inventory.ProductId)
                        .ToListAsync(cancellationToken);
                    var inventoryByProduct = inventories.ToDictionary(item => item.ProductId);
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

    private static void EnsureValidUserId(int userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }
    }
}
