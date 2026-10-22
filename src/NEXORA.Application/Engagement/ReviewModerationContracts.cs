namespace NEXORA.Application.Engagement;

public enum ReviewModerationAction
{
    Approve,
    Hide
}

public enum ReviewModerationFailure
{
    None,
    AlreadyInState,
    NotFound,
    InvalidTransition,
    UnknownStatus,
    ConcurrencyConflict
}

public sealed record ReviewModerationResult(
    ReviewModerationFailure Failure,
    string? StatusBefore = null,
    string? StatusAfter = null)
{
    public bool Succeeded => Failure is ReviewModerationFailure.None or ReviewModerationFailure.AlreadyInState;
}

public sealed record ReviewModerationItem(
    int ReviewId,
    int ProductId,
    string ProductName,
    string ProductSnapshotName,
    string CustomerName,
    int Rating,
    string? Comment,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ReviewModerationPage(
    IReadOnlyList<ReviewModerationItem> Items,
    int TotalCount,
    int UnknownStatusCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record ReviewModerationFilter(string? Status, string? Search, int Page = 1, int PageSize = 25);

public interface IReviewModerationService
{
    Task<ReviewModerationPage> SearchAsync(ReviewModerationFilter filter, CancellationToken cancellationToken = default);
    Task<ReviewModerationResult> TransitionAsync(int reviewId, int actorUserId, ReviewModerationAction action, CancellationToken cancellationToken = default);
}
