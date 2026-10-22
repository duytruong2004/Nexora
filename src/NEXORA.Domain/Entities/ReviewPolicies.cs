namespace NEXORA.Domain.Entities;

public static class ReviewStatuses
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Hidden = "HIDDEN";

    public static bool IsKnown(string? status) => status is Pending or Approved or Hidden;
}

public static class ReviewModerationPolicy
{
    public static string? GetTargetStatus(string? currentStatus, bool approve)
    {
        if (!ReviewStatuses.IsKnown(currentStatus)) return null;

        if (approve)
        {
            return currentStatus is ReviewStatuses.Pending or ReviewStatuses.Hidden
                ? ReviewStatuses.Approved
                : currentStatus;
        }

        return currentStatus is ReviewStatuses.Pending or ReviewStatuses.Approved
            ? ReviewStatuses.Hidden
            : currentStatus;
    }
}

public static class ReviewContentPolicy
{
    public static string? Validate(int rating, string? comment)
    {
        if (rating is < 1 or > 5) return "Rating must be between 1 and 5.";
        return comment?.Length > 3000 ? "Review comment must not exceed 3000 characters." : null;
    }
}
