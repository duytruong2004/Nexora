using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NEXORA.Application.Engagement;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Engagement;

public sealed class CustomerEngagementService(
    AppDbContext db,
    ILogger<CustomerEngagementService> logger) : IFavoriteService, IReviewService
{
    public async Task<IReadOnlyList<FavoriteListItem>> GetFavoritesAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        return await db.Favorites.AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .OrderByDescending(favorite => favorite.CreatedAt)
            .Select(favorite => new FavoriteListItem(
                favorite.ProductId,
                favorite.Product.Name,
                favorite.Product.Sku,
                favorite.Product.Category.Name,
                favorite.Product.Brand.Name,
                favorite.Product.Price,
                favorite.Product.DiscountPrice,
                favorite.Product.Images.Where(image => image.IsPrimary)
                    .Select(image => image.ImageUrl).FirstOrDefault(),
                favorite.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<FavoriteOperationResult> AddAsync(
        int userId,
        int productId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        if (productId <= 0 || !await db.Products.AsNoTracking()
                .AnyAsync(product => product.Id == productId && product.Status == ProductStatuses.Active &&
                                     product.Category.IsActive && product.Brand.IsActive, cancellationToken))
        {
            return new FavoriteOperationResult(FavoriteFailure.ProductNotFound);
        }

        if (await db.Favorites.AsNoTracking().AnyAsync(
                favorite => favorite.UserId == userId && favorite.ProductId == productId, cancellationToken))
        {
            return new FavoriteOperationResult(FavoriteFailure.None, AlreadyApplied: true);
        }

        var favorite = new Favorite { UserId = userId, ProductId = productId, CreatedAt = DateTime.UtcNow };
        db.Favorites.Add(favorite);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new FavoriteOperationResult(FavoriteFailure.None);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // The unique (user_id, product_id) index makes concurrent duplicate POSTs idempotent.
            db.Entry(favorite).State = EntityState.Detached;
            return new FavoriteOperationResult(FavoriteFailure.None, AlreadyApplied: true);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException)
        {
            logger.LogWarning("Favorite could not be saved for customer {CustomerUserId} and product {ProductId} ({ExceptionType}).",
                userId, productId, exception.GetType().Name);
            return new FavoriteOperationResult(FavoriteFailure.PersistenceFailure);
        }
    }

    public async Task<FavoriteOperationResult> RemoveAsync(
        int userId,
        int productId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        if (productId <= 0) return new FavoriteOperationResult(FavoriteFailure.ProductNotFound);

        try
        {
            await db.Favorites.Where(favorite => favorite.UserId == userId && favorite.ProductId == productId)
                .ExecuteDeleteAsync(cancellationToken);
            return new FavoriteOperationResult(FavoriteFailure.None);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException)
        {
            logger.LogWarning("Favorite removal failed for customer {CustomerUserId} and product {ProductId} ({ExceptionType}).",
                userId, productId, exception.GetType().Name);
            return new FavoriteOperationResult(FavoriteFailure.PersistenceFailure);
        }
    }

    public async Task<IReadOnlyList<EligibleReviewOrderLine>> GetEligibleOrderLinesAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        return await db.OrderDetails.AsNoTracking()
            .Where(line => line.Order.UserId == userId && line.Order.Status == OrderStatuses.Completed &&
                           !line.Reviews.Any())
            .OrderByDescending(line => line.Order.CompletedAt)
            .ThenByDescending(line => line.Id)
            .Select(line => new EligibleReviewOrderLine(
                line.OrderId,
                line.Order.OrderDate,
                line.Id,
                line.ProductId,
                line.ProductName,
                line.Quantity)
            {
                ProductImageUrl = line.Product.Images
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerReviewItem>> GetCustomerReviewsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        return await db.Reviews.AsNoTracking()
            .Where(review => review.UserId == userId)
            .OrderByDescending(review => review.CreatedAt)
            .Select(review => new CustomerReviewItem(
                review.Id,
                review.OrderId,
                review.OrderDetailId,
                review.ProductId,
                review.OrderDetail.ProductName,
                review.Rating,
                review.Comment,
                review.Status,
                review.CreatedAt,
                review.UpdatedAt)
            {
                ProductImageUrl = review.Product.Images
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ReviewSubmissionResult> SubmitAsync(
        int userId,
        int orderDetailId,
        int rating,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        EnsureUserId(userId);
        if (orderDetailId <= 0) return new ReviewSubmissionResult(ReviewSubmissionFailure.OrderLineNotFound);
        var contentError = ReviewContentPolicy.Validate(rating, comment);
        if (contentError is not null)
        {
            return new ReviewSubmissionResult(rating is < 1 or > 5
                ? ReviewSubmissionFailure.InvalidRating
                : ReviewSubmissionFailure.InvalidComment);
        }

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable, cancellationToken);

                var line = await db.OrderDetails.AsNoTracking()
                    .Where(item => item.Id == orderDetailId && item.Order.UserId == userId)
                    .Select(item => new
                    {
                        item.Id,
                        item.OrderId,
                        item.ProductId,
                        OrderUserId = item.Order.UserId,
                        OrderStatus = item.Order.Status
                    })
                    .SingleOrDefaultAsync(cancellationToken);
                if (line is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new ReviewSubmissionResult(ReviewSubmissionFailure.OrderLineNotFound);
                }

                var order = new Order { Id = line.OrderId, UserId = line.OrderUserId, Status = line.OrderStatus };
                var orderLine = new OrderDetail { Id = line.Id, OrderId = line.OrderId, ProductId = line.ProductId };
                var candidate = new Review
                {
                    UserId = userId,
                    OrderId = line.OrderId,
                    OrderDetailId = line.Id,
                    ProductId = line.ProductId
                };
                if (ReviewEligibilityPolicy.Validate(candidate, order, orderLine) is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new ReviewSubmissionResult(ReviewSubmissionFailure.IneligibleOrder);
                }

                if (await db.Reviews.AsNoTracking().AnyAsync(review => review.OrderDetailId == orderDetailId, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return new ReviewSubmissionResult(ReviewSubmissionFailure.AlreadyReviewed);
                }

                var now = DateTime.UtcNow;
                candidate.Rating = rating;
                candidate.Comment = normalizedComment;
                candidate.Status = ReviewStatuses.Pending;
                candidate.CreatedAt = now;
                candidate.UpdatedAt = now;
                db.Reviews.Add(candidate);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ReviewSubmissionResult(ReviewSubmissionFailure.None);
            });
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return new ReviewSubmissionResult(ReviewSubmissionFailure.AlreadyReviewed);
        }
        catch (Exception exception) when (exception is DbUpdateException or SqlException or InvalidOperationException)
        {
            logger.LogWarning("Review submission failed for customer {CustomerUserId}, order detail {OrderDetailId} ({ExceptionType}).",
                userId, orderDetailId, exception.GetType().Name);
            return new ReviewSubmissionResult(ReviewSubmissionFailure.PersistenceFailure);
        }
    }

    public async Task<IReadOnlyList<PublicProductReview>> GetApprovedForProductAsync(
        int productId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        if (productId <= 0) return [];
        pageSize = Math.Clamp(pageSize, 1, 50);
        page = Math.Max(1, page);
        return await (from review in db.Reviews.AsNoTracking()
                      where review.ProductId == productId &&
                            EF.Functions.Collate(review.Status, "Latin1_General_100_BIN2") == ReviewStatuses.Approved
                      orderby review.CreatedAt descending, review.Id descending
                      select new PublicProductReview(
                          review.Id,
                          "Khách hàng",
                          review.Rating,
                          review.Comment,
                          review.CreatedAt))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.GetBaseException() is SqlException { Number: 2601 or 2627 };

    private static void EnsureUserId(int userId)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
    }
}
