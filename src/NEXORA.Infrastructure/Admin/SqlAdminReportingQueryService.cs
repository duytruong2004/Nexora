using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Admin;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Admin;

public sealed class SqlAdminReportingQueryService(AppDbContext db) : IAdminReportingQueryService
{
    private const int MaximumPageSize = 100;
    private const string SqlVietnamTimeZone = "SE Asia Standard Time";

    public async Task<AdminOrdersReport> GetOrdersAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        var (startUtc, endExclusiveUtc) = range.ToUtcBounds(ReportingBusinessTimeZone.Resolve());
        var grouped = await db.Orders.AsNoTracking()
            .Where(order => order.OrderDate >= startUtc && order.OrderDate < endExclusiveUtc)
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .OrderBy(row => row.Status)
            .ToListAsync(cancellationToken);
        var statusCounts = grouped
            .Select(row => new OrderStatusCount(row.Status, row.Count))
            .ToArray();
        return new AdminOrdersReport(range, statusCounts, grouped.Sum(row => row.Count));
    }

    public async Task<AdminRevenueReport> GetRevenueAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        var (startUtc, endExclusiveUtc) = range.ToUtcBounds(ReportingBusinessTimeZone.Resolve());
        var completedOrders = db.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatuses.Completed && order.CompletedAt.HasValue &&
                            order.CompletedAt.Value >= startUtc && order.CompletedAt.Value < endExclusiveUtc);

        // Keep order-level total independent from OrderDetails to prevent 1:N join fan-out.
        var total = await completedOrders.SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m;
        var daily = await completedOrders
            .GroupBy(order => EF.Functions.AtTimeZone(
                EF.Functions.AtTimeZone(order.CompletedAt!.Value, "UTC"), SqlVietnamTimeZone).Date)
            .Select(group => new { LocalDate = group.Key, Amount = group.Sum(order => order.TotalAmount) })
            .OrderBy(group => group.LocalDate)
            .ToListAsync(cancellationToken);

        return new AdminRevenueReport(
            range,
            total,
            daily.Select(item => new RevenueByLocalDate(DateOnly.FromDateTime(item.LocalDate), item.Amount)).ToArray());
    }

    public async Task<AdminProductSalesReport> GetProductSalesAsync(
        ReportDateRange range,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var (startUtc, endExclusiveUtc) = range.ToUtcBounds(ReportingBusinessTimeZone.Resolve());
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var grouped = db.OrderDetails.AsNoTracking()
            .Where(line => line.Order.Status == OrderStatuses.Completed && line.Order.CompletedAt.HasValue &&
                           line.Order.CompletedAt.Value >= startUtc && line.Order.CompletedAt.Value < endExclusiveUtc)
            .GroupBy(line => line.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                QuantitySold = group.Sum(line => line.Quantity),
                // This label is selected from the immutable order-line snapshots; current product data/prices are not used.
                ProductNameSnapshot = group.Max(line => line.ProductName) ?? string.Empty
            });

        var totalProducts = await grouped.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalProducts / (double)pageSize));
        page = Math.Clamp(page, 1, pageCount);
        var rows = await grouped.OrderByDescending(row => row.QuantitySold)
            .ThenBy(row => row.ProductId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new ProductSalesRow(row.ProductId, row.ProductNameSnapshot, row.QuantitySold))
            .ToListAsync(cancellationToken);
        return new AdminProductSalesReport(range, rows, totalProducts, page, pageSize);
    }

    public async Task<AdminInventoryReport> GetInventoryAsync(
        int threshold,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var query = from product in db.Products.AsNoTracking()
                    join inventoryRow in db.Inventories.AsNoTracking() on product.Id equals inventoryRow.ProductId into inventoryRows
                    from inventory in inventoryRows.DefaultIfEmpty()
                    select new
                    {
                        product.Id,
                        product.Name,
                        product.Sku,
                        QuantityInStock = inventory == null ? (int?)null : inventory.QuantityInStock,
                        ReservedQuantity = inventory == null ? (int?)null : inventory.ReservedQuantity,
                        AvailableQuantity = inventory == null
                            ? (int?)null
                            : inventory.QuantityInStock - inventory.ReservedQuantity,
                        Status = inventory == null
                            ? "MISSING"
                            : inventory.QuantityInStock < 0 || inventory.ReservedQuantity < 0 ||
                              inventory.ReservedQuantity > inventory.QuantityInStock
                                ? "ANOMALY"
                                : inventory.QuantityInStock == inventory.ReservedQuantity
                                    ? "OUT_OF_STOCK"
                                    : inventory.QuantityInStock - inventory.ReservedQuantity <= threshold
                                        ? "LOW"
                                        : "NORMAL"
                    };

        var countRows = await query.GroupBy(row => row.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var countsByStatus = countRows.ToDictionary(row => row.Status, row => row.Count, StringComparer.Ordinal);
        var totalCount = countRows.Sum(row => row.Count);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, pageCount);
        var rows = await query
            .OrderBy(row => row.Status)
            .ThenBy(row => row.Name)
            .ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new InventoryReportRow(
                row.Id,
                row.Name,
                row.Sku,
                row.QuantityInStock,
                row.ReservedQuantity,
                row.AvailableQuantity,
                row.Status == "NORMAL" ? InventoryReportStatus.Normal
                    : row.Status == "LOW" ? InventoryReportStatus.Low
                    : row.Status == "OUT_OF_STOCK" ? InventoryReportStatus.OutOfStock
                    : row.Status == "MISSING" ? InventoryReportStatus.Missing
                    : InventoryReportStatus.Anomaly))
            .ToListAsync(cancellationToken);

        int Count(string key) => countsByStatus.GetValueOrDefault(key);
        return new AdminInventoryReport(threshold,
            new InventoryReportCounts(Count("NORMAL"), Count("LOW"), Count("OUT_OF_STOCK"), Count("MISSING"), Count("ANOMALY")),
            rows, totalCount, page, pageSize);
    }

}
