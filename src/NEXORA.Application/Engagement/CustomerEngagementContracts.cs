namespace NEXORA.Application.Engagement;

public sealed record FavoriteListItem(
    int ProductId,
    string ProductName,
    string Sku,
    string CategoryName,
    string BrandName,
    decimal Price,
    decimal? DiscountPrice,
    string? PrimaryImageUrl,
    DateTime CreatedAt);

public enum FavoriteFailure
{
    None,
    ProductNotFound,
    PersistenceFailure
}

public sealed record FavoriteOperationResult(FavoriteFailure Failure, bool AlreadyApplied = false)
{
    public bool Succeeded => Failure == FavoriteFailure.None;
}

public sealed record EligibleReviewOrderLine(
    int OrderId,
    DateTime OrderDate,
    int OrderDetailId,
    int ProductId,
    string ProductName,
    int Quantity)
{
    public string? ProductImageUrl { get; init; }
}

public sealed record CustomerReviewItem(
    int ReviewId,
    int OrderId,
    int OrderDetailId,
    int ProductId,
    string ProductName,
    int Rating,
    string? Comment,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public string? ProductImageUrl { get; init; }
}

public sealed record PublicProductReview(
    int ReviewId,
    string CustomerDisplayName,
    int Rating,
    string? Comment,
    DateTime CreatedAt);

public enum ReviewSubmissionFailure
{
    None,
    OrderLineNotFound,
    IneligibleOrder,
    AlreadyReviewed,
    InvalidRating,
    InvalidComment,
    PersistenceFailure
}

public sealed record ReviewSubmissionResult(ReviewSubmissionFailure Failure)
{
    public bool Succeeded => Failure == ReviewSubmissionFailure.None;
}

public interface IFavoriteService
{
    Task<IReadOnlyList<FavoriteListItem>> GetFavoritesAsync(int userId, CancellationToken cancellationToken = default);
    Task<FavoriteOperationResult> AddAsync(int userId, int productId, CancellationToken cancellationToken = default);
    Task<FavoriteOperationResult> RemoveAsync(int userId, int productId, CancellationToken cancellationToken = default);
}

public interface IReviewService
{
    Task<IReadOnlyList<EligibleReviewOrderLine>> GetEligibleOrderLinesAsync(int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerReviewItem>> GetCustomerReviewsAsync(int userId, CancellationToken cancellationToken = default);
    Task<ReviewSubmissionResult> SubmitAsync(int userId, int orderDetailId, int rating, string? comment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PublicProductReview>> GetApprovedForProductAsync(int productId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
}
