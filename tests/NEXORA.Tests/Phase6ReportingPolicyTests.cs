using NEXORA.Application.Admin;

namespace NEXORA.Tests;

public sealed class Phase6ReportingPolicyTests
{
    [Fact]
    public void ReportRange_UsesVietnamLocalMidnightsAndUtcHalfOpenBounds()
    {
        var zone = ReportingBusinessTimeZone.Resolve();
        var range = new ReportDateRange(new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1));

        var (startUtc, endUtc) = range.ToUtcBounds(zone);

        Assert.Equal(DateTimeKind.Utc, startUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, endUtc.Kind);
        Assert.Equal(new DateTime(2026, 10, 30, 17, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 11, 1, 17, 0, 0, DateTimeKind.Utc), endUtc);
    }

    [Fact]
    public void ReportRange_RejectsReversedDatesAndUnrepresentableExclusiveEnd()
    {
        Assert.NotNull(new ReportDateRange(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)).ValidationError);
        Assert.NotNull(new ReportDateRange(new DateOnly(9999, 12, 31), DateOnly.MaxValue).ValidationError);
    }
}
