using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;
using System.Diagnostics;

namespace NEXORA.Infrastructure.Engagement;

public sealed class ReviewModerationService(
    AppDbContext db,
    ILogger<ReviewModerationService> logger) : IReviewModerationService
{
    private const int MaximumPageSize = 100;

    public async Task<ReviewModerationPage> SearchAsync(
        ReviewModerationFilter filter,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);
        var baseQuery = from review in db.Reviews.AsNoTracking()
                        join user in db.Users.AsNoTracking() on review.UserId equals user.Id
                        select new { Review = review, CustomerName = user.FullName };

        var unknownStatusCount = await baseQuery.CountAsync(row =>
            EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") != ReviewStatuses.Pending &&
            EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") != ReviewStatuses.Approved &&
            EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") != ReviewStatuses.Hidden, cancellationToken);

        if (filter.Status == "UNKNOWN")
        {
            baseQuery = baseQuery.Where(row =>
                EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") != ReviewStatuses.Pending &&
                EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") != ReviewStatuses.Approved &&
                EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") != ReviewStatuses.Hidden);
        }
        else if (filter.Status is ReviewStatuses.Pending or ReviewStatuses.Approved or ReviewStatuses.Hidden)
        {
            baseQuery = baseQuery.Where(row =>
                EF.Functions.Collate(row.Review.Status, "Latin1_General_100_BIN2") == filter.Status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            baseQuery = baseQuery.Where(row => row.Review.Product.Name.Contains(term) ||
                                               row.Review.OrderDetail.ProductName.Contains(term) ||
                                               row.CustomerName.Contains(term) ||
                                               (row.Review.Comment != null && row.Review.Comment.Contains(term)));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var items = await baseQuery
            .OrderBy(row => row.Review.Status == ReviewStatuses.Pending ? 0 : 1)
            .ThenByDescending(row => row.Review.CreatedAt)
            .ThenByDescending(row => row.Review.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new ReviewModerationItem(
                row.Review.Id,
                row.Review.ProductId,
                row.Review.Product.Name,
                row.Review.OrderDetail.ProductName,
                row.CustomerName,
                row.Review.Rating,
                row.Review.Comment,
                row.Review.Status,
                row.Review.CreatedAt,
                row.Review.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new ReviewModerationPage(items, totalCount, unknownStatusCount, page, pageSize);
    }

    public async Task<ReviewModerationResult> TransitionAsync(
        int reviewId,
        int actorUserId,
        ReviewModerationAction action,
        CancellationToken cancellationToken = default)
    {
        if (reviewId <= 0 || actorUserId <= 0)
            return new ReviewModerationResult(ReviewModerationFailure.NotFound);

        var currentStatus = await db.Reviews.AsNoTracking()
            .Where(review => review.Id == reviewId)
            .Select(review => review.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (currentStatus is null) return new ReviewModerationResult(ReviewModerationFailure.NotFound);
        if (!ReviewStatuses.IsKnown(currentStatus))
        {
            LogDecision(actorUserId, reviewId, action, currentStatus, null, "unknown_status_rejected", warning: true);
            return new ReviewModerationResult(ReviewModerationFailure.UnknownStatus, currentStatus);
        }

        var target = ReviewModerationPolicy.GetTargetStatus(currentStatus, action == ReviewModerationAction.Approve);
        if (target is null)
            return new ReviewModerationResult(ReviewModerationFailure.UnknownStatus, currentStatus);
        if (target == currentStatus)
        {
            LogDecision(actorUserId, reviewId, action, currentStatus, currentStatus, "already_in_state");
            return new ReviewModerationResult(ReviewModerationFailure.AlreadyInState, currentStatus, currentStatus);
        }

        var now = DateTime.UtcNow;
        var changed = await db.Reviews
            .Where(review => review.Id == reviewId &&
                            EF.Functions.Collate(review.Status, "Latin1_General_100_BIN2") == currentStatus)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(review => review.Status, target)
                .SetProperty(review => review.UpdatedAt, now), cancellationToken);
        if (changed != 1)
        {
            LogDecision(actorUserId, reviewId, action, currentStatus, target, "concurrency_conflict", warning: true);
            return new ReviewModerationResult(ReviewModerationFailure.ConcurrencyConflict, currentStatus);
        }

        LogDecision(actorUserId, reviewId, action, currentStatus, target, "updated");
        return new ReviewModerationResult(ReviewModerationFailure.None, currentStatus, target);
    }

    private void LogDecision(int actorId, int reviewId, ReviewModerationAction action,
        string? before, string? after, string outcome, bool warning = false)
    {
        var correlationId = Activity.Current?.TraceId.ToString();
        if (warning)
        {
            logger.LogWarning(
                "Review moderation {Outcome}: actor {ActorUserId}, review {ReviewId}, action {Action}, status {StatusBefore}->{StatusAfter}, correlation {CorrelationId}.",
                outcome, actorId, reviewId, action, before, after, correlationId);
        }
        else
        {
            logger.LogInformation(
                "Review moderation {Outcome}: actor {ActorUserId}, review {ReviewId}, action {Action}, status {StatusBefore}->{StatusAfter}, correlation {CorrelationId}.",
                outcome, actorId, reviewId, action, before, after, correlationId);
        }
    }
}
