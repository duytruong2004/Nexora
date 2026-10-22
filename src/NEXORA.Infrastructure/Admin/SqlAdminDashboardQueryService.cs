using Microsoft.EntityFrameworkCore;
using NEXORA.Application.Admin;
using NEXORA.Domain.Entities;
using NEXORA.Infrastructure.Persistence;

namespace NEXORA.Infrastructure.Admin;

public sealed class SqlAdminDashboardQueryService(AppDbContext db) : IAdminDashboardQueryService
{
    public async Task<AdminDashboardMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var totalOrders = await db.Orders.AsNoTracking().CountAsync(cancellationToken);
        return new AdminDashboardMetrics(
            await db.Products.AsNoTracking().CountAsync(cancellationToken),
            await db.Products.AsNoTracking().CountAsync(product => product.Status == ProductStatuses.Active, cancellationToken),
            await db.Categories.AsNoTracking().CountAsync(cancellationToken),
            await db.UsageNeeds.AsNoTracking().CountAsync(cancellationToken),
            await db.RecommendationRules.AsNoTracking().CountAsync(cancellationToken))
        {
            TotalOrders = totalOrders,
            PendingOrders = await db.Orders.AsNoTracking().CountAsync(order => order.Status == OrderStatuses.Pending, cancellationToken),
            CompletedOrders = await db.Orders.AsNoTracking().CountAsync(order => order.Status == OrderStatuses.Completed, cancellationToken),
            ProductsWithoutInventory = await db.Products.AsNoTracking()
                .CountAsync(product => !db.Inventories.Any(inventory => inventory.ProductId == product.Id), cancellationToken)
        };
    }
}
