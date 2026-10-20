using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class OrderCancellationReviewService(
    AppDbContext db,
    ILogger<OrderCancellationReviewService> logger) : IOrderCancellationReviewService
{
    public async Task<CancellationReviewResult> ReviewAsync(
        int orderId,
        int reviewerUserId,
        bool approve,
        string? decisionNote,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || reviewerUserId <= 0)
        {
            return new CancellationReviewResult(CancellationReviewFailure.NotFound);
        }

        var normalizedNote = string.IsNullOrWhiteSpace(decisionNote) ? null : decisionNote.Trim();
        if (normalizedNote?.Length > 1000 || (!approve && normalizedNote is null))
        {
            return new CancellationReviewResult(CancellationReviewFailure.InvalidDecisionNote);
        }

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

                var now = DateTime.UtcNow;
                var candidate = db.Orders.Where(item =>
                    item.Id == orderId &&
                    item.CancelRequestStatus == OrderCancellationRequestStatuses.Pending &&
                    (item.Status == OrderStatuses.Confirmed || item.Status == OrderStatuses.Preparing));

                var changed = approve
                    ? await candidate.ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.Status, OrderStatuses.Cancelled)
                        .SetProperty(item => item.CancelledAt, (DateTime?)now)
                        .SetProperty(item => item.CancelReason, item => item.CancelRequestReason)
                        .SetProperty(item => item.CancelRequestStatus, OrderCancellationRequestStatuses.Approved)
                        .SetProperty(item => item.CancelReviewedBy, (int?)reviewerUserId)
                        .SetProperty(item => item.CancelReviewedAt, (DateTime?)now)
                        .SetProperty(item => item.CancelDecisionNote, normalizedNote), cancellationToken)
                    : await candidate.ExecuteUpdateAsync(setters => setters
                        .SetProperty(item => item.CancelRequestStatus, OrderCancellationRequestStatuses.Rejected)
                        .SetProperty(item => item.CancelReviewedBy, (int?)reviewerUserId)
                        .SetProperty(item => item.CancelReviewedAt, (DateTime?)now)
                        .SetProperty(item => item.CancelDecisionNote, normalizedNote), cancellationToken);

                if (changed != 1)
                {
                    var current = await db.Orders.AsNoTracking()
                        .Where(item => item.Id == orderId)
                        .Select(item => new { item.Status, item.CancelRequestStatus })
                        .SingleOrDefaultAsync(cancellationToken);
                    await transaction.RollbackAsync(cancellationToken);
                    return new CancellationReviewResult(
                        current is null
                            ? CancellationReviewFailure.NotFound
                            : current.CancelRequestStatus is null
                                ? CancellationReviewFailure.InvalidState
                                : current.CancelRequestStatus == OrderCancellationRequestStatuses.Pending
                                    ? CancellationReviewFailure.InvalidState
                                    : CancellationReviewFailure.AlreadyReviewed,
                        current?.CancelRequestStatus);
                }

                if (approve)
                {
                    var lines = await db.OrderDetails.AsNoTracking()
                        .Where(line => line.OrderId == orderId)
                        .OrderBy(line => line.ProductId)
                        .ThenBy(line => line.Id)
                        .Select(line => new { line.ProductId, line.Quantity, line.Id })
                        .ToListAsync(cancellationToken);
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
                            throw new InvalidOperationException("Reserved inventory is missing for a cancellation order line.");
                        }

                        inventory.ReleaseReservation(line.Quantity);
                        db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            UserId = reviewerUserId,
                            OrderId = orderId,
                            TransactionType = "RELEASE",
                            Quantity = line.Quantity,
                            Note = "Giải phóng hàng sau khi duyệt yêu cầu hủy.",
                            CreatedAt = now
                        });
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation(
                    "Order {OrderId} cancellation request {Decision} by user {ReviewerUserId} at {ReviewedAtUtc}.",
                    orderId,
                    approve ? OrderCancellationRequestStatuses.Approved : OrderCancellationRequestStatuses.Rejected,
                    reviewerUserId,
                    now);
                return new CancellationReviewResult(
                    CancellationReviewFailure.None,
                    approve ? OrderCancellationRequestStatuses.Approved : OrderCancellationRequestStatuses.Rejected);
            });
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogWarning(
                "Cancellation review concurrency conflict for order {OrderId} ({ExceptionType}).",
                orderId,
                exception.GetType().Name);
            return new CancellationReviewResult(CancellationReviewFailure.ConcurrencyConflict);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException or InvalidOperationException)
        {
            logger.LogWarning(
                "Cancellation review persistence failed for order {OrderId} ({ExceptionType}).",
                orderId,
                exception.GetType().Name);
            return new CancellationReviewResult(CancellationReviewFailure.PersistenceFailure);
        }
    }
}
