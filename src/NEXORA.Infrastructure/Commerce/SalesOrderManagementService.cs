using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Commerce;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Commerce;

public sealed class SalesOrderManagementService(
    AppDbContext db,
    ILogger<SalesOrderManagementService> logger) : ISalesOrderManagementService
{
    private const int MaximumPageSize = 50;
    private static readonly string[] ValidStatuses =
    [
        OrderStatuses.Pending,
        OrderStatuses.Confirmed,
        OrderStatuses.Preparing,
        OrderStatuses.Exported,
        OrderStatuses.Shipping,
        OrderStatuses.Completed,
        OrderStatuses.Cancelled
    ];
    private static readonly string[] ValidCancellationStatuses =
    [
        OrderCancellationRequestStatuses.Pending,
        OrderCancellationRequestStatuses.Approved,
        OrderCancellationRequestStatuses.Rejected
    ];

    public async Task<StaffOrderPage> SearchAsync(
        StaffOrderFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var requestedPage = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search.Trim();
        if (search is { Length: > 100 })
        {
            search = search[..100];
        }
        var status = string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status.Trim().ToUpperInvariant();
        var cancelRequestStatus = string.IsNullOrWhiteSpace(filter.CancelRequestStatus)
            ? null
            : filter.CancelRequestStatus.Trim().ToUpperInvariant();

        IQueryable<Order> query = db.Orders.AsNoTracking();
        if (search is not null)
        {
            if (int.TryParse(search, out var orderId))
            {
                query = query.Where(order => order.Id == orderId ||
                    order.CustomerName.Contains(search) || order.Phone.Contains(search));
            }
            else
            {
                query = query.Where(order => order.CustomerName.Contains(search) || order.Phone.Contains(search));
            }
        }

        if (status is not null)
        {
            query = ValidStatuses.Contains(status)
                ? query.Where(order => order.Status == status)
                : query.Where(_ => false);
        }

        if (cancelRequestStatus is not null)
        {
            query = ValidCancellationStatuses.Contains(cancelRequestStatus)
                ? query.Where(order => order.CancelRequestStatus == cancelRequestStatus)
                : query.Where(_ => false);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Min(requestedPage, pageCount);
        var items = await query
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new StaffOrderListItem(
                order.Id,
                order.OrderDate,
                order.Status,
                order.CustomerName,
                order.Phone,
                order.TotalAmount,
                order.Details.Sum(line => line.Quantity))
            {
                CancelRequestStatus = order.CancelRequestStatus,
                CancelRequestedAt = order.CancelRequestedAt
            })
            .ToListAsync(cancellationToken);

        return new StaffOrderPage(items, totalCount, page, pageSize);
    }

    public async Task<StaffOrderDetail?> GetAsync(int orderId, CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
        {
            return null;
        }

        var order = await db.Orders.AsNoTracking()
            .Include(item => item.Details)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return new StaffOrderDetail(
            order.Id,
            order.UserId,
            order.OrderDate,
            order.Status,
            order.CustomerName,
            order.Phone,
            order.ShippingAddress,
            order.Note,
            order.ConfirmedBy,
            order.ConfirmedAt,
            order.CancelledAt,
            order.CancelReason,
            order.CompletedAt,
            order.TotalAmount,
            order.Details.OrderBy(line => line.Id)
                .Select(line => new StaffOrderDetailLine(
                    line.ProductId,
                    line.ProductName,
                    line.Quantity,
                    line.UnitPrice,
                    line.DiscountAmount,
                    line.TotalPrice))
                .ToArray())
        {
            CancelRequestStatus = order.CancelRequestStatus,
            CancelRequestReason = order.CancelRequestReason,
            CancelRequestId = order.CancelRequestId,
            CancelRequestedAt = order.CancelRequestedAt,
            CancelReviewedBy = order.CancelReviewedBy,
            CancelReviewedAt = order.CancelReviewedAt,
            CancelDecisionNote = order.CancelDecisionNote
        };
    }

    public async Task<OrderTransitionResult> TransitionAsync(
        int orderId,
        int actorUserId,
        OrderWorkflowAction action,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
        {
            return new OrderTransitionResult(OrderTransitionFailure.NotFound);
        }

        if (actorUserId <= 0 || !OrderWorkflowRules.TryGetTransition(action, out var transition))
        {
            return new OrderTransitionResult(OrderTransitionFailure.InvalidState);
        }

        var now = DateTime.UtcNow;
        var currentOrder = db.Orders.Where(order => order.Id == orderId && order.Status == transition.FromStatus);
        var affected = action switch
        {
            OrderWorkflowAction.Confirm => await currentOrder.ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.Status, transition.ToStatus)
                .SetProperty(order => order.ConfirmedBy, (int?)actorUserId)
                .SetProperty(order => order.ConfirmedAt, (DateTime?)now), cancellationToken),
            OrderWorkflowAction.Complete => await currentOrder.ExecuteUpdateAsync(setters => setters
                .SetProperty(order => order.Status, transition.ToStatus)
                .SetProperty(order => order.CompletedAt, (DateTime?)now), cancellationToken),
            OrderWorkflowAction.HandoffToWarehouse or OrderWorkflowAction.MarkShipping =>
                await currentOrder.ExecuteUpdateAsync(setters => setters
                    .SetProperty(order => order.Status, transition.ToStatus), cancellationToken),
            _ => 0
        };

        if (affected == 1)
        {
            logger.LogInformation(
                "Order {OrderId} transitioned from {FromStatus} to {ToStatus} by user {ActorUserId} at {OccurredAtUtc}.",
                orderId,
                transition.FromStatus,
                transition.ToStatus,
                actorUserId,
                now);
            return new OrderTransitionResult(OrderTransitionFailure.None);
        }

        var currentStatus = await db.Orders.AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentStatus is null)
        {
            return new OrderTransitionResult(OrderTransitionFailure.NotFound);
        }

        if (currentStatus == transition.ToStatus)
        {
            return new OrderTransitionResult(OrderTransitionFailure.AlreadyApplied);
        }

        return new OrderTransitionResult(OrderTransitionFailure.InvalidState);
    }
}
