namespace NEXORA.Application.Admin;

public static class ReportingBusinessTimeZone
{
    public static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException) when (OperatingSystem.IsWindows())
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
    }
}

public sealed record ReportDateRange(DateOnly StartDate, DateOnly EndDate)
{
    public string? ValidationError => StartDate > EndDate
        ? "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc."
        : EndDate == DateOnly.MaxValue
            ? "Ngày kết thúc vượt quá phạm vi có thể truy vấn."
            : null;

    public (DateTime StartUtc, DateTime EndExclusiveUtc) ToUtcBounds(TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(businessTimeZone);
        if (ValidationError is not null) throw new InvalidOperationException(ValidationError);

        var localStart = DateTime.SpecifyKind(StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return (TimeZoneInfo.ConvertTimeToUtc(localStart, businessTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, businessTimeZone));
    }
}

public sealed record OrderStatusCount(string Status, int Count);
public sealed record AdminOrdersReport(ReportDateRange Range, IReadOnlyList<OrderStatusCount> StatusCounts, int TotalOrders);
public sealed record RevenueByLocalDate(DateOnly Date, decimal Amount);
public sealed record AdminRevenueReport(ReportDateRange Range, decimal TotalRevenue, IReadOnlyList<RevenueByLocalDate> DailyRevenue);
public sealed record ProductSalesRow(int ProductId, string ProductNameSnapshot, int QuantitySold);
public sealed record AdminProductSalesReport(ReportDateRange Range, IReadOnlyList<ProductSalesRow> Items, int TotalProducts, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalProducts / (double)PageSize));
}

public enum InventoryReportStatus
{
    Normal,
    Low,
    OutOfStock,
    Missing,
    Anomaly
}

public sealed record InventoryReportRow(
    int ProductId,
    string ProductName,
    string Sku,
    int? QuantityInStock,
    int? ReservedQuantity,
    int? AvailableQuantity,
    InventoryReportStatus Status);

public sealed record InventoryReportCounts(int Normal, int Low, int OutOfStock, int Missing, int Anomaly);
public sealed record AdminInventoryReport(int Threshold, InventoryReportCounts Counts, IReadOnlyList<InventoryReportRow> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public interface IAdminReportingQueryService
{
    Task<AdminOrdersReport> GetOrdersAsync(ReportDateRange range, CancellationToken cancellationToken = default);
    Task<AdminRevenueReport> GetRevenueAsync(ReportDateRange range, CancellationToken cancellationToken = default);
    Task<AdminProductSalesReport> GetProductSalesAsync(ReportDateRange range, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<AdminInventoryReport> GetInventoryAsync(int threshold, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
}
