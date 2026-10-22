using NEXORA.Application.Admin;

namespace NEXORA.Web.Areas.Admin.Models;

public sealed class AdminOrdersReportViewModel
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string? Error { get; init; }
    public AdminOrdersReport? Report { get; init; }
}

public sealed class AdminRevenueReportViewModel
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string? Error { get; init; }
    public AdminRevenueReport? Report { get; init; }
}

public sealed class AdminProductSalesReportViewModel
{
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public string? Error { get; init; }
    public AdminProductSalesReport? Report { get; init; }
}

public sealed class AdminInventoryReportViewModel
{
    public int Threshold { get; init; }
    public string? Error { get; init; }
    public AdminInventoryReport? Report { get; init; }
}
