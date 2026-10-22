using NEXORA.Domain.Entities;

namespace NEXORA.Tests;

public sealed class Phase6EngagementPolicyTests
{
    [Theory]
    [InlineData(ReviewStatuses.Pending, true, ReviewStatuses.Approved)]
    [InlineData(ReviewStatuses.Pending, false, ReviewStatuses.Hidden)]
    [InlineData(ReviewStatuses.Approved, true, ReviewStatuses.Approved)]
    [InlineData(ReviewStatuses.Approved, false, ReviewStatuses.Hidden)]
    [InlineData(ReviewStatuses.Hidden, true, ReviewStatuses.Approved)]
    [InlineData(ReviewStatuses.Hidden, false, ReviewStatuses.Hidden)]
    public void ModerationPolicy_ReturnsOnlyApprovedTransitions(string current, bool approve, string expected)
    {
        Assert.Equal(expected, ReviewModerationPolicy.GetTargetStatus(current, approve));
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("pending")]
    [InlineData("approved")]
    [InlineData("")]
    public void UnknownOrNonCanonicalStatus_IsNotTransitioned(string status)
    {
        Assert.Null(ReviewModerationPolicy.GetTargetStatus(status, approve: true));
        Assert.False(ReviewStatuses.IsKnown(status));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(5, "Useful")]
    [InlineData(6, null)]
    public void ReviewContentPolicy_ValidatesRatingAndCommentLength(int rating, string? comment)
    {
        var error = ReviewContentPolicy.Validate(rating, comment);
        Assert.Equal(rating is >= 1 and <= 5 ? null : "Rating must be between 1 and 5.", error);
    }

    [Fact]
    public void ReviewContentPolicy_RejectsCommentOverThreeThousandCharacters()
    {
        Assert.NotNull(ReviewContentPolicy.Validate(5, new string('x', 3001)));
        Assert.Null(ReviewContentPolicy.Validate(5, new string('x', 3000)));
    }
}
