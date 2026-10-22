using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NEXORA.Application.Admin;
using NEXORA.Domain.Entities;
using NEXORA.Domain.Security;
using NEXORA.Web.Areas.Admin.Models;

namespace NEXORA.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = RoleNames.Admin)]
public sealed class ReportsController(IAdminReportingQueryService reports) : Controller
{
    private const int MaximumReportPageSize = 25;

    [HttpGet]
    public async Task<IActionResult> Orders(DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var (start, end) = ResolveDates(startDate, endDate);
        var range = new ReportDateRange(start, end);
        if (!ModelState.IsValid)
            return View(new AdminOrdersReportViewModel { StartDate = start, EndDate = end, Error = "Ngày lọc không hợp lệ." });
        if (range.ValidationError is { } error)
            return View(new AdminOrdersReportViewModel { StartDate = start, EndDate = end, Error = error });

        return View(new AdminOrdersReportViewModel
        {
            StartDate = start,
            EndDate = end,
            Report = await reports.GetOrdersAsync(range, cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Revenue(DateOnly? startDate, DateOnly? endDate, CancellationToken cancellationToken)
    {
        var (start, end) = ResolveDates(startDate, endDate);
        var range = new ReportDateRange(start, end);
        if (!ModelState.IsValid)
            return View(new AdminRevenueReportViewModel { StartDate = start, EndDate = end, Error = "Ngày lọc không hợp lệ." });
        if (range.ValidationError is { } error)
            return View(new AdminRevenueReportViewModel { StartDate = start, EndDate = end, Error = error });

        return View(new AdminRevenueReportViewModel
        {
            StartDate = start,
            EndDate = end,
            Report = await reports.GetRevenueAsync(range, cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Products(DateOnly? startDate, DateOnly? endDate, int page = 1, CancellationToken cancellationToken = default)
    {
        var (start, end) = ResolveDates(startDate, endDate);
        var range = new ReportDateRange(start, end);
        if (!ModelState.IsValid)
            return View(new AdminProductSalesReportViewModel { StartDate = start, EndDate = end, Error = "Ngày lọc không hợp lệ." });
        if (range.ValidationError is { } error)
            return View(new AdminProductSalesReportViewModel { StartDate = start, EndDate = end, Error = error });

        return View(new AdminProductSalesReportViewModel
        {
            StartDate = start,
            EndDate = end,
            Report = await reports.GetProductSalesAsync(range, page, MaximumReportPageSize, cancellationToken)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Inventory(int? threshold, int page = 1, CancellationToken cancellationToken = default)
    {
        var value = threshold ?? 5;
        if (!ModelState.IsValid || value < 0)
        {
            return View(new AdminInventoryReportViewModel { Threshold = value, Error = "Ngưỡng phải là số nguyên không âm." });
        }

        return View(new AdminInventoryReportViewModel
        {
            Threshold = value,
            Report = await reports.GetInventoryAsync(value, page, MaximumReportPageSize, cancellationToken)
        });
    }

    private static (DateOnly Start, DateOnly End) ResolveDates(DateOnly? startDate, DateOnly? endDate)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ReportingBusinessTimeZone.Resolve()));
        var end = endDate ?? today;
        var start = startDate ?? new DateOnly(end.Year, end.Month, 1);
        return (start, end);
    }
}
